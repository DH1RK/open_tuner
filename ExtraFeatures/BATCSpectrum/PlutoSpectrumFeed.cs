using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace opentuner
{
    // The spectrum of a Tezuka Pluto (PlutoSky) read directly and converted into the BATC format (#215). It does what the
    // stand-alone PlutoBridge does, without the server: the spectrum comes as a binary WebSocket (ws://<host>/waterfall: a frame
    // counter plus the bins), the centre frequency and the span over MQTT (ws://<host>:9001/mqtt).
    //
    // Measured at the PlutoSky (docs, 3.5 to 3.7): the passband is flat (+-2 to 3 dB) about -4.4 to +4.0 MHz around the centre, the
    // frame values behave like power (10*log10), the level of a frame depends on the span, so every frame is normalised to the
    // noise floor of OpenTuner (11000 units, 3459 units per dB). The LNB LO of 9360 MHz (IF 1135 MHz = 10495 MHz) is an assumption.
    class PlutoSpectrumFeed : SpectrumFeed
    {
        const int Bins = 922;               // BATC raster
        const double StartMHz = 10490.5;
        const double WidthMHz = 9.0;
        const double UsableLoMHz = -4.4;    // around the centre, measured passband
        const double UsableHiMHz = 4.0;
        const double FloorUnits = 11000;
        const double UnitsPerDb = 65536.0 * 0.052778;   // OpenTuner CalcDbb
        const double DbFactor = 10;
        const double FloorPercentile = 30;
        const double DespikeDb = 1.5;       // the spur of the receiver in the centre
        const int DespikeHalf = 3;
        static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(250);

        private readonly string host;
        private readonly double set_center_mhz;      // centre (IF) = view (RF) - LNB LO, 0 = leave it
        private readonly double lo_mhz;
        private readonly double set_span_mhz;
        private readonly double set_frame_ms;
        private readonly double average_seconds;
        private readonly bool flat_floor;
        private readonly bool follow_rate;
        private readonly double offset_low_mhz, offset_high_mhz;

        private readonly object run_lock = new object();
        private CancellationTokenSource cts;

        private double center_hz = double.NaN, span_hz = double.NaN, sampling_hz = double.NaN;
        private double floor_ema = double.NaN;
        private double[] avg_power;
        private double avg_center_hz, avg_span_hz;
        private DateTime last_frame_utc = DateTime.MinValue;
        private DateTime last_delivery = DateTime.MinValue;
        private bool center_set;
        private bool span_set;
        private bool frame_set;
        private bool warned_no_bins;

        public PlutoSpectrumFeed(string host, double view_mhz, double lo_mhz, double set_span_mhz, double set_frame_ms, double average_seconds, bool flat_floor, bool follow_rate, double offset_low_mhz, double offset_high_mhz)
        {
            this.host = host;
            this.lo_mhz = lo_mhz;
            this.set_center_mhz = view_mhz > 0 ? view_mhz - lo_mhz : 0;
            this.set_span_mhz = set_span_mhz;
            this.set_frame_ms = set_frame_ms;
            this.average_seconds = average_seconds;
            this.flat_floor = flat_floor;
            this.follow_rate = follow_rate;
            this.offset_low_mhz = offset_low_mhz;
            this.offset_high_mhz = offset_high_mhz;
        }

        public override string Description => "Pluto " + host;

        public override void start()
        {
            lock (run_lock)
            {
                if (cts != null)
                    return;     // running, the loops reconnect themselves

                cts = new CancellationTokenSource();
                var token = cts.Token;
                // The writes of the start (span, VFW, centre) make the spectrum pause for a moment; the watchdog of the spectrum (no data for
                // 2 s: stop) must not end the feed, and with it the writes, in the middle of them.
                if (config_until == DateTime.MinValue)
                    config_until = DateTime.UtcNow.AddSeconds(25);
                Log.Information("Pluto spectrum: start, " + host);
                Task.Run(() => MqttLoop(token));
                Task.Run(() => WaterfallLoop(token));
            }
        }

        private DateTime config_until = DateTime.MinValue;

        public override void stop()
        {
            if (connected && DateTime.UtcNow >= config_until)
                Cancel();
        }

        public override void Shutdown()
        {
            Cancel();
        }

        private void Cancel()
        {
            bool was_connected;
            lock (run_lock)
            {
                cts?.Cancel();
                cts = null;
                was_connected = connected;
                connected = false;
            }
            span_hz = double.NaN;       // the next start takes the values the Pluto publishes
            center_hz = double.NaN;
            avg_power = null;
            env_ema = null;
            if (was_connected)
                RaiseConnectionStatusChanged(false);
        }

        private double Span()
        {
            return !double.IsNaN(span_hz) ? span_hz : sampling_hz;
        }

        // ---------------------------------------------------------------- MQTT (centre frequency and span)

        private async Task MqttLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using (var ws = new ClientWebSocket())
                    {
                        ws.Options.AddSubProtocol("mqtt");
                        await ws.ConnectAsync(new Uri("ws://" + host + ":9001/mqtt"), ct);
                        var id = Encoding.ASCII.GetBytes("opentuner" + Random.Shared.Next(1000, 9999));
                        var vh = new List<byte> { 0, 4, (byte)'M', (byte)'Q', (byte)'T', (byte)'T', 4, 2, 0, 60, (byte)(id.Length >> 8), (byte)(id.Length & 255) };
                        vh.AddRange(id);
                        await SendPacket(ws, 0x10, vh, ct);

                        int pid = 1;
                        foreach (var topic in new[] { "state/rx/frequency", "state/rx/span", "state/rx/sampling", "state/spectro/fps", "dt/pluto/+/tx/dvbs2/sr" })
                        {
                            var tb = Encoding.ASCII.GetBytes(topic);
                            var pl = new List<byte> { (byte)(pid >> 8), (byte)(pid & 255), (byte)(tb.Length >> 8), (byte)(tb.Length & 255) };
                            pl.AddRange(tb);
                            pl.Add(0);
                            pid++;
                            await SendPacket(ws, 0x82, pl, ct);
                        }
                        Log.Information("Pluto spectrum: MQTT connected");

                        // the writes to the device (span, VFW, centre) are made by a task that waits for the answers of the device
                        _ = Task.Run(() => ConfigureAsync(ws, ct), ct);

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                                {
                                    await Task.Delay(30000, ct);
                                    await SendPacket(ws, 0xC0, new List<byte>(), ct);
                                }
                            }
                            catch (Exception) { }
                        }, ct);

                        var acc = new List<byte>();
                        var buf = new byte[8192];
                        while (ws.State == WebSocketState.Open)
                        {
                            var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                            if (r.MessageType == WebSocketMessageType.Close)
                                break;
                            acc.AddRange(new ArraySegment<byte>(buf, 0, r.Count));
                            ParsePackets(acc);
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) { Log.Debug("Pluto spectrum: MQTT: " + e.Message); }

                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
            }
        }

        // ---- writes to the device ---------------------------------------------------------------------------------------
        // The order and the waiting matter: the span changes the sampling rate of the receiver (the firmware publishes state/rx/span when
        // it is done), and the display of the dashboard (#spectrum) is wrong after a span change until the centre frequency is written
        // again. So: span, wait for the answer, VFW, and the centre last, once, with the symbol rate of the #datv page already read.
        // Later: a change of the symbol rate writes the centre that belongs to it, and any other change of the span writes the centre
        // that the device has again (what is entered by hand in the dashboard).
        private async Task ConfigureAsync(ClientWebSocket ws, CancellationToken ct)
        {
            try
            {
                // the retained state (span, centre, symbol rate) arrives right after the subscribe
                await Task.Delay(800, ct);

                if (set_span_mhz > 0 && !span_set)
                {
                    span_set = true;
                    double want = set_span_mhz * 1e6;
                    if (double.IsNaN(span_hz) || Math.Abs(span_hz - want) > 1)
                    {
                        await PublishHz(ws, "cmd/rx/span", set_span_mhz, ct);
                        Log.Information("Pluto spectrum: span set to " + set_span_mhz.ToString("0.###", CultureInfo.InvariantCulture) + " MHz");
                        await WaitFor(() => Math.Abs(span_hz - want) <= 1, 4000, ct);
                        await Task.Delay(700, ct);      // let the receiver settle
                    }
                }

                // the frame interval of the spectrometer (VFW of the dashboard): the spectrometer averages over it, so a longer
                // interval calms the noise before the frames get here (cmd/spectro/fps = 1000 / ms)
                if (set_frame_ms > 0 && !frame_set)
                {
                    frame_set = true;
                    int fps = (int)Math.Max(1, Math.Round(1000.0 / set_frame_ms, MidpointRounding.AwayFromZero));     // like the dashboard (Math.round): 400 ms -> 3 fps
                    spectro_fps_state = null;
                    await PublishText(ws, "cmd/spectro/fps", fps.ToString(CultureInfo.InvariantCulture), ct);
                    Log.Information("Pluto spectrum: frame interval set to " + set_frame_ms.ToString("0.#", CultureInfo.InvariantCulture) + " ms (" + fps + " fps)");

                    // The spectrometer answers a rate it does not take with HTTP 400 (state/spectro/fps = error:400) and leaves the rate as it
                    // was. Measured at a span of 16 MHz: 2 and 3 fps refused, 4 taken. Then the next shorter interval that it takes is used.
                    await WaitFor(() => spectro_fps_state != null, 1500, ct);
                    if (spectro_fps_state != null && spectro_fps_state.StartsWith("error", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Warning("Pluto spectrum: the spectrometer refused " + fps + " fps (" + spectro_fps_state + ")");
                        foreach (int try_fps in new[] { 3, 4, 5, 8, 10 })
                        {
                            if (try_fps <= fps)
                                continue;
                            spectro_fps_state = null;
                            await PublishText(ws, "cmd/spectro/fps", try_fps.ToString(CultureInfo.InvariantCulture), ct);
                            await WaitFor(() => spectro_fps_state != null, 1500, ct);
                            if (spectro_fps_state != null && !spectro_fps_state.StartsWith("error", StringComparison.OrdinalIgnoreCase))
                            {
                                Log.Information("Pluto spectrum: frame interval is " + (1000.0 / try_fps).ToString("0.#", CultureInfo.InvariantCulture) + " ms (" + try_fps + " fps)");
                                break;
                            }
                        }
                    }
                    await Task.Delay(300, ct);
                }

                double want_centre = DesiredCenterMhz();
                if (want_centre > 0 && !center_set)
                {
                    center_set = true;
                    await WriteCenter(ws, want_centre, "start", ct);
                }
                sr_changed = false;     // the symbol rate that was read is in the centre that was written

                double seen_span = span_hz;
                DateTime span_changed = DateTime.MinValue;
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    await Task.Delay(250, ct);

                    if (!double.IsNaN(span_hz) && span_hz != seen_span)
                    {
                        seen_span = span_hz;
                        span_changed = DateTime.UtcNow;
                    }

                    if (sr_changed && (DateTime.UtcNow - sr_changed_at).TotalMilliseconds > 700)
                    {
                        // another symbol rate: its span has been set by the device, the centre that belongs to it now
                        sr_changed = false;
                        span_changed = DateTime.MinValue;
                        double centre = DesiredCenterMhz();
                        if (centre > 0)
                            await WriteCenter(ws, centre, "symbol rate", ct);
                    }
                    else if (span_changed != DateTime.MinValue && (DateTime.UtcNow - span_changed).TotalMilliseconds > 700)
                    {
                        // the span was changed: the centre that belongs to the symbol rate and the new span if that is followed, else the centre
                        // the device has once more, so that its display is right again
                        span_changed = DateTime.MinValue;
                        double followed = follow_rate && symbol_rate > 0 ? DesiredCenterMhz() : 0;
                        if (followed > 0)
                        {
                            await WriteCenter(ws, followed, "span", ct);
                            continue;
                        }
                        double centre_hz_now = center_hz;
                        if (!double.IsNaN(centre_hz_now))
                        {
                            await PublishHz(ws, "cmd/rx/frequency", centre_hz_now / 1e6, ct);
                            Log.Information("Pluto spectrum: centre frequency written again after the span changed (" + (centre_hz_now / 1e6).ToString("0.###", CultureInfo.InvariantCulture) + " MHz)");
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Debug("Pluto spectrum: configuration: " + e.Message); }
        }

        // the centre frequency (IF, MHz) that is wanted now: the one of the settings, shifted by the symbol rate if that is followed
        private double DesiredCenterMhz()
        {
            if (set_center_mhz <= 0)
                return 0;
            double offset = 0;
            if (follow_rate && symbol_rate > 0)
            {
                // only a narrow view (below 7.5 MHz) is shifted; from 8 MHz on (250 KS, 2000 KS, ...) the usable range of the receiver is covered
                double span_mhz = !double.IsNaN(span_hz) ? span_hz / 1e6 : ExpectedSpanMhz(symbol_rate);
                if (span_mhz < 7.5)
                    offset = symbol_rate <= 125000.5 ? offset_low_mhz : offset_high_mhz;
            }
            return set_center_mhz + offset;
        }

        // the span that the device gives for a symbol rate (measured, docs 3.2): 32 x SR below about 520 KS, 4 x SR above, 6 MHz for 20 to 66 KS
        private static double ExpectedSpanMhz(double symbol_rate)
        {
            if (symbol_rate <= 66000.5)
                return 6;
            return (symbol_rate < 520800 ? 32 : 4) * symbol_rate / 1e6;
        }

        // writes the centre and checks that the device took it (state/rx/frequency), once more if not
        private async Task WriteCenter(ClientWebSocket ws, double mhz, string why, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await PublishHz(ws, "cmd/rx/frequency", mhz, ct);
                Log.Information("Pluto spectrum: centre frequency set to " + mhz.ToString("0.###", CultureInfo.InvariantCulture) + " MHz (" + why + ")");
                if (await WaitFor(() => Math.Abs(center_hz - mhz * 1e6) <= 1, 2000, ct))
                    return;
            }
        }

        private static async Task<bool> WaitFor(Func<bool> condition, int timeout_ms, CancellationToken ct)
        {
            var end = DateTime.UtcNow.AddMilliseconds(timeout_ms);
            while (DateTime.UtcNow < end)
            {
                if (condition())
                    return true;
                await Task.Delay(100, ct);
            }
            return condition();
        }

        // MQTT publish (QoS 0) of a value in MHz as Hz
        private Task PublishHz(ClientWebSocket ws, string topic, double mhz, CancellationToken ct)
        {
            return PublishText(ws, topic, ((long)Math.Round(mhz * 1e6)).ToString(CultureInfo.InvariantCulture), ct);
        }

        private async Task PublishText(ClientWebSocket ws, string topic, string text, CancellationToken ct)
        {
            var tb = Encoding.ASCII.GetBytes(topic);
            var pb = Encoding.ASCII.GetBytes(text);
            var pk = new List<byte> { (byte)(tb.Length >> 8), (byte)(tb.Length & 255) };
            pk.AddRange(tb);
            pk.AddRange(pb);
            await SendPacket(ws, 0x30, pk, ct);
        }

        private readonly SemaphoreSlim send_gate = new SemaphoreSlim(1, 1);

        // one packet at a time: the keepalive and the configuration both send
        private async Task SendPacket(ClientWebSocket ws, byte header, List<byte> body, CancellationToken ct)
        {
            var p = new List<byte> { header };
            int len = body.Count;
            do
            {
                int d = len % 128;
                len /= 128;
                if (len > 0) d |= 128;
                p.Add((byte)d);
            } while (len > 0);
            p.AddRange(body);
            await send_gate.WaitAsync(ct);
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(p.ToArray()), WebSocketMessageType.Binary, true, ct);
            }
            finally
            {
                send_gate.Release();
            }
        }

        private void ParsePackets(List<byte> acc)
        {
            while (acc.Count >= 2)
            {
                int mult = 1, rem = 0, i = 1;
                bool complete = false;
                while (i < acc.Count && i <= 4)
                {
                    int d = acc[i++];
                    rem += (d & 127) * mult;
                    mult *= 128;
                    if ((d & 128) == 0) { complete = true; break; }
                }
                if (!complete || acc.Count < i + rem)
                    return;

                int type = acc[0] >> 4;
                if (type == 3)   // PUBLISH
                {
                    int pos = i;
                    int tl = (acc[pos] << 8) | acc[pos + 1];
                    string topic = Encoding.ASCII.GetString(acc.GetRange(pos + 2, tl).ToArray());
                    pos += 2 + tl;
                    if (((acc[0] >> 1) & 3) > 0)
                        pos += 2;
                    string payload = Encoding.ASCII.GetString(acc.GetRange(pos, i + rem - pos).ToArray());
                    OnMqtt(topic, payload);
                }
                acc.RemoveRange(0, i + rem);
            }
        }

        // The view follows the symbol rate: the span is only 4 to 6 MHz for up to 125 KS and for 1000 KS and more, so the centre is shifted
        // towards where those signals are. Noted here, the configuration task writes the centre (DesiredCenterMhz).
        private double symbol_rate;
        private volatile bool sr_changed;
        private DateTime sr_changed_at = DateTime.MinValue;

        private void OnSymbolRate(double rate)
        {
            if (rate == symbol_rate)
                return;
            symbol_rate = rate;
            sr_changed_at = DateTime.UtcNow;
            sr_changed = true;
        }

        private volatile string spectro_fps_state;      // state/spectro/fps: the rate, or "error:<http code>" if the device refused it

        private void OnMqtt(string topic, string payload)
        {
            if (topic == "state/spectro/fps")
            {
                spectro_fps_state = payload.Trim();
                return;
            }
            if (!double.TryParse(payload.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return;
            if (topic.StartsWith("dt/pluto/", StringComparison.Ordinal) && topic.EndsWith("/tx/dvbs2/sr", StringComparison.Ordinal))
            {
                OnSymbolRate(v);
                return;
            }
            switch (topic)
            {
                case "state/rx/frequency": center_hz = v; break;
                case "state/rx/span": span_hz = v; break;
                case "state/rx/sampling": sampling_hz = v; break;
            }
        }

        // ---------------------------------------------------------------- spectrum frames from the Pluto

        private async Task WaterfallLoop(CancellationToken ct)
        {
            var buf = new byte[1 << 16];
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using (var ws = new ClientWebSocket())
                    {
                        await ws.ConnectAsync(new Uri("ws://" + host + "/waterfall"), ct);
                        Log.Information("Pluto spectrum: waterfall connected");
                        bool closed = false;
                        while (ws.State == WebSocketState.Open && !closed)
                        {
                            int len = 0;
                            WebSocketReceiveResult r;
                            do
                            {
                                r = await ws.ReceiveAsync(new ArraySegment<byte>(buf, len, buf.Length - len), ct);
                                if (r.MessageType == WebSocketMessageType.Close) { closed = true; break; }
                                len += r.Count;
                            } while (!r.EndOfMessage && len < buf.Length);

                            if (closed || r.MessageType != WebSocketMessageType.Binary || len < 16)
                                continue;
                            var floats = new float[len / 4];
                            Buffer.BlockCopy(buf, 0, floats, 0, floats.Length * 4);
                            OnFrame(floats);
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) { Log.Debug("Pluto spectrum: waterfall: " + e.Message); }

                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
            }
        }

        private void OnFrame(float[] frame)
        {
            double span = Span();
            double center = center_hz;
            if (double.IsNaN(span) || double.IsNaN(center) || span <= 0)
                return;

            int n = frame.Length - 1;     // frame[0] is the frame counter
            var db = new double[n];
            for (int i = 0; i < n; i++)
            {
                float v = frame[i + 1];
                db[i] = v > 0 ? DbFactor * Math.Log10(v) : -300;
            }

            Despike(db);

            // average over time (linear power): unaveraged the noise ripples by several dB and OpenTuner takes every 1.5 dB above
            // the floor for a signal
            if (average_seconds > 0)
            {
                var now = DateTime.UtcNow;
                double dt = (now - last_frame_utc).TotalSeconds;
                last_frame_utc = now;
                if (avg_power == null || avg_power.Length != n || avg_center_hz != center || avg_span_hz != span || dt > 5)
                {
                    avg_power = new double[n];
                    for (int i = 0; i < n; i++)
                        avg_power[i] = Math.Pow(10, db[i] / 10);
                    avg_center_hz = center;
                    avg_span_hz = span;
                }
                else
                {
                    double alpha = 1 - Math.Exp(-Math.Max(dt, 0.001) / average_seconds);
                    for (int i = 0; i < n; i++)
                        avg_power[i] += alpha * (Math.Pow(10, db[i] / 10) - avg_power[i]);
                }
                for (int i = 0; i < n; i++)
                    db[i] = 10 * Math.Log10(avg_power[i]);
            }

            double span_mhz = span / 1e6, center_mhz = center / 1e6;
            double bin_w = WidthMHz / Bins;
            var out_db = new double[Bins];
            var valid = new bool[Bins];
            int valid_count = 0;

            for (int i = 0; i < Bins; i++)
            {
                // same bin to frequency rule as OpenTuner (signal.cs): start + (bin + 1) / 922 * 9
                double rf = StartMHz + (i + 1) * bin_w;
                double off = rf - lo_mhz - center_mhz;
                if (off < UsableLoMHz || off > UsableHiMHz || Math.Abs(off) > span_mhz / 2)
                    continue;

                double x0 = ((off - bin_w / 2) / span_mhz + 0.5) * n;
                double x1 = ((off + bin_w / 2) / span_mhz + 0.5) * n;
                int a = Math.Max((int)Math.Floor(x0), 0);
                int b = Math.Min(Math.Max((int)Math.Ceiling(x1) - 1, a), n - 1);
                if (a > b)
                    continue;

                double p = 0;
                for (int k = a; k <= b; k++)
                    p += Math.Pow(10, db[k] / 10);
                out_db[i] = 10 * Math.Log10(p / (b - a + 1));
                valid[i] = true;
                valid_count++;
            }

            if (valid_count < 20)
            {
                if (!warned_no_bins)
                {
                    warned_no_bins = true;
                    Log.Warning("Pluto spectrum: no valid bins (span " + span_mhz + " MHz, centre " + center_mhz + " MHz do not match the band)");
                }
                return;
            }
            warned_no_bins = false;

            // noise floor: percentile over the valid bins, smoothed over time so the levels do not flicker
            var vals = new List<double>(valid_count);
            for (int i = 0; i < Bins; i++)
                if (valid[i]) vals.Add(out_db[i]);
            vals.Sort();
            double floor_now = vals[(int)Math.Clamp(vals.Count * FloorPercentile / 100.0, 0, vals.Count - 1)];
            floor_ema = double.IsNaN(floor_ema) || Math.Abs(floor_now - floor_ema) > 6 ? floor_now : floor_ema + 0.1 * (floor_now - floor_ema);

            var now_local = DateTime.Now;
            if (now_local - last_delivery < FrameInterval)
                return;
            last_delivery = now_local;

            // flat noise floor: every bin is measured against the noise around it, so the tilt and the hump of the passband do not
            // push the noise over the detection threshold of OpenTuner
            double[] local_floor = flat_floor ? UpdateLocalFloor(out_db, valid, center, span) : null;

            var data = new ushort[Bins];
            for (int i = 0; i < Bins; i++)
            {
                double units = valid[i] ? FloorUnits + (out_db[i] - (local_floor != null ? local_floor[i] : floor_ema)) * UnitsPerDb : FloorUnits;
                data[i] = (ushort)Math.Clamp(Math.Round(units), 0, 65535);
            }

            lastdata = now_local;
            if (!connected)
            {
                connected = true;
                RaiseConnectionStatusChanged(true);
            }
            callback?.Invoke(data);
        }

        // ---- flat noise floor -------------------------------------------------------------------------------------
        // The noise floor of every bin is a low percentile of the bins around it (+-2 MHz), which is the noise as long as the
        // signals take less than two thirds of that window (the beacon is 2 MHz wide). It is calculated every FloorStep bins,
        // interpolated in between and followed over the frames.
        private const int FloorHalfBins = 205;           // 2 MHz at 9.76 kHz per bin
        private const int FloorStep = 4;
        private const double FloorLocalPercentile = 30;
        private const double FloorFollow = 0.2;          // per frame that is delivered

        private double[] env_ema;
        private double env_center_hz, env_span_hz;

        private double[] UpdateLocalFloor(double[] out_db, bool[] valid, double center, double span)
        {
            int lo = Array.IndexOf(valid, true);
            int hi = Array.LastIndexOf(valid, true);
            var env = new double[Bins];
            if (lo < 0)
                return env;

            var window = new List<double>(2 * FloorHalfBins + 1);
            int prev_k = -1;
            double prev_v = 0;
            for (int k = lo; ; k += FloorStep)
            {
                if (k > hi)
                    k = hi;

                window.Clear();
                for (int j = Math.Max(lo, k - FloorHalfBins); j <= Math.Min(hi, k + FloorHalfBins); j++)
                    window.Add(out_db[j]);
                window.Sort();
                double v = window[(int)Math.Clamp(window.Count * FloorLocalPercentile / 100.0, 0, window.Count - 1)];

                if (prev_k < 0)
                {
                    for (int j = lo; j <= k; j++)
                        env[j] = v;
                }
                else
                {
                    for (int j = prev_k + 1; j <= k; j++)
                        env[j] = prev_v + (v - prev_v) * (j - prev_k) / (double)(k - prev_k);
                }
                prev_k = k;
                prev_v = v;

                if (k >= hi)
                    break;
            }

            if (env_ema == null || env_center_hz != center || env_span_hz != span)
            {
                env_ema = env;
                env_center_hz = center;
                env_span_hz = span;
            }
            else
            {
                for (int i = lo; i <= hi; i++)
                    env_ema[i] += FloorFollow * (env[i] - env_ema[i]);
            }
            return env_ema;
        }

        // the spur in the centre of the receiver: the bins around the centre that stick out of their surroundings
        private static void Despike(double[] db)
        {
            int n = db.Length;
            int c = (int)Math.Round(n / 2.0);
            int half = DespikeHalf;
            var neigh = new List<double>();
            for (int k = half + 2; k <= half + 11; k++)
            {
                if (c - k >= 0) neigh.Add(db[c - k]);
                if (c + k < n) neigh.Add(db[c + k]);
            }
            if (neigh.Count < 6)
                return;
            neigh.Sort();
            double med = neigh[neigh.Count / 2];
            for (int j = c - half; j <= c + half; j++)
            {
                if (j >= 0 && j < n && db[j] > med + DespikeDb)
                    db[j] = med;
            }
        }
    }
}
