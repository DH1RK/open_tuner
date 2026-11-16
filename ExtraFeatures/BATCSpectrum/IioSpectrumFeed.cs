using Serilog;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace opentuner
{
    // The QO-100 spectrum from an ADI Pluto (or another device with an AD9361/AD9363/AD9364 and the ADI firmware) read over libiio 1.0
    // and calculated here (#215, setup "ADI Pluto"). The device is set to the receiver (sampling rate, bandwidth, LO = view - LNB LO,
    // manual gain), IQ bursts are taken from the stream of libiio, and IioSpectrumProcessor makes the 922 values of the BATC format.
    //
    // Measured with the test tool Testing\IioSpectrum (Pluto Rev.B, ADI v0.39, 16 MS/s, 16 MHz): FFT of a block 4 to 5 ms, taking a block
    // 10.7 ms (the transfer of 262 KB, about 24 MB/s at most over the network). The device is read continuously, blocksPerSecond blocks
    // of BurstSamples samples a second, and every block goes into the average over time of IioSpectrumProcessor; the picture is made
    // four times a second from that average. So the smoothing comes from many blocks (the BATC server integrates all the time) and not
    // from a long time constant, the curve stays calm and still follows quickly. A stream of one block is used. When the blocks are
    // read slower than the device makes them, the ones in the queue are at most a few blocks old (after a long pause they would be old).
    //
    // The settings of the device are left as they are when the feed stops (nothing is restored).
    class IioSpectrumFeed : SpectrumFeed
    {
        const int BurstSamples = 65536;     // 4.1 ms at 16 MS/s
        const int FftSize = 4096;           // 3.9 kHz per bin at 16 MS/s
        const double UsableMHz = 7;         // the BATC band needs +-4.5 MHz around the centre
        const double DefaultViewMHz = 10495;
        static readonly double FrameIntervalMs = 250;
        static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
        static readonly TimeSpan MissingLibraryDelay = TimeSpan.FromSeconds(30);

        private readonly string uri;
        private readonly double view_mhz, lnb_lo_mhz, sample_rate_mhz, bandwidth_mhz, gain_db, average_seconds;
        private readonly bool flat_floor;
        private readonly int smooth_bins;
        private readonly double blocks_per_second;

        private readonly object run_lock = new object();
        private CancellationTokenSource cts;
        private iio.Stream active_stream;
        private volatile string problem;        // what the last attempt said, shown while there is no spectrum
        private string last_logged_problem;

        public IioSpectrumFeed(string uri, double view_mhz, double lnb_lo_mhz, double sample_rate_mhz, double bandwidth_mhz, double gain_db, double average_seconds, bool flat_floor, int smooth_bins = 2, double blocks_per_second = 30)
        {
            this.smooth_bins = smooth_bins;
            this.blocks_per_second = Math.Max(2, Math.Min(60, blocks_per_second));
            this.uri = uri == null ? "" : uri.Trim();
            this.view_mhz = view_mhz;
            this.lnb_lo_mhz = lnb_lo_mhz;
            this.sample_rate_mhz = sample_rate_mhz;
            this.bandwidth_mhz = bandwidth_mhz;
            this.gain_db = gain_db;
            this.average_seconds = average_seconds;
            this.flat_floor = flat_floor;
        }

        public override string Description => "ADI Pluto " + (uri.Length == 0 ? "(no address set)" : uri) + (problem != null ? ": " + problem : "");

        public override void start()
        {
            lock (run_lock)
            {
                if (cts != null)
                    return;     // running, the loop reconnects itself

                if (uri.Length == 0)
                {
                    problem = null;
                    return;     // nothing to connect to; the description says so
                }

                cts = new CancellationTokenSource();
                var token = cts.Token;
                Log.Information("ADI spectrum: start, " + uri);
                Task.Run(() => RunLoop(token));
            }
        }

        // the watchdog of BATCSpectrum calls this when no data comes any more; only while connected (otherwise the loop is busy connecting)
        public override void stop()
        {
            if (connected)
                Cancel();
        }

        public override void Shutdown()
        {
            Cancel();
        }

        private void Cancel()
        {
            bool was_connected;
            iio.Stream stream;
            lock (run_lock)
            {
                cts?.Cancel();
                cts = null;
                was_connected = connected;
                connected = false;
                stream = active_stream;
            }
            try { stream?.cancel(); }       // a next() that waits for the device returns
            catch (Exception) { }
            if (was_connected)
                RaiseConnectionStatusChanged(false);
        }

        private void RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TimeSpan delay = ReconnectDelay;
                try
                {
                    if (!IioNative.Load(out string why))
                    {
                        SetProblem("libiio not found (" + why + ")");
                        delay = MissingLibraryDelay;
                    }
                    else
                    {
                        Session(ct);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                        break;      // Cancel() made a call return with an error
                    SetProblem(ex.Message);
                }

                if (ct.IsCancellationRequested)
                    break;
                MarkDisconnected();
                if (ct.WaitHandle.WaitOne(delay))
                    break;
            }
        }

        private void SetProblem(string text)
        {
            problem = text;
            if (text != last_logged_problem)
            {
                last_logged_problem = text;
                Log.Warning("ADI spectrum: " + uri + ": " + text);
            }
        }

        private void MarkDisconnected()
        {
            bool was_connected;
            lock (run_lock)
            {
                was_connected = connected;
                connected = false;
            }
            if (was_connected)
                RaiseConnectionStatusChanged(false);
        }

        private static iio.Attr Attribute(iio.Channel channel, string name)
        {
            if (!channel.attrs.TryGetValue(name, out var attr))
                throw new InvalidOperationException("the device has no attribute " + name + " (an AD9361/AD9363/AD9364 with the ADI firmware is needed)");
            return attr;
        }

        // one connection: set the receiver, then frames until it is cancelled or something fails
        private void Session(CancellationToken ct)
        {
            using (var ctx = new iio.Context(uri))
            {
                ctx.set_timeout(5000);
                var phy = ctx.get_device("ad9361-phy");
                var adc = ctx.get_device("cf-ad9361-lpc");
                var rx = phy.get_channel("voltage0", false);
                var lo = phy.get_channel("RX_LO", true);

                double view = view_mhz > 0 ? view_mhz : DefaultViewMHz;
                long fs_hz = (long)Math.Round(sample_rate_mhz * 1e6);
                long bw_hz = (long)Math.Round(bandwidth_mhz * 1e6);
                long lo_hz = (long)Math.Round((view - lnb_lo_mhz) * 1e6);

                // the sampling rate first (it can change the bandwidth), then bandwidth, LO and gain
                Attribute(rx, "sampling_frequency").write(fs_hz);
                Attribute(rx, "rf_bandwidth").write(bw_hz);
                Attribute(lo, "frequency").write(lo_hz);
                Attribute(rx, "gain_control_mode").write("manual");
                Attribute(rx, "hardwaregain").write(gain_db);
                if (ct.WaitHandle.WaitOne(300))
                    return;

                long fs_now = Attribute(rx, "sampling_frequency").read_long();
                double center_mhz = Attribute(lo, "frequency").read_long() / 1e6;
                Log.Information("ADI spectrum: " + uri + ", " + (fs_now / 1e6).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " MS/s, centre "
                                + center_mhz.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " MHz (IF), view " + view.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                                + " MHz, gain " + gain_db.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " dB, "
                                + blocks_per_second.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " blocks/s, average " + average_seconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " s");

                var mask = new iio.ChannelsMask((uint)adc.channels.Count);
                try
                {
                    adc.get_channel("voltage0", false).enable(mask);
                    adc.get_channel("voltage1", false).enable(mask);
                    uint sample_size = adc.get_sample_size(mask);
                    var data = new byte[BurstSamples * sample_size];
                    var processor = new IioSpectrumProcessor(FftSize, fs_now, center_mhz, lnb_lo_mhz, UsableMHz, average_seconds, flat_floor) { SmoothBins = smooth_bins };

                    using (var stream = new iio.Stream(adc.buffers[0], mask, 1, BurstSamples))
                    {
                        lock (run_lock)
                        {
                            active_stream = stream;
                        }
                        try
                        {
                            FrameLoop(ct, stream, data, processor);
                        }
                        finally
                        {
                            lock (run_lock)
                            {
                                active_stream = null;
                            }
                        }
                    }
                }
                finally
                {
                    mask.Dispose();
                }
            }
        }

        // blocks at blocks_per_second go into the average over time, a picture is delivered every FrameIntervalMs
        private void FrameLoop(CancellationToken ct, iio.Stream stream, byte[] data, IioSpectrumProcessor processor)
        {
            var clock = Stopwatch.StartNew();
            double block_interval_ms = 1000.0 / blocks_per_second;
            long block_count = 0;
            double next_delivery_ms = FrameIntervalMs;
            while (!ct.IsCancellationRequested)
            {
                double wait = block_count * block_interval_ms - clock.Elapsed.TotalMilliseconds;
                if (wait > 1 && ct.WaitHandle.WaitOne((int)wait))
                    return;
                if (wait < -1000)
                    block_count = (long)(clock.Elapsed.TotalMilliseconds / block_interval_ms);     // far behind: no catching up
                block_count++;

                stream.next().read(data);
                processor.AddBlock(data, data.Length);

                double now = clock.Elapsed.TotalMilliseconds;
                if (now >= next_delivery_ms)
                {
                    next_delivery_ms = Math.Max(next_delivery_ms + FrameIntervalMs, now + FrameIntervalMs / 2);
                    Deliver(ct, processor.GetFrame());
                }
            }
        }

        private void Deliver(CancellationToken ct, ushort[] frame)
        {
            if (ct.IsCancellationRequested)
                return;

            lastdata = DateTime.Now;
            if (!connected)
            {
                connected = true;
                problem = null;
                last_logged_problem = null;
                Log.Information("ADI spectrum: connected, " + uri);
                RaiseConnectionStatusChanged(true);
            }
            callback?.Invoke(frame);
        }
    }
}
