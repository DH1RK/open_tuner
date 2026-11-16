using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace opentuner
{
    // One IQ burst of an AD9361/AD9363/AD9364 device (ADI Pluto) -> averaged power spectrum -> the 922 values of the BATC format (#215).
    //
    // The mapping of the bins to the QO-100 frequencies, the noise floor and the scale (11000 units, 3459 units per dB) are the ones of
    // the BATC server. The spectrum is not delivered by a device, it is calculated here (FFT of the IQ samples).
    // The test tool Testing\IioSpectrum compiles this file too, so the test and OpenTuner use the same code.
    sealed class IioSpectrumProcessor
    {
        public const int Bins = 922;                // BATC raster
        public const double StartMHz = 10490.5;
        public const double WidthMHz = 9.0;
        public const double FloorUnits = 11000;
        public const double UnitsPerDb = 65536.0 * 0.052778;   // OpenTuner CalcDbb

        const double FloorPercentile = 30;
        const int FloorHalfBins = 205;              // 2 MHz at 9.76 kHz per bin
        const int FloorStep = 4;
        const double FloorLocalPercentile = 30;
        const double FloorFollow = 0.2;             // per frame

        // The beacon (1500 kS at 10491.5 MHz, 10490.75 to 10492.25) is always there and wide. At the low edge of the band the window of the
        // floor only reaches to the high side, where the beacon fills more than two thirds of it: the "noise floor" then lay inside the
        // beacon and its low flank was cut off. Its bins are left out of the floor, the noise next to it gives the floor under it.
        const double FloorExcludeFromMHz = 10490.6;
        const double FloorExcludeToMHz = 10492.3;
        const int FloorMinWindowBins = 40;          // fewer bins left in a window: use all of them

        readonly int fft_size;
        readonly double sample_rate_hz;
        readonly double center_mhz;                 // IF centre (the RX LO of the device)
        readonly double lnb_lo_mhz;
        readonly double usable_mhz;                 // only bins within +- this around the centre are taken
        readonly double average_seconds;
        readonly bool flat_floor;

        readonly bool[] floor_excluded = new bool[Bins];    // the bins of the beacon, not used for the floor
        readonly double[] window;
        readonly double[] re, im, power;
        readonly double[] cos_t, sin_t;
        readonly int[] rev;

        double[] avg_power;
        double[] current_lin;                       // the spectrum as it is now: the average over time of the blocks added so far
        DateTime last_frame_utc = DateTime.MinValue;
        double floor_ema = double.NaN;
        double[] env_ema;

        // The spur of the receiver in the centre (the LO of the device): visible when the noise of the LNB does not cover it. The bins
        // around the centre that stick out of their surroundings are set to the surroundings.
        public bool Despike = true;
        const double DespikeDb = 1.5;
        const int DespikeHalf = 3;

        // Smoothing over the neighbouring bins of the 922: 0 = off, n = n times [1 2 1] / 4 (in linear power). The ripple of the noise
        // falls to 0.61 with the first pass and to about 0.5 with two (1 4 6 4 1, if the bins were independent; neighbours are a little
        // correlated, so it is somewhat less). A signal of 125 kS (17 bins) or wider is hardly changed, 66 kS (9 bins) a little, the 33 kS
        // ones (5 bins) lose some height. The BATC server delivers an averaged spectrum; this and the average over time make the curve as calm.
        public int SmoothBins = 2;

        // for the report
        public double[] LastDb { get; private set; }       // per FFT bin, centre in the middle
        public double[] LastBinDb { get; private set; }    // the 922 bins
        public int ValidBins { get; private set; }
        public double FloorDb { get; private set; }

        public IioSpectrumProcessor(int fft_size, double sample_rate_hz, double center_mhz, double lnb_lo_mhz, double usable_mhz, double average_seconds, bool flat_floor)
        {
            if (fft_size < 16 || (fft_size & (fft_size - 1)) != 0)
                throw new ArgumentException("the FFT size must be a power of two");
            this.fft_size = fft_size;
            this.sample_rate_hz = sample_rate_hz;
            this.center_mhz = center_mhz;
            this.lnb_lo_mhz = lnb_lo_mhz;
            this.usable_mhz = usable_mhz;
            this.average_seconds = average_seconds;
            this.flat_floor = flat_floor;

            for (int i = 0; i < Bins; i++)
            {
                double rf = StartMHz + (i + 1) * (WidthMHz / Bins);
                floor_excluded[i] = rf >= FloorExcludeFromMHz && rf <= FloorExcludeToMHz;
            }

            window = new double[fft_size];
            for (int k = 0; k < fft_size; k++)
                window[k] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * k / fft_size);     // Hann (periodic)
            re = new double[fft_size];
            im = new double[fft_size];
            power = new double[fft_size];
            cos_t = new double[fft_size / 2];
            sin_t = new double[fft_size / 2];
            for (int k = 0; k < fft_size / 2; k++)
            {
                cos_t[k] = Math.Cos(2 * Math.PI * k / fft_size);
                sin_t[k] = Math.Sin(2 * Math.PI * k / fft_size);
            }
            int bits = 0;
            while ((1 << bits) < fft_size)
                bits++;
            rev = new int[fft_size];
            for (int i = 0; i < fft_size; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++)
                    if ((i & (1 << b)) != 0)
                        r |= 1 << (bits - 1 - b);
                rev[i] = r;
            }
        }

        // one block and the picture of it (the test tool does it this way: one block per frame)
        public ushort[] Process(byte[] data, int length)
        {
            AddBlock(data, length);
            return GetFrame();
        }

        // The device is read continuously and every block goes into the average over time (average_seconds is the time constant, the
        // blocks come at their own rate); GetFrame makes the picture from it, a few times a second. So the smoothing comes from many
        // blocks, not from a long time, and the picture still follows quickly.
        // data: the bytes of one block, interleaved 16 bit I and Q (little endian), as the device delivers them
        public void AddBlock(byte[] data, int length)
        {
            var samples = MemoryMarshal.Cast<byte, short>(new ReadOnlySpan<byte>(data, 0, length & ~3));
            int n = samples.Length / 2;
            int segments = n / fft_size;
            if (segments < 1)
                throw new ArgumentException("a burst of " + n + " samples is shorter than one FFT (" + fft_size + ")");

            double mean_i = 0, mean_q = 0;      // the DC offset of the receiver
            for (int i = 0; i < n; i++)
            {
                mean_i += samples[2 * i];
                mean_q += samples[2 * i + 1];
            }
            mean_i /= n;
            mean_q /= n;

            Array.Clear(power, 0, power.Length);
            for (int s = 0; s < segments; s++)
            {
                int off = s * fft_size;
                for (int k = 0; k < fft_size; k++)
                {
                    re[k] = (samples[2 * (off + k)] - mean_i) * window[k];
                    im[k] = (samples[2 * (off + k) + 1] - mean_q) * window[k];
                }
                Fft();
                for (int k = 0; k < fft_size; k++)
                    power[k] += re[k] * re[k] + im[k] * im[k];
            }

            // centre of the spectrum in the middle, average over the FFTs of the burst
            var lin = new double[fft_size];
            for (int k = 0; k < fft_size; k++)
                lin[k] = power[(k + fft_size / 2) % fft_size] / segments + 1e-9;

            if (Despike)
                DespikeCentre(lin);

            // average over time (linear power): unaveraged the noise ripples by several dB and
            // OpenTuner takes every 1.5 dB above the floor for a signal
            if (average_seconds > 0)
            {
                var now = DateTime.UtcNow;
                double dt = (now - last_frame_utc).TotalSeconds;
                last_frame_utc = now;
                if (avg_power == null || dt > 5)
                    avg_power = (double[])lin.Clone();
                else
                {
                    double alpha = 1 - Math.Exp(-Math.Max(dt, 0.001) / average_seconds);
                    for (int k = 0; k < fft_size; k++)
                        avg_power[k] += alpha * (lin[k] - avg_power[k]);
                }
                lin = avg_power;
            }
            current_lin = lin;
        }

        // the 922 values of the BATC format from the average over time of the blocks added so far
        public ushort[] GetFrame()
        {
            var lin = current_lin;
            if (lin == null)
                throw new InvalidOperationException("no block was added yet");

            var db = new double[fft_size];
            for (int k = 0; k < fft_size; k++)
                db[k] = 10 * Math.Log10(lin[k]);
            LastDb = db;

            // the 922 bins of the BATC band (same rule as OpenTuner signal.cs: start + (bin + 1) / 922 * 9)
            double span_mhz = sample_rate_hz / 1e6;
            double bin_w = WidthMHz / Bins;
            var out_db = new double[Bins];
            var valid = new bool[Bins];
            int valid_count = 0;
            for (int i = 0; i < Bins; i++)
            {
                double rf = StartMHz + (i + 1) * bin_w;
                double off = rf - lnb_lo_mhz - center_mhz;
                if (Math.Abs(off) > usable_mhz || Math.Abs(off) > span_mhz / 2)
                    continue;

                double x0 = ((off - bin_w / 2) / span_mhz + 0.5) * fft_size;
                double x1 = ((off + bin_w / 2) / span_mhz + 0.5) * fft_size;
                int a = Math.Max((int)Math.Floor(x0), 0);
                int b = Math.Min(Math.Max((int)Math.Ceiling(x1) - 1, a), fft_size - 1);
                if (a > b)
                    continue;

                double p = 0;
                for (int k = a; k <= b; k++)
                    p += Math.Pow(10, db[k] / 10);
                out_db[i] = 10 * Math.Log10(p / (b - a + 1));
                valid[i] = true;
                valid_count++;
            }
            for (int pass = 0; pass < SmoothBins; pass++)
                SmoothOnce(out_db, valid);

            ValidBins = valid_count;
            LastBinDb = out_db;

            var data_out = new ushort[Bins];
            if (valid_count < 20)
            {
                for (int i = 0; i < Bins; i++)
                    data_out[i] = (ushort)FloorUnits;
                return data_out;
            }

            // noise floor: percentile over the valid bins, smoothed over time so the levels do not flicker
            var vals = new List<double>(valid_count);
            for (int i = 0; i < Bins; i++)
                if (valid[i]) vals.Add(out_db[i]);
            vals.Sort();
            double floor_now = vals[(int)Math.Clamp(vals.Count * FloorPercentile / 100.0, 0, vals.Count - 1)];
            floor_ema = double.IsNaN(floor_ema) || Math.Abs(floor_now - floor_ema) > 6 ? floor_now : floor_ema + 0.1 * (floor_now - floor_ema);
            FloorDb = floor_ema;

            // flat noise floor: every bin is measured against the noise around it
            double[] local_floor = flat_floor ? UpdateLocalFloor(out_db, valid) : null;
            for (int i = 0; i < Bins; i++)
            {
                double units = valid[i] ? FloorUnits + (out_db[i] - (local_floor != null ? local_floor[i] : floor_ema)) * UnitsPerDb : FloorUnits;
                data_out[i] = (ushort)Math.Clamp(Math.Round(units), 0, 65535);
            }
            return data_out;
        }

        // The noise floor of every bin is a low percentile of the bins around it (+-2 MHz), calculated every FloorStep bins,
        // interpolated in between and followed over the frames.
        double[] UpdateLocalFloor(double[] out_db, bool[] valid)
        {
            int lo = Array.IndexOf(valid, true);
            int hi = Array.LastIndexOf(valid, true);
            var env = new double[Bins];
            if (lo < 0)
                return env;

            var win = new List<double>(2 * FloorHalfBins + 1);
            int prev_k = -1;
            double prev_v = 0;
            for (int k = lo; ; k += FloorStep)
            {
                if (k > hi)
                    k = hi;

                win.Clear();
                for (int j = Math.Max(lo, k - FloorHalfBins); j <= Math.Min(hi, k + FloorHalfBins); j++)
                    if (!floor_excluded[j])
                        win.Add(out_db[j]);
                if (win.Count < FloorMinWindowBins)     // (almost) only the beacon in reach: better than nothing
                {
                    win.Clear();
                    for (int j = Math.Max(lo, k - FloorHalfBins); j <= Math.Min(hi, k + FloorHalfBins); j++)
                        win.Add(out_db[j]);
                }
                win.Sort();
                double v = win[(int)Math.Clamp(win.Count * FloorLocalPercentile / 100.0, 0, win.Count - 1)];

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

            if (env_ema == null)
                env_ema = env;
            else
            {
                for (int i = lo; i <= hi; i++)
                    env_ema[i] += FloorFollow * (env[i] - env_ema[i]);
            }
            return env_ema;
        }

        // [1 2 1] / 4 over the neighbouring valid bins, in linear power (db is changed in place)
        static void SmoothOnce(double[] db, bool[] valid)
        {
            var lin = new double[db.Length];
            for (int i = 0; i < db.Length; i++)
                lin[i] = valid[i] ? Math.Pow(10, db[i] / 10) : 0;
            for (int i = 0; i < db.Length; i++)
            {
                if (!valid[i])
                    continue;
                double sum = 2 * lin[i], weight = 2;
                if (i > 0 && valid[i - 1]) { sum += lin[i - 1]; weight += 1; }
                if (i < db.Length - 1 && valid[i + 1]) { sum += lin[i + 1]; weight += 1; }
                db[i] = 10 * Math.Log10(sum / weight);
            }
        }

        static void DespikeCentre(double[] lin)
        {
            int n = lin.Length, c = n / 2;
            var neigh = new List<double>();
            for (int k = DespikeHalf + 2; k <= DespikeHalf + 11; k++)
            {
                if (c - k >= 0) neigh.Add(10 * Math.Log10(lin[c - k]));
                if (c + k < n) neigh.Add(10 * Math.Log10(lin[c + k]));
            }
            if (neigh.Count < 6)
                return;
            neigh.Sort();
            double med = neigh[neigh.Count / 2];
            double med_lin = Math.Pow(10, med / 10);
            for (int j = c - DespikeHalf; j <= c + DespikeHalf; j++)
                if (j >= 0 && j < n && 10 * Math.Log10(lin[j]) > med + DespikeDb)
                    lin[j] = med_lin;
        }

        // in-place radix-2 FFT of re/im (forward, e^-j...)
        void Fft()
        {
            int n = fft_size;
            for (int i = 0; i < n; i++)
            {
                int j = rev[i];
                if (j > i)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len >> 1;
                int step = n / len;
                for (int i = 0; i < n; i += len)
                {
                    for (int k = 0; k < half; k++)
                    {
                        double wr = cos_t[k * step], wi = -sin_t[k * step];
                        int a = i + k, b = a + half;
                        double tr = re[b] * wr - im[b] * wi;
                        double ti = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                    }
                }
            }
        }
    }
}
