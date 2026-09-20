using System;
using System.Drawing;
using System.Windows.Forms;
using opentuner.Utilities;
using Serilog;

namespace opentuner.MediaSources.Minitiouner
{
    // One tuner's group on the "Expert" tab, top to bottom: I/Q constellation, four gauges (Carrier Lock,
    // SR Lock, RF Power, C/N MER), lock time and the DSTATUS / DSTATUS2 fields of the demodulator as LEDs.
    public class ExpertTunerView
    {
        private const int RawLogIntervalMs = 2000;
        private const int FaultHoldMs = 1500; // DSTATUS2 fault bits are cleared by the read, so keep them visible for a moment

        private readonly string _title;
        private readonly CustomGroupBox _group;
        private readonly ConstellationControl _constellation = new ConstellationControl();
        private readonly GaugeControl _carrier_gauge = new GaugeControl("Carrier Lock", 0, 100, 20, 2);
        private readonly GaugeControl _sr_gauge = new GaugeControl("SR Lock", 0, 100, 20, 2);
        private readonly GaugeControl _rf_gauge = new GaugeControl("RF Power", -110, -10, 20, 2);
        private readonly GaugeControl _mer_gauge = new GaugeControl("C/N MER", -5, 15, 5, 5);

        private readonly Label _lock_time_label = new Label();
        private readonly Label _ldpc_label = new Label();
        private readonly Label _verror_label = new Label();

        // DSTATUS
        private readonly LedControl _car_lock_led = new LedControl("CAR_LOCK");
        private readonly LedControl _tmg_quality_led = new LedControl("TMGLOCK_QUALITY");
        private readonly LedControl _lock_definitif_led = new LedControl("LOCK_DEFINITIF");
        private readonly LedControl _ovadc_led = new LedControl("OVADC_DETECT");
        // DSTATUS2
        private readonly LedControl _delock_led = new LedControl("DEMOD_DELOCK");
        private readonly LedControl _agc1_led = new LedControl("AGC1_NOSIGNALACK");
        private readonly LedControl _agc2_led = new LedControl("AGC2_OVERFLOW");
        private readonly LedControl _cfr_led = new LedControl("CFR_OVERFLOW");
        private readonly LedControl _gamma_led = new LedControl("GAMMA_OVERUNDER");

        private readonly long[] _fault_until = new long[4]; // AGC1, AGC2, CFR, GAMMA
        private long _last_raw_log = 0;
        private double _last_cn_needed_db = double.NaN;

        public ExpertTunerView(string title, Control parent)
        {
            _title = title;

            _group = new CustomGroupBox();
            _group.Dock = DockStyle.Top;
            _group.Height = 642;
            _group.Text = title;
            _group.Font = new Font("Microsoft Sans Serif", 9.75F, FontStyle.Regular, GraphicsUnit.Point, (byte)0);
            _group.Padding = new Padding(8, 20, 8, 8);

            // Docking is evaluated last-added first, so the controls are added bottom to top.
            var tips = new ToolTip();
            tips.ShowAlways = true;

            var status = new FlowLayoutPanel();
            status.Dock = DockStyle.Top;
            status.Height = 132;
            status.Padding = new Padding(2, 2, 0, 0);
            AddLed(status, tips, _car_lock_led, "DSTATUS[7] CAR_LOCK: carrier lock");
            AddLed(status, tips, _tmg_quality_led, "DSTATUS[6:5] TMGLOCK_QUALITY: 00 timing not locked, 01 in process of being locked, 1x locked");
            AddLed(status, tips, _lock_definitif_led, "DSTATUS[3] LOCK_DEFINITIF: demodulator locked - the official locking indicator");
            AddLed(status, tips, _ovadc_led, "DSTATUS[0] OVADC_DETECT: persistent ADC overflow (more than 1/16 of the samples)");
            AddLed(status, tips, _delock_led, "DSTATUS2[7] DEMOD_DELOCK: LOCK_DEFINITIF went through zero since the last reset - the lock was lost at least once (stays set)");
            AddLed(status, tips, _agc1_led, "DSTATUS2[3] AGC1_NOSIGNALACK: tuner no signal, no signal at the ADC inputs (cleared by the read)");
            AddLed(status, tips, _agc2_led, "DSTATUS2[2] AGC2_OVERFLOW: AGC2 saturated at maximum amplification, no signal after Nyquist filtering (cleared by the read)");
            AddLed(status, tips, _cfr_led, "DSTATUS2[1] CFR_OVERFLOW: carrier frequency register reached the limit of CFRUP, CFRLOW or the tuner range (cleared by the read)");
            AddLed(status, tips, _gamma_led, "DSTATUS2[0] GAMMA_OVERUNDER: SFR reached the limit of SFRmin or SFRmax (cleared by the read)");
            _group.Controls.Add(status);

            var header = new Label();
            header.Dock = DockStyle.Top;
            header.Height = 20;
            header.Text = "DSTATUS / DSTATUS2";
            header.Font = new Font(_group.Font, FontStyle.Bold);
            header.TextAlign = ContentAlignment.BottomLeft;
            _group.Controls.Add(header);

            _verror_label.Dock = DockStyle.Top;
            _verror_label.Height = 26;
            _verror_label.Text = "VERROR (Viterbi):  -";
            _verror_label.TextAlign = ContentAlignment.MiddleLeft;
            _group.Controls.Add(_verror_label);

            _ldpc_label.Dock = DockStyle.Top;
            _ldpc_label.Height = 26;
            _ldpc_label.Text = "LDPC Iterations:  -";
            _ldpc_label.TextAlign = ContentAlignment.MiddleLeft;
            _group.Controls.Add(_ldpc_label);

            _lock_time_label.Dock = DockStyle.Top;
            _lock_time_label.Height = 26;
            _lock_time_label.Text = "Lock Time:  -";
            _lock_time_label.TextAlign = ContentAlignment.MiddleLeft;
            _group.Controls.Add(_lock_time_label);

            var gauges = new TableLayoutPanel();
            gauges.Dock = DockStyle.Top;
            gauges.Height = 150;
            gauges.RowCount = 1;
            gauges.ColumnCount = 4;
            for (int c = 0; c < 4; c++)
                gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            gauges.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var all = new GaugeControl[] { _carrier_gauge, _sr_gauge, _rf_gauge, _mer_gauge };
            for (int c = 0; c < all.Length; c++)
            {
                all[c].Dock = DockStyle.Fill;
                all[c].Margin = new Padding(2);
                gauges.Controls.Add(all[c], c, 0);
            }
            _group.Controls.Add(gauges);

            _constellation.Dock = DockStyle.Top;
            _constellation.Height = 240;
            _group.Controls.Add(_constellation);

            parent.Controls.Add(_group);
            _group.BringToFront(); // stacks the groups top to bottom in creation order
        }

