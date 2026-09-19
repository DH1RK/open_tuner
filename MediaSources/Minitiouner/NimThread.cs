using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using opentuner.MediaSources.Minitiouner.HardwareInterfaces;
using opentuner.MediaSources.Minitiouner;
using Serilog;

namespace opentuner
{
    public delegate void SourceStatusCallback(TunerStatus status);

    public class NimThread
    {
        MTHardwareInterface hardware;

        nim _nim;
        stv0910 _stv0910;
        stv6120 _stv6120;
        stvvglna stvvglna_top;
        stvvglna stvvglna_bottom;

        ConcurrentQueue<TunerConfig> config_queue;
        private List<SourceStatusCallback> status_callback = null;

        bool lna_top_ok = false;
        bool lna_bottom_ok = false;
        bool reset = false;
        bool no_lna = false;

        // last applied config per tuner (index 0 = tuner 1 / T1P2, index 1 = tuner 2 / T2P1) -
        // used by the Digole display update in get_nim_status(), which otherwise has no
        // access to the currently tuned frequency/symbol rate (nim_config in worker_thread()
        // is a loop-local variable).
        private TunerConfig[] current_config = new TunerConfig[2];

        private DigoleDisplay digole;
        private bool digole_enabled;
        private string device_name;
        private string digole_callsign;
        private bool digole_showed_greeting = false;
        // First no-lock greeting after connect is labelled "START", later ones "NO SIGNAL".
        private bool digole_start_shown = false;
        // While set (Environment.TickCount64 deadline), update_digole_display() leaves the
        // screen alone - used by the manual "Send Final Now" test so the END screen stays
        // visible for a few seconds instead of being redrawn by the next ~200ms live update.
        private long digole_hold_until = 0;

        // Service name (from TS/SDT parsing) and video codec (from the media player's
        // OpenCompleted event) live on different threads (ts_parser_thread / media player)
        // than NimThread's own I2C polling loop - MinitiounerProperties.cs pushes the latest
        // values here (plain reference assignment, atomic enough for a display refresh) so
        // update_digole_display() can read them without crossing threads itself.
        private string[] service_name = new string[] { "", "" };
        private string[] video_codec = new string[] { "", "" };

        public void UpdateServiceName(int tuner_index, string name)
        {
            if (tuner_index >= 0 && tuner_index < service_name.Length)
                service_name[tuner_index] = name;
        }

        public void UpdateVideoCodec(int tuner_index, string codec)
        {
            if (tuner_index >= 0 && tuner_index < video_codec.Length)
                video_codec[tuner_index] = codec;
        }

        // One-shot DiSEqC tone burst trigger (22kHz "TS" mode) - set from the UI thread,
        // consumed once and cleared in worker_thread()'s own polling loop so the actual I2C
        // sequence runs on the same thread as the rest of the NIM traffic.
        private volatile bool trigger_burst_0 = false;
        private volatile bool trigger_burst_1 = false;

        public void TriggerToneBurst(int tuner_index)
        {
            if (tuner_index == 0) trigger_burst_0 = true;
            else if (tuner_index == 1) trigger_burst_1 = true;
        }

        // Direct EN_LNB/SEL_LNB pin test toggles (debug/isolation aid) - set from the UI thread,
        // applied once and cleared in worker_thread()'s own loop so the actual GPIO write runs on
        // the same thread as the rest of the NIM I2C traffic (see hw_gpio_write_test's doc
        // comment for why this can't be called directly from the UI thread).
        private volatile bool test_gpio_pending = false;
        private MTHardwareInterface.TestGpioPin test_gpio_pin;
        private volatile bool test_gpio_value;

        public void SetTestGpio(MTHardwareInterface.TestGpioPin pin, bool value)
        {
            test_gpio_pin = pin;
            test_gpio_value = value;
            test_gpio_pending = true;
        }

        // Debug aid for the "Digole doesn't clear on exit" investigation: fires the exact same
        // ShowGreeting()/Clear() call worker_thread() makes on its way out, but on demand while
        // the thread is fully alive and NOT shutting down - isolates whether the I2C write
        // itself is the problem, or whether it's specific to the Close()/Stop()/Join sequence.
        // Same one-shot/flag-consumed-on-worker-thread pattern as SetTestGpio above, for the
        // same reason (must run on NimThread's own thread under HwLock).
        private volatile bool digole_final_test_pending = false;

