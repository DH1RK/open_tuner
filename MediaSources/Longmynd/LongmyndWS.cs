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
            uint if_khz = frequency - _settings.Offset1;
            int sent_khz = (int)if_khz + CorrectionKHz(if_khz);
            Log.Information("Longmynd tune: " + sent_khz + " kHz IF (correction " + CorrectionKHz(if_khz) + " kHz), " + symbol_rate + " kS, control socket " + controlWS.ReadyState);
            controlWS.Send("C" + sent_khz.ToString() + "," + symbol_rate.ToString());
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

            // Watchdog: now and then the Longmynd sends nothing to a new monitor connection, although it is open. After 5 s without a
            // message the connection is closed; the close handler then reconnects, which gives a new session.
            System.Threading.Interlocked.Exchange(ref _last_monitor_message_ticks, Environment.TickCount64);
            _monitor_watchdog?.Dispose();
            _monitor_watchdog = new System.Threading.Timer(MonitorWatchdogTick, null, 2000, 2000);
        }

        private void Monitorws_OnOpen(object sender, EventArgs e)
        {
            debug("Success: Monitor WS Open");
            System.Threading.Interlocked.Exchange(ref _last_monitor_message_ticks, Environment.TickCount64);
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
            _monitor_watchdog?.Dispose();
            var monitor = monitorWS;
            var control = controlWS;
            Task.Run(() =>
            {
                try { monitor?.Close(); } catch (Exception ex) { Log.Warning("Longmynd monitor WS close failed: " + ex.Message); }
                try { control?.Close(); } catch (Exception ex) { Log.Warning("Longmynd control WS close failed: " + ex.Message); }
            });
        }

        // WebSocketSharp swallows an exception of an event handler without a trace, so a failing message is logged here (the first one,
        // then every 100th, a broken field would otherwise repeat with every message)
        private int _monitor_message_failures = 0;

        private int _monitor_messages = 0;
        private long _last_monitor_message_ticks = 0;
        private System.Threading.Timer _monitor_watchdog;
        private const int MonitorSilenceMs = 5000;

        private void MonitorWatchdogTick(object state)
        {
            var ws = monitorWS;
            if (_closing || ws == null || ws.ReadyState != WebSocketState.Open)
                return;

            if (Environment.TickCount64 - System.Threading.Interlocked.Read(ref _last_monitor_message_ticks) < MonitorSilenceMs)
                return;

            Log.Warning("Longmynd: no monitor message for " + MonitorSilenceMs / 1000 + " s, reconnecting");
            System.Threading.Interlocked.Exchange(ref _last_monitor_message_ticks, Environment.TickCount64);
            Task.Run(() => RecreateMonitorSocket(ws));
        }

        // A new socket object instead of a Connect() on the closed one: reconnecting the same WebSocketSharp object can fail
        // ("Operation is not supported on this platform"). The handlers of the old one are taken off first, so that its close does not
        // start a second reconnect.
        private void RecreateMonitorSocket(WebSocket old)
        {
            try
            {
                if (_closing)
                    return;

                old.OnOpen -= Monitorws_OnOpen;
                old.OnMessage -= Monitorws_OnMessage;
                old.OnClose -= Monitorws_OnClose;

                string url = "ws://" + _settings.LongmyndWSHost + ":" + _settings.LongmyndWSPort.ToString() + "/ ";
                var fresh = new WebSocket(url, "monitor");
                fresh.OnOpen += Monitorws_OnOpen;
                fresh.OnMessage += Monitorws_OnMessage;
                fresh.OnClose += Monitorws_OnClose;
                monitorWS = fresh;

                try { old.Close(); } catch (Exception ex) { Log.Warning("Longmynd monitor WS close failed: " + ex.Message); }

                Task.Delay(ReconnectDelayMs).Wait();
                if (!_closing)
                    fresh.Connect();
            }
            catch (Exception ex)
            {
                Log.Warning("Longmynd monitor WS could not be recreated: " + ex.Message);
            }
        }

        private void Monitorws_OnMessage(object sender, MessageEventArgs e)
        {
            // the first messages and then every 60th go to the log, to see whether a Longmynd that stays silent sends nothing or OpenTuner drops it
            System.Threading.Interlocked.Exchange(ref _last_monitor_message_ticks, Environment.TickCount64);
            int message_number = System.Threading.Interlocked.Increment(ref _monitor_messages);
            if (message_number <= 3 || message_number % 60 == 0)
                Log.Information("Longmynd monitor message " + message_number + " received (" + e.Data.Length + " bytes, properties " + (_tuner1_properties != null) + ", video callback " + (VideoChangeCB != null) + ")");

            try
            {
                HandleMonitorMessage(e);
            }
            catch (Exception ex)
            {
                if (_monitor_message_failures++ % 100 == 0)
                    Log.Warning(ex, "Longmynd: a monitor message could not be processed (" + _monitor_message_failures + ")");
            }
        }

        private void HandleMonitorMessage(MessageEventArgs e)
        {
            // Initialize connects the WebSocket first and builds the properties and sets VideoChangeCB afterwards. A message that is
            // faster than that (a Longmynd that answers at once, a slow start under the debugger) found no properties and crashed;
            // it is dropped, the next one follows within a second. The state is not touched, so the video starts with that one.
            // The media player is configured after the properties: a "Playing" before that is lost (MainForm has no player yet) and the
            // state is already remembered, so the video would never start. The message is dropped, the next one starts it.
            if (_tuner1_properties == null || VideoChangeCB == null || _media_player == null)
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


            // The Longmynd reports what was sent (with the ppm correction and the manual offset of the Special tab) plus the carrier offset.
            // OpenTuner works with the nominal frequency (the info line, the spectrum), so the correction is taken off again.
            uint reported_if_khz = (uint)Math.Max(0, mm.packet.rx.frequency);
            current_frequency_0 = (uint)Math.Max(0, (long)reported_if_khz - CorrectionKHz(reported_if_khz));
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

            // TS status: Longmynd has no TSSTATUS register like the chip of the MiniTiouner, so it is derived from what the demodulator reports.
            // Not locked is "-". An uncorrectable BCH frame in the last 3 s, or (once the lock has settled, 3 s) a TEI in the TS of the last
            // 10 s (the demodulator flagged a packet) is ERROR, otherwise OK. Continuity and sync errors of the TS parser can come from the
            // network, they stay in the info line flags and do not change the TS status (the MiniTiouner takes its status from the chip, too).
            long now = Environment.TickCount64;
            // Longmynd reports "uncorrected" as TRUE while the chip's ERRORFLAG is 0 (stv0910.c), so the normal state of a clean lock is 1
            // and a frame the BCH decoder could not correct shows as 0. A 0 while locked in DVB-S2 is the error.
            if (rx.demod_state == 4 && rx.errors_bch_uncorrected == 0)
                _last_bch_error_ticks = now;

            if (!locked)
                _locked_since_ticks = 0;
            else if (_locked_since_ticks == 0)
                _locked_since_ticks = now;

            TSParserThread ts_parser = _testModeParser ?? _tsParser;
            TSHealthCounts ts_health = ts_parser != null ? ts_parser.Health.Get() : new TSHealthCounts();
            bool lock_settled = locked && _locked_since_ticks != 0 && now - _locked_since_ticks >= 3000;
            bool bch_error = rx.demod_state == 4 && _last_bch_error_ticks != 0 && now - _last_bch_error_ticks < 3000;

            string ts_status_now;
            Color ts_status_color;
            if (!locked)   // no lock: "-", like the MiniTiouner. OpenTuner follows what Longmynd reports, it does not have to tune first
            {
                ts_status_now = "-";
                ts_status_color = Color.Transparent;
            }
            else if (bch_error || (lock_settled && ts_health.Transport > 0))
            {
                ts_status_now = "ERROR";
                ts_status_color = Color.Red;
            }
            else
            {
                ts_status_now = "OK";
                ts_status_color = Color.LimeGreen;
            }

            _tuner1_properties.UpdateValue("ts_status", ts_status_now);
            _tuner1_properties.UpdateColor("ts_status", ts_status_color);

            // a change of the TS status goes to the log with the values it was derived from (to check the rule against real signals)
            if (ts_status_now != _last_ts_status_text)
            {
                Log.Information("Longmynd TS status " + _last_ts_status_text + " -> " + ts_status_now + " (demod state " + rx.demod_state + ", BCH uncorrected " + rx.errors_bch_uncorrected +
                                ", BCH count " + rx.errors_bch_count + ", LDPC count " + rx.errors_ldpc_count + ", TS " + TSHealth.Describe(ts_health) + ", requested " + requested_frequency_0 + ")");
                _last_ts_status_text = ts_status_now;
            }

            // C/N needed for the received MODCOD with the margin to the measured MER in brackets, like the MiniTiouner view
            _tuner1_properties.UpdateValue("cn_needed", double.IsNaN(cn_needed) ? "-" : cn_needed.ToString("N1") + " dB [D " + db_margin.ToString("N1") + "]");

            // RF input level from AGC1, AGC2, the symbol rate and the LNA gain, the same calculation as the MiniTiouner
            double rf_level_dbm = double.NaN;
            double lna_db_value = 0;
            if (rx.agc2 > 0)
            {
                double lna_db;
                stvvglna.stvvglna_gain_db((ushort)Math.Max(0, Math.Min(65535, rx.lna)), out lna_db);
                uint sr_ks = requested_sr_0 > 0 ? requested_sr_0 : (uint)(rx.symbolrate / 1000);
                double level = NimThread.get_rf_level((ushort)Math.Max(0, Math.Min(65535, rx.agc1)), (ushort)Math.Min(65535, rx.agc2), sr_ks, lna_db);
                rf_level_dbm = level;
                lna_db_value = lna_db;
                _tuner1_properties.UpdateValue("rf_input_level", level.ToString("F2") + " dBm");
            }
            else
            {
                _tuner1_properties.UpdateValue("rf_input_level", "-");
            }

            _tuner1_properties.UpdateValue("offset", _settings.Offset1.ToString() + " kHz");

            // Expert tab: constellation, gauges and error counts (the register lamps are not available with Longmynd)
            if (_expert_1 != null)
            {
                // lock time: from the last tuning by OpenTuner to the lock, "-" if it was not tuned by OpenTuner
                if (!locked)
                    _lock_time_ms = -1;
                else if (_lock_time_ms < 0 && _tune_ticks != 0)
                    _lock_time_ms = Environment.TickCount64 - _tune_ticks;

                byte[,] constellation = null;
                if (locked && rx.constellation != null && rx.constellation.Length > 0)
                {
                    constellation = new byte[rx.constellation.Length, 2];
                    for (int n = 0; n < rx.constellation.Length; n++)
                    {
                        if (rx.constellation[n] == null || rx.constellation[n].Length < 2)
                            continue;
                        constellation[n, 0] = unchecked((byte)rx.constellation[n][0]);
                        constellation[n, 1] = unchecked((byte)rx.constellation[n][1]);
                    }
                }

                string lna_text = rx.lna > 0 && stvvglna.stvvglna_gain_db((ushort)Math.Max(0, Math.Min(65535, rx.lna)), out double lna_known_db)
                    ? lna_known_db.ToString("N1") + " dB" : rx.lna + " (?)";

                _expert_1.UpdateBasic(rx.demod_state == 4, rx.demod_state == 3, rf_level_dbm, !double.IsNaN(rf_level_dbm), mer, cn_needed, constellation,
                                      _lock_time_ms, (uint)Math.Max(0, rx.vber), (uint)Math.Max(0, rx.errors_ldpc_count),
                                      "BCH:  counter " + rx.errors_bch_count + ",  ERRORFLAG " + (rx.errors_bch_uncorrected == 0 ? 1 : 0),
                                      (ushort)Math.Max(0, Math.Min(65535, rx.agc2)), lna_text);
            }

            // TS Info tab: the service from the status and the bit rate the received MODCOD can carry (the chip's own rate is not known)
            _tsinfo_1?.SetService(monitor_message.packet.ts.service_name, monitor_message.packet.ts.service_provider_name);
            byte chip_demod_status = rx.demod_state == 4 ? stv0910.DEMOD_S2 : rx.demod_state == 3 ? stv0910.DEMOD_S : stv0910.DEMOD_HUNTING;
            _tsinfo_1?.SetSignal(opentuner.MediaSources.Minitiouner.MinitiounerSource.ExpectedBitrateKbps(chip_demod_status, (uint)Math.Max(0, rx.modcod), (uint)(rx.symbolrate / 1000), rx.short_frame, rx.pilot_symbols), double.NaN);

            // Special tab: carrier offset, frequency found, measured symbol rate and the derotator, from the frequency Longmynd reports
            // (what was sent plus the carrier offset, in whole kHz) and the symbol rate OpenTuner asked for
            if (_frequency_1 != null)
            {
                uint reported_if_khz = (uint)Math.Max(0, rx.frequency);
                uint nominal_rf_khz = requested_frequency_0 > 0 ? requested_frequency_0 : reported_if_khz + _settings.Offset1;
                uint nominal_if_khz = nominal_rf_khz > _settings.Offset1 ? nominal_rf_khz - _settings.Offset1 : 0;
                int sent_if_khz = (int)nominal_if_khz + CorrectionKHz(nominal_if_khz);
                // without a tuning by OpenTuner the sent frequency is unknown, so there is no carrier offset
                int carrier_offset_hz = requested_frequency_0 > 0 ? (int)(((long)reported_if_khz - sent_if_khz) * 1000) : 0;
                uint measured_sr_hz = (uint)Math.Max(0, rx.symbolrate);
                // the search range of the Longmynd is 1.5 x the symbol rate (its option -S, default 1.5), it cannot be changed while it runs
                int half_range_hz = (int)(Math.Max(measured_sr_hz, requested_sr_0 * 1000u) * 1.5);

                _frequency_1.SetRequestedRate(requested_sr_0 > 0 ? requested_sr_0 : measured_sr_hz / 1000);
                _frequency_1.Update(chip_demod_status, carrier_offset_hz, -half_range_hz, half_range_hz, measured_sr_hz, nominal_rf_khz, nominal_if_khz,
                                    (ushort)Math.Max(0, Math.Min(65535, rx.agc2)));
            }

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
            _tuner1_properties.UpdateValue("symbol_rate", (monitor_message.packet.rx.symbolrate / 1000).ToString() + " KS");
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
                // the flags of the TS analysis (CC, TEI, SYNC, BUF, ENC), like with the MiniTiouner
                opentuner.MediaSources.Minitiouner.MinitiounerSource.FillTsHealth(source_data, _testModeParser ?? _tsParser, ts_thread);
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
                if (_recorder != null && _recorder.record)
                {
                    _recorder.record = false;    // stop recording
//                    ClearIndicator(ref indicatorStatus, PropertyIndicators.RecordingIndicator);
                    _tuner1_properties.UpdateRecordButtonColor("media_controls_1", Color.Transparent);
                    _tuner1_properties.UpdateValue("media_controls", indicatorStatus.ToString());
                }

                // stop streaming if we lost lock
                if (_streamer != null && _streamer.stream && !_streamer.steady)
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