        private static void AddLed(FlowLayoutPanel panel, ToolTip tips, LedControl led, string tip)
        {
            led.Margin = new Padding(0, 0, 0, 2);
            tips.SetToolTip(led, tip);
            panel.Controls.Add(led);
        }

        // demod_status: 2 = DVB-S2 locked, 3 = DVB-S locked (stv0910.DEMOD_S2 / DEMOD_S).
        // dstatus / dstatus2: the DSTATUS / DSTATUS2 registers. ldi: carrier lock indicator accumulator
        // (signed), tmglock: timing lock indicator accumulator (16 bit). constellation: 16 (I, Q) samples
        // or null. lock_time_ms: software-measured time to lock, -1 = not locked yet. cn_needed_db: C/N the
        // received MODCOD needs (NaN = unknown).
        public void Update(byte demod_status, short rf_dbm, double mer_db, byte dstatus, byte dstatus2, sbyte ldi, ushort tmglock,
                           uint symbol_rate, byte[,] constellation, double lock_time_ms, double cn_needed_db,
                           byte ldpc_iterations, byte ldpc_max_iterations, uint viterbi_error_rate)
        {
            bool locked = demod_status == stv0910.DEMOD_S || demod_status == stv0910.DEMOD_S2;
            long now = Environment.TickCount64;

            // constellation and gauges
            _constellation.AddSamples(constellation, constellation != null);
            _rf_gauge.SetValue(rf_dbm, rf_dbm.ToString() + " dBm");

            if (locked)
            {
                double carrier_percent = CarrierLockPercent(ldi);
                double sr_percent = SrLockPercent(tmglock);

                _carrier_gauge.SetValue(carrier_percent, carrier_percent.ToString("N0") + " %");
                _sr_gauge.SetValue(sr_percent, sr_percent.ToString("N0") + " %");
                _mer_gauge.SetValue(mer_db, mer_db.ToString("N1") + " dB");
                if (!double.IsNaN(cn_needed_db))
                    _last_cn_needed_db = cn_needed_db; // kept while a dummy frame (MODCOD 0) is reported in between
                _mer_gauge.SetBand(-5, _last_cn_needed_db, Color.Orange); // scale below the C/N the current MODCOD needs
            }
            else
            {
                _carrier_gauge.SetValue(0, "-");
                _sr_gauge.SetValue(0, "-");
                _mer_gauge.SetValue(-5, "-");
                _last_cn_needed_db = double.NaN;
                _mer_gauge.SetBand(double.NaN, double.NaN, Color.Orange);
            }


            SetText(_lock_time_label, "Lock Time:  " + LockTimeText(lock_time_ms));
            SetText(_ldpc_label, demod_status == stv0910.DEMOD_S2
                ? "LDPC Iterations:  " + ldpc_iterations + "   (max " + ldpc_max_iterations + ")"
                : "LDPC Iterations:  -");

            // VERROR: error rate seen by the Viterbi decoder, DVB-S (not S2) only. viterbi_error_rate is in 1/100 %.
            SetText(_verror_label, demod_status == stv0910.DEMOD_S
                ? "VERROR (Viterbi):  " + (viterbi_error_rate / 100.0).ToString("N2") + " %"
                : "VERROR (Viterbi):  -   (DVB-S only)");

            // DSTATUS: green = set / good
            _car_lock_led.Set((dstatus & 0x80) != 0 ? Color.LimeGreen : LedControl.OffColor);

            int timing_quality = (dstatus >> 5) & 0x03; // 00 not locked, 01 locking, 1x locked
            _tmg_quality_led.Set(timing_quality >= 2 ? Color.LimeGreen : timing_quality == 1 ? Color.Orange : LedControl.OffColor,
                                 "TMGLOCK_QUALITY: " + (timing_quality >= 2 ? "locked" : timing_quality == 1 ? "locking" : "not locked"));

            _lock_definitif_led.Set((dstatus & 0x08) != 0 ? Color.LimeGreen : LedControl.OffColor);
            _ovadc_led.Set((dstatus & 0x01) != 0 ? Color.Red : LedControl.OffColor);

            // DSTATUS2: DEMOD_DELOCK stays set until a write (orange = "lock was lost since start"), the
            // failure bits 3..0 are cleared by every read, so a hit is held visible for a moment
            _delock_led.Set((dstatus2 & 0x80) != 0 ? Color.Orange : LedControl.OffColor);
            SetFault(0, _agc1_led, (dstatus2 & 0x08) != 0, now);
            SetFault(1, _agc2_led, (dstatus2 & 0x04) != 0, now);
            SetFault(2, _cfr_led, (dstatus2 & 0x02) != 0, now);
            SetFault(3, _gamma_led, (dstatus2 & 0x01) != 0, now);

            LogRawValues(demod_status, dstatus, dstatus2, ldi, tmglock, symbol_rate, now);
        }