        public void TriggerDigoleFinalTest()
        {
            digole_final_test_pending = true;
        }

        // Which tuner's data to show on the Digole when BOTH are locked at once (only one
        // physical display for two tuners) - updated on every SetFrequency call, so the last
        // tuner the user actually selected (BATC spectrum click, tuner dialog, preset, ...)
        // wins. Doesn't override a genuine lock: if only the OTHER tuner is locked, that one
        // still shows (see update_digole_display) - this only breaks the tie between two
        // simultaneously locked tuners.
        private volatile int preferred_tuner = 0;

        public void SetPreferredTuner(int tuner_index)
        {
            preferred_tuner = tuner_index;
        }

        // Thread.Abort() doesn't exist on modern .NET (throws PlatformNotSupportedException) -
        // worker_thread() checks this cooperatively instead.
        private volatile bool _stopRequested = false;
        public void Stop() { _stopRequested = true; }

        // Guards every access to the shared FTDI I2C-channel state (MPSSEbuffer etc.) from
        // this thread's own hardware calls below. MinitiounerSource.Close() takes the same
        // lock before its own hw_ts_led/hw_set_polarization_supply calls, so LED/LNB shutoff
        // no longer has to wait for the full worker_thread() Join to finish (which could hang
        // for 20s+ on a stuck I2C retry, see MinitiounerSource.Close()'s comment) - it only
        // ever waits as long as whatever hardware call NimThread actually has in flight right
        // now, typically well under a second.
        public readonly object HwLock = new object();

        //byte current_demod = stv0910.STV0910_DEMOD_BOTTOM;  

        public event EventHandler<StatusEvent> onNewStatus;

        // Offsets (LNB LO frequency etc., MinitiounerSettings.Offset1/Offset2) added back onto
        // the tuned IF frequency for display, so the Digole shows the real downlink frequency
        // (e.g. "10491500 kHz") rather than the internal IF value the STV6120 is actually set
        // to (current_config[i].frequency, e.g. 741525) - matches MinitiounerSource.GetFrequency's
        // offset_included=true behavior, which the main GUI itself uses for the same reason.
        private uint[] frequency_offsets;

        public NimThread(ConcurrentQueue<TunerConfig> _config_queue, MTHardwareInterface _hardware, SourceStatusCallback _status_callback, bool _no_lna, bool _enable_digole = false, byte _digole_i2c_address = 0x27, string _device_name = "", uint[] _frequency_offsets = null, string _digole_callsign = "")
        {
            hardware = _hardware;
            config_queue = _config_queue;
            //status_callback = _status_callback;
            status_callback = new List<SourceStatusCallback>();
            status_callback.Add(_status_callback);

            _nim = new nim(hardware);

            _stv0910 = new stv0910(_nim);
            _stv6120 = new stv6120(_nim);
            no_lna = _no_lna;
            stvvglna_top = new stvvglna(_nim);
            stvvglna_bottom = new stvvglna(_nim);

            digole_enabled = _enable_digole;
            device_name = _device_name;
            digole_callsign = _digole_callsign;
            frequency_offsets = _frequency_offsets ?? new uint[] { 0, 0 };
            if (digole_enabled)
            {
                digole = new DigoleDisplay(hardware, _digole_i2c_address);
            }
        }

