using Newtonsoft.Json;
using opentuner.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using System.Drawing;
using Serilog;

namespace opentuner.MediaSources.Longmynd
{
    public partial class LongmyndSource
    {
        // ws interface
        private WebSocket controlWS;        // longmynd control ws websocket
        private WebSocket monitorWS;        // longmynd monitor ws websocket

        // Set by Close() so the OnClose handlers below stop reconnecting once the app is shutting
        // down (issue #16) - otherwise our own Close() triggers yet another Connect().
        private volatile bool _closing;

        // Pause between reconnect attempts. A refused connection returns immediately, so without
        // it an unreachable host would spin; a timing-out one (no route) already takes ~20 s.
        private const int ReconnectDelayMs = 3000;


        public void WSSetTS(string ip, int port)
        {
            Log.Information(ip.ToString() + " - " + port.ToString());
            controlWS.Send("U" + ip + ":" + port);
        }

        private void WSSetFrequency(uint frequency, uint symbol_rate)
        {
            controlWS.Send("C" + (frequency - _settings.Offset1).ToString() + "," + symbol_rate.ToString());
        }

        private void connectWebsockets()
        {
            _closing = false;   // a re-connect after Close() must reconnect again

            string url = "ws://" + _settings.LongmyndWSHost + ":" + _settings.LongmyndWSPort.ToString() + "/ ";

            monitorWS = new WebSocket(url, "monitor");
            monitorWS.OnOpen += Monitorws_OnOpen;
            monitorWS.OnMessage += Monitorws_OnMessage;
            monitorWS.OnClose += Monitorws_OnClose;
            System.Threading.Tasks.Task.Run(() => monitorWS.Connect());

            controlWS = new WebSocket(url, "control");
            controlWS.OnClose += Controlws_OnClose;
            controlWS.OnMessage += Controlws_OnMessage;
            controlWS.OnOpen += Controlws_OnOpen;
            System.Threading.Tasks.Task.Run(() => controlWS.Connect());
        }

        private void Monitorws_OnOpen(object sender, EventArgs e)
        {
            debug("Success: Monitor WS Open");
            _connected = true;
        }



        public void debug(string msg)
        {
            Log.Information(msg);
        }

        private void Controlws_OnOpen(object sender, EventArgs e)
        {
            debug("Success: Control WS Open");
        }


        private void Controlws_OnMessage(object sender, MessageEventArgs e)
        {
        }

        private void Controlws_OnClose(object sender, CloseEventArgs e)
        {
            if (_closing)
                return;

            debug("Error: Control WS Closed - Check WS IP");
            Reconnect(controlWS);
        }

        private void Monitorws_OnClose(object sender, CloseEventArgs e)
        {
            if (_closing)
                return;

            debug("Error: Monitor WS Closed - Check WS IP");
            Reconnect(monitorWS);
        }