        private void SetFault(int index, LedControl led, bool set, long now)
        {
            if (set)
                _fault_until[index] = now + FaultHoldMs;

            led.Set(now < _fault_until[index] ? Color.Red : LedControl.OffColor);
        }

        private static void SetText(Control control, string text)
        {
            if (control.Text == text || !control.IsHandleCreated || control.IsDisposed)
                return;

            try
            {
                control.BeginInvoke((MethodInvoker)(() => control.Text = text));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
            }
        }

        // ms below one second, otherwise seconds. Software-measured, so the resolution is the poll interval.
        private static string LockTimeText(double lock_time_ms)
        {
            if (lock_time_ms < 0)
                return "-";

            return lock_time_ms < 1000 ? Math.Round(lock_time_ms).ToString("N0") + " ms" : (lock_time_ms / 1000.0).ToString("N2") + " s";
        }

        // PROVISIONAL - MiniTioune's own conversion is unknown. LDI is a signed 8 bit accumulator that
        // is "maximised when locked" (datasheet 5.10; lock above LDT = -48, lost below LDT2 = -72), so
        // -128..+127 is mapped to 0..100 %. To be calibrated against MiniTioune on the same signal.
        private static double CarrierLockPercent(sbyte ldi)
        {
            return Math.Max(0, Math.Min(100, (ldi + 128) * 100.0 / 255.0));
        }

        // PROVISIONAL - TMGLOCK is the 16 bit timing lock indicator accumulator (datasheet 5.8, thresholds
        // TMGTHRISE 0x1E / TMGTHFALL 0x08 are 8 bit), its high byte is mapped to 0..100 %. Same caveat.
        private static double SrLockPercent(ushort tmglock)
        {
            return Math.Max(0, Math.Min(100, (tmglock >> 8) * 100.0 / 255.0));
        }

        // Raw values every 2 s at debug level (--debuglevel 1) so the conversions above can be
        // calibrated against MiniTioune by comparing its gauges with these numbers.
        private void LogRawValues(byte demod_status, byte dstatus, byte dstatus2, sbyte ldi, ushort tmglock, uint symbol_rate, long now)
        {
            if (now - _last_raw_log < RawLogIntervalMs)
                return;

            _last_raw_log = now;
            Log.Debug("Expert " + _title + ": demod_status=" + demod_status + ", DSTATUS=0x" + dstatus.ToString("X2") +
                      ", DSTATUS2=0x" + dstatus2.ToString("X2") + ", LDI=" + ldi + " (0x" + ((byte)ldi).ToString("X2") +
                      "), TMGLOCK=" + tmglock + " (0x" + tmglock.ToString("X4") + "), SR=" + symbol_rate);
        }
    }
}