        // Picks whichever tuner is currently locked (T1P2/"TUNER A" preferred, falling back to
        // T2P1/"TUNER B") and pushes its status to the Digole display - mirrors the single-tuner
        // layout of the existing MiniTioune Digole integration this is modeled on.
        private void update_digole_display(TunerStatus status)
        {
            if (digole_hold_until != 0)
            {
                if (Environment.TickCount64 < digole_hold_until)
                    return;

                // hold over - force the proper state (greeting or live status) to be redrawn
                digole_hold_until = 0;
                digole_showed_greeting = false;
            }

            // Initialize() always auto-tunes both channels to a fixed placeholder (741525 kHz
            // IF / 1500 KS/s) right at connect, before the user picks a real frequency via the
            // BATC spectrum click - so current_config[i] != null almost immediately and is not
            // a meaningful "user tuned in" signal. Gate the greeting on genuine demod lock
            // instead: it stays up through that meaningless placeholder tune (which won't lock
            // onto anything real) and only switches to status once a real signal is received.
            bool t1_locked = status.T1P2_demod_status == stv0910.DEMOD_S || status.T1P2_demod_status == stv0910.DEMOD_S2;
            bool t2_locked = status.T2P1_demod_status == stv0910.DEMOD_S || status.T2P1_demod_status == stv0910.DEMOD_S2;

            if (!t1_locked && !t2_locked)
            {
                // Show a callsign greeting instead of a meaningless placeholder frequency, but
                // only once (not every ~200ms poll tick).
                if (!digole_showed_greeting)
                {
                    digole_showed_greeting = true;
                    digole.ShowGreeting(device_name, digole_callsign, digole_start_shown ? "NO SIGNAL" : "START");
                    digole_start_shown = true;
                }
                return;
            }

            int tuner_index;
            string tuner_label;

            if (t1_locked && t2_locked)
            {
                tuner_index = preferred_tuner == 1 ? 1 : 0;
            }
            else if (t1_locked)
            {
                tuner_index = 0;
            }
            else
            {
                tuner_index = 1;
            }

            tuner_label = tuner_index == 0 ? "TUNER A" : "TUNER B";

            digole_showed_greeting = false; // a tuner is now locked - back to normal status updates

            TunerConfig cfg = current_config[tuner_index];
            if (cfg == null)
                return;

            long freq_kHz = cfg.frequency + frequency_offsets[tuner_index];
            uint sr_kS = cfg.symbol_rate;
            short rf_level_dBm = tuner_index == 0 ? status.T1P2_input_power_level : status.T2P1_input_power_level;
            byte demod_status = tuner_index == 0 ? status.T1P2_demod_status : status.T2P1_demod_status;
            uint modcode = tuner_index == 0 ? status.T1P2_modcode : status.T2P1_modcode;
            double mer_dB = (tuner_index == 0 ? status.T1P2_mer : status.T2P1_mer) / 10.0;

            string modcod_name = "";
            if (demod_status == stv0910.DEMOD_S2 && lookups.modcod_lookup_dvbs2.ContainsKey(modcode))
                modcod_name = lookups.modcod_lookup_dvbs2[modcode];
            else if (demod_status == stv0910.DEMOD_S && lookups.modcod_lookup_dvbs.ContainsKey(modcode))
                modcod_name = lookups.modcod_lookup_dvbs[modcode];

            digole.UpdateStatus(device_name, tuner_label, freq_kHz, sr_kS, rf_level_dBm, mer_dB,
                service_name[tuner_index], video_codec[tuner_index], modcod_name);
        }

        public void register_callback(SourceStatusCallback cb)
        {
            status_callback.Add(cb);
        }

        // https://wiki.batc.org.uk/MiniTiouner_Power_Level_Indication
        short get_rf_level(ushort agc1, ushort agc2)
        {
            int index = -1;

            if (agc1 >= 0)
            {
                index = lookups.agc1_lookup.BinarySearch(agc1);

                if (index < 0)
                    index = ~index;
            }
            else
            {
                index = lookups.agc2_lookup.BinarySearch(agc2);

                if (index < 0)
                    index = ~index;

            }

            if (index < 0) index = 0;

            if (index >= lookups.rf_power_level.Count())
                index = lookups.rf_power_level.Count() - 1;

            return lookups.rf_power_level[index];
        }