        // Reconnects in the background after a short pause: Connect() blocks until the attempt
        // succeeds or times out, which must not happen on the WebSocket's own event thread.
        private void Reconnect(WebSocket ws)
        {
            debug("Attempting to reconnect...");
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(ReconnectDelayMs);
                    if (!_closing)
                        ws.Connect();
                }
                catch (Exception ex)
                {
                    Log.Warning("Longmynd WS reconnect failed: " + ex.Message);
                }
            });
        }

        // Closes both sockets off the UI thread: Close() waits for a connect attempt that is still
        // in flight (up to ~20 s if the host is unreachable), which froze the window on exit.
        private void CloseWebsockets()
        {
            _closing = true;
            var monitor = monitorWS;
            var control = controlWS;
            Task.Run(() =>
            {
                try { monitor?.Close(); } catch (Exception ex) { Log.Warning("Longmynd monitor WS close failed: " + ex.Message); }
                try { control?.Close(); } catch (Exception ex) { Log.Warning("Longmynd control WS close failed: " + ex.Message); }
            });
        }

        private void Monitorws_OnMessage(object sender, MessageEventArgs e)
        {
            // Initialize connects the WebSocket first and builds the properties and sets VideoChangeCB afterwards. A message that is
            // faster than that (a Longmynd that answers at once, a slow start under the debugger) found no properties and crashed;
            // it is dropped, the next one follows within a second. The state is not touched, so the video starts with that one.
            if (_tuner1_properties == null || VideoChangeCB == null)
                return;

            monitorMessage mm = JsonConvert.DeserializeObject<monitorMessage>(e.Data);

            if (demodState != mm.packet.rx.demod_state)
            {
                if (mm.packet.rx.demod_state < 3)
                {
                    Log.Information("Stopping");
                    VideoChangeCB?.Invoke(1, false);
                    playing = false;
                }
                else
                {
                    Log.Information("Playing");
                    VideoChangeCB?.Invoke(1, true);
                    playing = true;
                }

                demodState = mm.packet.rx.demod_state;
            }


            current_frequency_0 = (uint)mm.packet.rx.frequency;
            current_sr_0 = (uint)mm.packet.rx.symbolrate;

            UpdatePropertiesWs(mm);

        }

        // TODO (#177): Cleanup with real HW test - test the info line (websocket + MQTT) and the TS error flags with Longmynd hardware,
        // then remove or simplify this guard (try/catch + failure counter) as far as the test shows it is not needed.
        // The info line is switched off after this many failures in a row, instead of logging the same error with every report
        private const int InfoLineMaxFailures = 5;
        private int _info_line_failures = 0;

        // The properties the MiniTiouner view has and the Longmynd view lacked (#186): TS status, C/N needed, RF input level,
        // frequency offset and carrier offset. They are filled from fields of the Longmynd WebSocket and from what was requested.
        private void UpdateViewPropertiesWs(monitorMessage monitor_message, double mer, double cn_needed, double db_margin)
        {
            var rx = monitor_message.packet.rx;
            bool locked = rx.demod_state >= 3;

            // TS status: Longmynd has no TSSTATUS register like the chip of the MiniTiouner. Not locked is "-", a lock with a NEW
            // uncorrected BCH frame (flag rises or BCH count rises) in the last 3 seconds is ERROR, a lock without is OK. (NO SYNC cannot be told apart here.)
            long now = Environment.TickCount64;
            if ((_last_bch_flag >= 0 && rx.errors_bch_uncorrected != 0 && _last_bch_flag == 0) ||
                (_last_bch_count >= 0 && rx.errors_bch_count > _last_bch_count))
                _last_bch_error_ticks = now;
            _last_bch_flag = rx.errors_bch_uncorrected != 0 ? 1 : 0;
            _last_bch_count = rx.errors_bch_count;

            if (!locked)   // no lock: "-", like the MiniTiouner. OpenTuner follows what Longmynd reports, it does not have to tune first
            {
                _tuner1_properties.UpdateValue("ts_status", "-");
                _tuner1_properties.UpdateColor("ts_status", Color.Transparent);
            }
            else if (rx.demod_state == 4 && _last_bch_error_ticks != 0 && now - _last_bch_error_ticks < 3000)
            {
                _tuner1_properties.UpdateValue("ts_status", "ERROR");
                _tuner1_properties.UpdateColor("ts_status", Color.Red);
            }
            else
            {
                _tuner1_properties.UpdateValue("ts_status", "OK");
                _tuner1_properties.UpdateColor("ts_status", Color.LimeGreen);
            }

            // a change of the TS status goes to the log with the values it was derived from (to check the rule against real signals)
            string ts_status_now = !locked ? "-" : (rx.demod_state == 4 && _last_bch_error_ticks != 0 && now - _last_bch_error_ticks < 3000 ? "ERROR" : "OK");
            if (ts_status_now != _last_ts_status_text)
            {
                Log.Information("Longmynd TS status " + _last_ts_status_text + " -> " + ts_status_now + " (demod state " + rx.demod_state + ", BCH uncorrected " + rx.errors_bch_uncorrected + ", BCH count " + rx.errors_bch_count + ", LDPC count " + rx.errors_ldpc_count + ", requested " + requested_frequency_0 + ")");
                _last_ts_status_text = ts_status_now;
            }

            // C/N needed for the received MODCOD with the margin to the measured MER in brackets, like the MiniTiouner view
            _tuner1_properties.UpdateValue("cn_needed", double.IsNaN(cn_needed) ? "-" : cn_needed.ToString("N1") + " dB [D " + db_margin.ToString("N1") + "]");

            // RF input level from AGC1, AGC2, the symbol rate and the LNA gain, the same calculation as the MiniTiouner
            if (rx.agc2 > 0)
            {
                double lna_db;
                stvvglna.stvvglna_gain_db((ushort)Math.Max(0, Math.Min(65535, rx.lna)), out lna_db);
                uint sr_ks = requested_sr_0 > 0 ? requested_sr_0 : (uint)(rx.symbolrate / 1000);
                double level = NimThread.get_rf_level((ushort)Math.Max(0, Math.Min(65535, rx.agc1)), (ushort)Math.Min(65535, rx.agc2), sr_ks, lna_db);
                _tuner1_properties.UpdateValue("rf_input_level", level.ToString("F2") + " dBm");
            }
            else
            {
                _tuner1_properties.UpdateValue("rf_input_level", "-");
            }

            _tuner1_properties.UpdateValue("offset", _settings.Offset1.ToString() + " kHz");

            // Longmynd only outputs a transport stream, so a lock means "Transport" (the MiniTiouner reads the format from the chip)
            _tuner1_properties.UpdateValue("stream_format", locked ? "Transport" : "");

            // carrier offset: where Longmynd found the carrier compared to the frequency OpenTuner asked for
            if (locked && requested_frequency_0 > 0)
            {
                long carrier_offset_khz = GetFrequency(0, true) - (long)requested_frequency_0;
                _tuner1_properties.UpdateValue("freq_carrier_offset", carrier_offset_khz.ToString("+0;-0;0") + " kHz");
            }
            else
            {
                _tuner1_properties.UpdateValue("freq_carrier_offset", "-");
            }
        }

        private void UpdatePropertiesWs(monitorMessage monitor_message)
        {
            double mer = Convert.ToDouble(monitor_message.packet.rx.mer) / 10;
            double db_margin = 0;
            double cn_needed = double.NaN;
            string modcod_text = "";

            _tuner1_properties.UpdateValue("requested_freq", "(" + GetFrequency(0, true).ToString("N0") + ") (" + GetFrequency(0, false).ToString("N0") + ") kHz");
            _tuner1_properties.UpdateValue("symbol_rate", (monitor_message.packet.rx.symbolrate / 1000).ToString() + " kS");
            _tuner1_properties.UpdateValue("demodstate", demod_state_lookup[monitor_message.packet.rx.demod_state]);
            _tuner1_properties.UpdateValue("ber", monitor_message.packet.rx.ber.ToString());

            if (monitor_message.packet.rx.demod_state < 3)
                _tuner1_properties.UpdateColor("demodstate", Color.PaleVioletRed);
            else
                _tuner1_properties.UpdateColor("demodstate", Color.PaleGreen);


            try
            {
                switch (monitor_message.packet.rx.demod_state)
                {
                    case 3:
                        modcod_text = modcod_lookup_dvbs[monitor_message.packet.rx.modcod];
                        cn_needed = modcod_lookup_dvbs_threshold[monitor_message.packet.rx.modcod];
                        db_margin = (mer - cn_needed);
                        break;
                    case 4:
                        modcod_text = modcod_lookup_dvbs2[monitor_message.packet.rx.modcod];
                        cn_needed = modcod_lookup_dvbs2_threshold[monitor_message.packet.rx.modcod];
                        db_margin = (mer - cn_needed);
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                debug("Unknown ModCod : " + ex.Message);
                debug(monitor_message.packet.rx.modcod.ToString());
            }

            last_mer_0 = mer.ToString();
            last_dbm_0 = "D" + db_margin.ToString("N1").ToString();
            _tuner1_properties.UpdateBigLabel("D" + db_margin.ToString("N1"));
            //_tuner1_properties.UpdateValue("db_margin", "D" + db_margin.ToString("N1"));
            _tuner1_properties.UpdateValue("modcod", modcod_text);
            _tuner1_properties.UpdateValue("mer", mer.ToString() + " dB");

            UpdateViewPropertiesWs(monitor_message, mer, cn_needed, db_margin);

            _tuner1_properties.UpdateValue("rf_input", (monitor_message.packet.rx.rfport == 0 ? "A (RF IN_1)" : "B (RF IN_2)"));

            _tuner1_properties.UpdateValue("service_name_provider", monitor_message.packet.ts.service_provider_name);
            _tuner1_properties.UpdateValue("service_name", monitor_message.packet.ts.service_name);

            last_service_name_0 = monitor_message.packet.ts.service_name;
            last_service_provider_0 = monitor_message.packet.ts.service_provider_name;

            _tuner1_properties.UpdateValue("null_packets", monitor_message.packet.ts.null_ratio + "%");

            _source_properties.UpdateValue("source_ts_ip", monitor_message.packet.rx.ts_ip_addr + ":" + monitor_message.packet.rx.ts_ip_port.ToString());

            if (_TsAddress + ":" + _settings.TS_Port != monitor_message.packet.rx.ts_ip_addr + ":" + monitor_message.packet.rx.ts_ip_port.ToString())
            {
                _source_properties.UpdateColor("source_ts_ip", Color.PaleVioletRed);
            }
            else
            {
                _source_properties.UpdateColor("source_ts_ip", Color.Bisque);
            }

            //_source_properties.UpdateValue("source_ip", _settings.LongmyndWSHost);

            // the info line above the video (#130)
            // caught on its own: a failure here must not skip the lost-lock handling below
            if (_info_line_failures < InfoLineMaxFailures)
            try
            {
                var source_data = new OTSourceData();
                source_data.video_number = 0;
                source_data.frequency = GetFrequency(0, true);
                source_data.symbol_rate = (int)(monitor_message.packet.rx.symbolrate / 1000);
                source_data.mer = mer;
                source_data.db_margin = db_margin;
                source_data.demod_locked = monitor_message.packet.rx.demod_state >= 3;
                source_data.demode_state = demod_state_lookup[monitor_message.packet.rx.demod_state];
                source_data.service_name = source_data.demod_locked ? (monitor_message.packet.ts.service_name ?? "") : "";
                source_data.modcode = modcod_text;
                source_data.video_codec = last_video_codec_0;
                if (_media_player != null)
                    source_data.volume = _media_player.GetVolume();
                OnSourceData?.Invoke(0, source_data, "Longmynd");
                _info_line_failures = 0;
            }
            catch (Exception ex)
            {
                _info_line_failures++;
                if (_info_line_failures >= InfoLineMaxFailures)
                    Log.Warning(ex, "Longmynd info line update failed " + InfoLineMaxFailures + " times in a row, info line switched off");
                else
                    Log.Warning(ex, "Longmynd info line update failed");
            }

            // lost lock
            if (monitor_message.packet.rx.demod_state < 3)
            {
                _tuner1_properties.UpdateValue("service_name_provider", "");
                _tuner1_properties.UpdateValue("service_name", "");
                _tuner1_properties.UpdateValue("stream_format", "");

                _tuner1_properties.UpdateValue("video_codec", "");
                _tuner1_properties.UpdateValue("video_resolution", "");
                _tuner1_properties.UpdateValue("audio_codec", "");
                _tuner1_properties.UpdateValue("audio_rate", "");

                // stop recording if we lost lock
                if (_recorder.record)
                {
                    _recorder.record = false;    // stop recording
//                    ClearIndicator(ref indicatorStatus, PropertyIndicators.RecordingIndicator);
                    _tuner1_properties.UpdateRecordButtonColor("media_controls_1", Color.Transparent);
                    _tuner1_properties.UpdateValue("media_controls", indicatorStatus.ToString());
                }

                // stop streaming if we lost lock
                if (_streamer.stream && !_streamer.steady)
                {
                    _streamer.stream = false;
//                    ClearIndicator(ref indicatorStatus, PropertyIndicators.StreamingIndicator);
                    _tuner1_properties.UpdateStreamButtonColor("media_controls_1", Color.Transparent);
                    _tuner1_properties.UpdateValue("media_controls", indicatorStatus.ToString());
                }

            }

        }

    }
}