        byte get_nim_status()
        {
            TunerStatus nim_status = new TunerStatus();

            byte err = 0;

            nim_status.T1P2_reset = reset;

            /*
            if (no_lna)
            {
                nim_status.lna_bottom_ok = false;
                nim_status.lna_top_ok = false;
                nim_status.lna_gain = 0;
            }
            else
            {
                // get lna info
                nim_status.lna_bottom_ok = lna_bottom_ok;
                nim_status.lna_top_ok = lna_top_ok;

                byte lna_gain = 0, lna_vgo = 0;
                if (err == 0) stvvglna_top.stvvglna_read_agc(nim.NIM_INPUT_TOP, ref lna_gain, ref lna_vgo);
                nim_status.lna_gain = (ushort)((lna_gain << 5) | lna_vgo);
            }
            */

            byte rf_input_1 = 0;
            byte rf_input_2 = 0;
            byte temp = 0;
            err = _stv6120.stv6120_read_rf_sel(ref temp);

            rf_input_1 = (byte)(temp & stv6120_regs.STV6120_CTRL9_RFSEL_1_MASK);
            rf_input_2 = (byte)(temp & stv6120_regs.STV6120_CTRL9_RFSEL_2_MASK);

            if (rf_input_1 == 1)
                nim_status.T1P2_rf_input = nim.NIM_INPUT_TOP;
            else
                nim_status.T1P2_rf_input = nim.NIM_INPUT_BOTTOM;

            if (rf_input_2 == 4)
                nim_status.T2P1_rf_input = nim.NIM_INPUT_TOP;
            else
                nim_status.T2P1_rf_input = nim.NIM_INPUT_BOTTOM;

            // get scan state (demod state)
            byte demod_state = 0;
            err = _stv0910.stv0910_read_scan_state(stv0910.STV0910_DEMOD_TOP, ref demod_state);
            nim_status.T1P2_demod_status = demod_state;

            byte demod2_state = 0;
            err = _stv0910.stv0910_read_scan_state(stv0910.STV0910_DEMOD_BOTTOM, ref demod2_state);
            nim_status.T2P1_demod_status = demod2_state;

            // power
            byte power_i = 0;
            byte power_q = 0;
            if (err == 0) err = _stv0910.stv0910_read_power(stv0910.STV0910_DEMOD_TOP, ref power_i, ref power_q);
            nim_status.T1P2_power_i = power_i;
            nim_status.T1P2_power_q = power_q;

            byte[,] constellation_data = new byte[16, 2];

            byte con_i = 0;
            byte con_q = 0;
            if (err == 0)
            {
                for (byte count = 0; count < 16; count++)
                {
                    _stv0910.stv0910_read_constellation(stv0910.STV0910_DEMOD_TOP, ref con_i, ref con_q);
                    constellation_data[count,0] = con_i;
                    constellation_data[count,1] = con_q;
                }
            }

            nim_status.T1P2_constellation = constellation_data;

            /* LDPC Error Count */
            UInt32 errors_ldpc_count = 0;
            if (err == 0) err = _stv0910.stv0910_read_errors_ldpc_count(stv0910.STV0910_DEMOD_TOP, ref errors_ldpc_count);
            nim_status.errors_ldpc_count = errors_ldpc_count;

            /* puncture rate */
            byte puncture_rate = 0;
            if (err == 0) err = _stv0910.stv0910_read_puncture_rate(stv0910.STV0910_DEMOD_TOP, ref puncture_rate);
            nim_status.T1P2_puncture_rate = puncture_rate;

            if (err == 0) err = _stv0910.stv0910_read_puncture_rate(stv0910.STV0910_DEMOD_BOTTOM, ref puncture_rate);
            nim_status.T2P1_puncture_rate = puncture_rate;

            /* carrier frequency offset we are trying */
            Int32 frequency_offset = 0;
            if (err == 0) err = _stv0910.stv0910_read_car_freq(stv0910.STV0910_DEMOD_TOP, ref frequency_offset);
            nim_status.T1P2_frequency_carrier_offset = frequency_offset;
            if (err == 0) err = _stv0910.stv0910_read_car_freq(stv0910.STV0910_DEMOD_BOTTOM, ref frequency_offset);
            nim_status.T2P1_frequency_carrier_offset = frequency_offset;

            /* symbol rate we are trying */
            UInt32 sr = 0;
            if (err == 0) err = _stv0910.stv0910_read_sr(stv0910.STV0910_DEMOD_TOP, ref sr);
            nim_status.T1P2_symbol_rate = sr;
            if (err == 0) err = _stv0910.stv0910_read_sr(stv0910.STV0910_DEMOD_BOTTOM, ref sr);
            nim_status.T2P1_symbol_rate = sr;

            /* viterbi error rate */
            UInt32 viterbi_error_rate = 0;
            if (err == 0) err = _stv0910.stv0910_read_err_rate(stv0910.STV0910_DEMOD_TOP, ref viterbi_error_rate);
            nim_status.T1P2_viterbi_error_rate = viterbi_error_rate;
            if (err == 0) err = _stv0910.stv0910_read_err_rate(stv0910.STV0910_DEMOD_BOTTOM, ref viterbi_error_rate);
            nim_status.T2P1_viterbi_error_rate = viterbi_error_rate;

            /* BER */
            UInt32 ber = 0;
            if (err == 0) err = _stv0910.stv0910_read_ber(stv0910.STV0910_DEMOD_TOP, ref ber);
            nim_status.T1P2_ber = ber;
            if (err == 0) err = _stv0910.stv0910_read_ber(stv0910.STV0910_DEMOD_BOTTOM, ref ber);
            nim_status.T2P1_ber = ber;

            /* BCH Uncorrected Flag */
            bool errors_bch_uncorrected = false;
            if (err == 0) err = _stv0910.stv0910_read_errors_bch_uncorrected(stv0910.STV0910_DEMOD_TOP, ref errors_bch_uncorrected);
            nim_status.T1P2_errors_bch_uncorrected = errors_bch_uncorrected;
            if (err == 0) err = _stv0910.stv0910_read_errors_bch_uncorrected(stv0910.STV0910_DEMOD_BOTTOM, ref errors_bch_uncorrected);
            nim_status.T2P1_errors_bch_uncorrected = errors_bch_uncorrected;

            /* BCH Error Count */
            UInt32 errors_bch_count = 0;
            if (err == 0) err = _stv0910.stv0910_read_errors_bch_count(stv0910.STV0910_DEMOD_TOP, ref errors_bch_count);
            nim_status.T1P2_errors_bch_count = errors_bch_count;
            if (err == 0) err = _stv0910.stv0910_read_errors_bch_count(stv0910.STV0910_DEMOD_BOTTOM, ref errors_bch_count);
            nim_status.T2P1_errors_bch_count = errors_bch_count;


            // agc1 gain
            ushort agc1_gain = 0;
            if (err == 0) err = _stv0910.stv0910_read_agc1_gain(stv0910.STV0910_DEMOD_TOP, ref agc1_gain);
            nim_status.T1P2_agc1_gain = agc1_gain;
            if (err == 0) err = _stv0910.stv0910_read_agc1_gain(stv0910.STV0910_DEMOD_BOTTOM, ref agc1_gain);
            nim_status.T2P1_agc1_gain = agc1_gain;

            // agc2 gain
            ushort agc2_gain = 0;
            if (err == 0) err = _stv0910.stv0910_read_agc2_gain(stv0910.STV0910_DEMOD_TOP, ref agc2_gain);
            nim_status.T1P2_agc2_gain = agc2_gain;
            nim_status.T1P2_input_power_level = get_rf_level(agc1_gain, agc2_gain);

            if (err == 0) err = _stv0910.stv0910_read_agc2_gain(stv0910.STV0910_DEMOD_BOTTOM, ref agc2_gain);
            nim_status.T2P1_agc2_gain = agc2_gain;
            nim_status.T2P1_input_power_level = get_rf_level(agc1_gain, agc2_gain);

            // ma type
            UInt32 ma_type1 = 0;
            UInt32 ma_type2 = 0;

            if (err == 0) _stv0910.stv0910_read_matype(stv0910.STV0910_DEMOD_TOP, ref ma_type1, ref ma_type2);
            nim_status.T1P2_stream_format = (ma_type1 & 0xC0) >> 6; ;

            if (err == 0) _stv0910.stv0910_read_matype(stv0910.STV0910_DEMOD_BOTTOM, ref ma_type1, ref ma_type2);
            nim_status.T2P1_stream_format = (ma_type1 & 0xC0) >> 6; ;

            Int32 mer = 0;

            if (nim_status.T1P2_demod_status == stv0910.DEMOD_S || nim_status.T1P2_demod_status == stv0910.DEMOD_S2)
            {
                if (err == 0) err = _stv0910.stv0910_read_mer(stv0910.STV0910_DEMOD_TOP, ref mer);
            }

            nim_status.T1P2_mer = mer;

            if (nim_status.T2P1_demod_status == stv0910.DEMOD_S || nim_status.T2P1_demod_status == stv0910.DEMOD_S2)
            {
                if (err == 0) err = _stv0910.stv0910_read_mer(stv0910.STV0910_DEMOD_BOTTOM, ref mer);
            }

            nim_status.T2P1_mer = mer;

            if (digole_enabled && digole != null)
            {
                update_digole_display(nim_status);
            }

            /* MODCOD, Short Frames, Pilots */
            UInt32 modcod = 0;
            bool short_frame = false;
            bool pilots = false;
            byte rolloff = 0;
            
            if (err == 0) err = _stv0910.stv0910_read_modcod_and_type(stv0910.STV0910_DEMOD_TOP, ref modcod, ref short_frame, ref pilots, ref rolloff);
            nim_status.T1P2_modcode = modcod;
            nim_status.T1P2_rolloff = rolloff;
            nim_status.T1P2_short_frame = short_frame;
            nim_status.T1P2_pilots = pilots;

            if (err == 0) err = _stv0910.stv0910_read_modcod_and_type(stv0910.STV0910_DEMOD_BOTTOM, ref modcod, ref short_frame, ref pilots, ref rolloff);
            nim_status.T2P1_modcode = modcod;
            nim_status.T2P1_rolloff = rolloff;
            nim_status.T2P1_short_frame = short_frame;
            nim_status.T2P1_pilots = pilots;

            // TSSTATUS - decoded TS_VALID/TS_ERR/sync bits, read directly via I2C (see
            // stv0910_read_ts_status_decoded's doc comment for why this replaces the unused
            // hardware BC3_1/BC3_2 NAND signal).
            bool ts_line_ok = false, ts_error = false, ts_nosync = false;
            if (err == 0) err = _stv0910.stv0910_read_ts_status_decoded(stv0910.STV0910_DEMOD_TOP, ref ts_line_ok, ref ts_error, ref ts_nosync);
            nim_status.T1P2_ts_line_ok = ts_line_ok;
            nim_status.T1P2_ts_error = ts_error;
            nim_status.T1P2_ts_nosync = ts_nosync;

            if (err == 0) err = _stv0910.stv0910_read_ts_status_decoded(stv0910.STV0910_DEMOD_BOTTOM, ref ts_line_ok, ref ts_error, ref ts_nosync);
            nim_status.T2P1_ts_line_ok = ts_line_ok;
            nim_status.T2P1_ts_error = ts_error;
            nim_status.T2P1_ts_nosync = ts_nosync;

            if (nim_status.T1P2_demod_status != stv0910.DEMOD_S2)
            {
                /* short frames & pilots only valid for S2 DEMOD state */
                nim_status.T1P2_short_frame = false;
                nim_status.T1P2_pilots = false;
            }

            if (nim_status.T2P1_demod_status != stv0910.DEMOD_S2)
            {
                /* short frames & pilots only valid for S2 DEMOD state */
                nim_status.T2P1_short_frame = false;
                nim_status.T2P1_pilots = false;
            }

            // send status callback if available
            for ( int c = 0;c < status_callback.Count; c++)
            {
                status_callback[c](nim_status);
            }


            reset = false;

            return err;
        }

        public void worker_thread()
        {
            int hw_errors = 0;

            try
            {

                Log.Information("Nim Thread: Starting...");

                bool initialConfig = false;

                TunerConfig nim_config = null;

                byte err = _stv0910.stv0910_init(hardware.RequireSerialTS);

                if (err != 0)
                {
                    Log.Information("STV0910 Init Error: " + err.ToString());
                }

                Log.Information("Init Nim");
                err = _nim.nim_init();

                while (!_stopRequested)
                {
                    if (initialConfig == false)
                    {
                        Log.Information("Nim Thread: Initial Config");
                    }

                    if (config_queue.Count() > 0 || initialConfig == false)
                    {
                        while (config_queue.TryDequeue(out nim_config))
                        {
                            Thread.Sleep(10);

                            current_config[nim_config.tuner - 1] = nim_config;

                            lock (HwLock)
                            {
                                switch(nim_config.lnba_psu)
                                {
                                    case 0:
                                        hardware.hw_set_polarization_supply(0, false, false);
                                        break;
                                    case 1:
                                        hardware.hw_set_polarization_supply(0, true, false);
                                        break;
                                    case 2:
                                        hardware.hw_set_polarization_supply(0, true, true);
                                        break;
                                }

                                switch (nim_config.lnbb_psu)
                                {
                                    case 0:
                                        hardware.hw_set_polarization_supply(1, false, false);
                                        break;
                                    case 1:
                                        hardware.hw_set_polarization_supply(1, true, false);
                                        break;
                                    case 2:
                                        hardware.hw_set_polarization_supply(1, true, true);
                                        break;
                                }


                                // setup demod
                                if (err == 0)
                                {
                                    Log.Information("Configure Demod Receive - " + nim_config.tuner);
                                    if (nim_config.tuner == 1)
                                    {
                                        err = _stv0910.stv0910_setup_receive(stv0910.STV0910_DEMOD_TOP, nim_config.symbol_rate);
                                    }
                                    else
                                    {
                                        err = _stv0910.stv0910_setup_receive(stv0910.STV0910_DEMOD_BOTTOM, nim_config.symbol_rate);
                                    }
                                }
                                else
                                {
                                    Log.Information("Error before Demod");
                                }

                                // configure tuner
                                if (err == 0)
                                {
                                    Log.Information("Configure Tuner - " + nim_config.tuner.ToString());

                                    if (nim_config.tuner == 1)
                                    {
                                        err = _stv6120.stv6120_init(1, nim_config.frequency, nim_config.rf_input, nim_config.symbol_rate);
                                    }
                                    else
                                    {
                                        //err = _stv6120.stv6120_init(2, 749246, nim.NIM_INPUT_TOP, 333);
                                        err = _stv6120.stv6120_init(2, nim_config.frequency, nim_config.rf_input, nim_config.symbol_rate);
                                    }
                                }
                                else
                                {
                                    Log.Information("Error before Tuner");
                                }

                                // demod - start scan
                                if (err == 0)
                                {
                                    Log.Information("Demod Start Scan - " + nim_config.tuner.ToString() );

                                    if (nim_config.tuner == 1)
                                    {
                                        err = _stv0910.stv0910_start_scan(stv0910.STV0910_DEMOD_TOP);
                                    }
                                    else
                                    {
                                        err = _stv0910.stv0910_start_scan(stv0910.STV0910_DEMOD_BOTTOM);
                                    }
                                }
                                else
                                {
                                    Log.Information("Error before demod scan");
                                }


                                // 22 kHz - independent per tuner (22K-A/22K-B)
                                if (err == 0)
                                {
                                    err = _stv0910.stv0910_switch_22Khz(nim_config.tuner == 1 ? stv0910.STV0910_DEMOD_TOP : stv0910.STV0910_DEMOD_BOTTOM, nim_config.tone_22kHz_P1);
                                }
                            }

                            // done, if we have errors, then exit thread
                            if (err != 0)
                            {
                                Log.Information("****** Nim Thread: Hardware Error: " + err.ToString() + " ******");
                                hw_errors += 1;
                                if (hw_errors > 5)
                                {
                                    Log.Information("Too many hardware errors");
                                    return;
                                }
                            }
                            else
                            {
                                Log.Information("Nim Thread: Nim Init Good");
                            }

                            initialConfig = true;
                            reset = true;
                        }
                    }
                    else
                    {
                        // Timed diagnostic for the "Digole doesn't clear on exit" investigation:
                        // Join(5s) in MinitiounerSource.Close() has timed out without this loop
                        // ever reaching its "Loop exited" log line - i.e. it's stuck somewhere in
                        // here, not in the Digole shutdown write itself. Logging every call's
                        // elapsed time (not just slow ones) so the moment Stop() is requested is
                        // visible relative to which get_nim_status() call was in flight.
                        var status_sw = System.Diagnostics.Stopwatch.StartNew();
                        lock (HwLock)
                        {
                            if (trigger_burst_0)
                            {
                                trigger_burst_0 = false;
                                _stv0910.stv0910_send_tone_burst_p1();
                            }
                            if (trigger_burst_1)
                            {
                                trigger_burst_1 = false;
                                _stv0910.stv0910_send_tone_burst_p2();
                            }
                            if (test_gpio_pending)
                            {
                                test_gpio_pending = false;
                                hardware.hw_gpio_write_test(test_gpio_pin, test_gpio_value);
                            }
                            if (digole_final_test_pending)
                            {
                                digole_final_test_pending = false;
                                if (digole_enabled && digole != null)
                                {
                                    var test_sw = System.Diagnostics.Stopwatch.StartNew();
                                    byte test_err;
                                    if (!string.IsNullOrEmpty(digole_callsign))
                                    {
                                        Log.Information("Nim Thread: [Digole Final Test] Sending greeting...");
                                        test_err = digole.ShowGreeting(device_name, digole_callsign, "END");
                                    }
                                    else
                                    {
                                        Log.Information("Nim Thread: [Digole Final Test] Sending clear...");
                                        test_err = digole.Clear();
                                    }
                                    Log.Information("Nim Thread: [Digole Final Test] Write done, err=" + test_err + ", elapsed=" + test_sw.ElapsedMilliseconds + "ms");
                                    digole_hold_until = Environment.TickCount64 + 4000; // keep END screen visible ~4s
                                }
                                else
                                {
                                    Log.Information("Nim Thread: [Digole Final Test] Skipped, digole_enabled=" + digole_enabled + ", digole=" + (digole == null ? "null" : "set"));
                                }
                            }

                            get_nim_status();
                        }
                        status_sw.Stop();
                        Log.Debug("Nim Thread: get_nim_status() took " + status_sw.ElapsedMilliseconds + "ms, _stopRequested=" + _stopRequested);
                        Thread.Sleep(200);
                    }
                }

                // Leave the Digole on the callsign greeting (or a plain Clear if no callsign is
                // set) instead of a stale reading after OpenTuner closes. Done here, on the
                // worker thread's own way out, not from Stop() (called from the UI thread) -
                // guarded by HwLock like every other hardware access on this thread, so it can't
                // interleave with MinitiounerSource.Close()'s own LED/LNB shutoff writes.
                Log.Information("Nim Thread: Loop exited, digole_enabled=" + digole_enabled.ToString());
                if (digole_enabled && digole != null)
                {
                    var shutdown_sw = System.Diagnostics.Stopwatch.StartNew();
                    byte digole_err;
                    lock (HwLock)
                    {
                        if (!string.IsNullOrEmpty(digole_callsign))
                        {
                            Log.Information("Nim Thread: Sending Digole shutdown greeting...");
                            digole_err = digole.ShowGreeting(device_name, digole_callsign, "END");
                        }
                        else
                        {
                            Log.Information("Nim Thread: Sending Digole shutdown clear...");
                            digole_err = digole.Clear();
                        }
                    }
                    Log.Information("Nim Thread: Digole shutdown write sent, err=" + digole_err + ", elapsed=" + shutdown_sw.ElapsedMilliseconds + "ms");
                }
            }
            catch (ThreadAbortException)
            {
                Log.Information("Nim Thread: Closing");
            }
            catch (Exception ex)
            {
                // Without this, any exception here (e.g. from the Digole shutdown I2C write)
                // would silently kill the thread before the shutdown block below could run or
                // log anything - making the "Digole doesn't clear on exit" symptom unexplainable
                // from the logs alone.
                Log.Error(ex, "Nim Thread: Unhandled exception in worker_thread");
            }

        }

    }

    public class StatusEvent : EventArgs
    {
        public TunerStatus nim_status { get; set; }
    }
}
