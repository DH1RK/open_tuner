using opentuner.MediaSources.Minitiouner.HardwareInterfaces;
using opentuner.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using opentuner.MediaPlayers;
using Serilog;

namespace opentuner.MediaSources.Minitiouner
{
    public partial class MinitiounerSource : OTSource
    {
        public MTHardwareInterface hardware_interface;
        public bool hardware_connected = false;

        public int ts_devices = 1;

        // for the "Hardware Info" window
        private TunerStatus _last_status;
        private string _detected_ports = "";

        ConcurrentQueue<TunerConfig> config_queue = new ConcurrentQueue<TunerConfig>();

        public CircularBuffer ts_data_queue = new CircularBuffer(GlobalDefines.CircularBufferStartingCapacity);
        public CircularBuffer ts_data_queue2 = new CircularBuffer(GlobalDefines.CircularBufferStartingCapacity);

        // bytes waiting in the TS data queue of that tuner (Chip tab)
        public int TsBufferBytes(int device)
        {
            return (device == 0 ? ts_data_queue : ts_data_queue2).Count;
        }

        public TSThread ts_thread;
        public TSThread ts_thread2;

        NimThread nim_thread;
        TSParserThread ts_parser_thread;
        TSParserThread ts_parser_thread2;

        Thread ts_parser_t = null;
        Thread ts_parser_2_t = null;

        // threads
        Thread nim_thread_t = null;

        Thread ts_thread_t = null;
        Thread ts_thread_2_t = null;

        bool T1P2_prevLocked = false;
        bool T2P1_prevLocked = false;


        List<OTMediaPlayer> _media_player = new List<OTMediaPlayer> ();
        private List<TSRecorder> _ts_recorders;
        private List<TSUdpStreamer> _ts_streamers;
        private List<TunerControlForm> _tuner_forms;

        string _mediapath = "";

        public override bool DeviceConnected
        {
            get { return hardware_connected; }
        }

        // EXTERN-0..7 (AUX chip GPIO, see MinitiounerProperties.cs "Switches" panel) - kept as
        // one packed byte, mirrored to _settings.ExternState for persistence across connects.
        public bool AuxAvailable => hardware_connected && hardware_interface != null && hardware_interface.AuxAvailable;

        public bool GetExternOutput(int index)
        {
            return _settings.ExternState[index];
        }

        public void SetExternOutput(int index, bool on)
        {
            _settings.ExternState[index] = on;

            if (!AuxAvailable)
                return;

            byte value = 0;
            for (int i = 0; i < _settings.ExternState.Length; i++)
            {
                if (_settings.ExternState[i])
                    value |= (byte)(1 << i);
            }

            hardware_interface.aux_gpio_write(value);
        }

        // One-shot 22kHz DiSEqC tone burst ("TS" mode) - see NimThread.TriggerToneBurst.
        public void SendToneBurst(int tuner_index)
        {
            nim_thread?.TriggerToneBurst(tuner_index);
        }

        // "Stop TS" (issue #9) - stops the demodulator; the existing "not locked" handling in
        // nim_status_feedback/UpdateTunerProperties then stops the player, TS recording and UDP
        // streaming on the next status update, same as when a signal is lost.
        public override void StopTuner(int tuner_index)
        {
            nim_thread?.StopTuner(tuner_index);

            // the button of the tuner lights up, also for the hotkey S and the menu of the video
            if (tuner_index == 0)
                _tuner1_properties?.FlashStopButton("media_controls_1");
            else if (tuner_index == 1)
                _tuner2_properties?.FlashStopButton("media_controls_2");
        }

        public override bool CanStopTuner(int tuner_index)
        {
            return nim_thread != null && tuner_index >= 0 && tuner_index < GetVideoSourceCount();
        }

        // The Switches state the user picked last (LNB-A/LNB-B supply, 22kHz tone per tuner) is
        // kept for the next connect: Initialize() restores it before the Switches dropdowns are
        // built, so the setup survives restarts without a trip through the settings dialog.
        // Frequency tab: new capture range / correction of one tuner. Remembered in the settings and applied by
        // tuning again to the current frequency and symbol rate.
        private void ApplyTunerTrim(int device, uint capture_range, double correction_ppm)
        {
            if (device < 0 || device > 1)
                return;

            capture_range_khz[device] = capture_range;
            freq_correction_ppm[device] = correction_ppm;

            if (_settings.CaptureRangeKHz == null || _settings.CaptureRangeKHz.Length < 2)
                _settings.CaptureRangeKHz = new uint[2];
            if (_settings.FreqCorrectionPpm == null || _settings.FreqCorrectionPpm.Length < 2)
                _settings.FreqCorrectionPpm = new double[2];

            _settings.CaptureRangeKHz[device] = capture_range;
            _settings.FreqCorrectionPpm[device] = correction_ppm;
            SaveSettingsFiles();

            if (device == 0)
                change_frequency(0, current_frequency_0, current_sr_0, current_rf_input_0, current_tone_22kHz_0, current_lnba_psu, current_lnbb_psu);
            else
                change_frequency(1, current_frequency_1, current_sr_1, current_rf_input_1, current_tone_22kHz_1, current_lnba_psu, current_lnbb_psu);
        }

        // Special tab: new DVB mode (0 = automatic, 1 = DVB-S only, 2 = DVB-S2 only) of one tuner, remembered in the settings and applied by
        // tuning again to the current frequency and symbol rate.
        private void ApplyDvbMode(int device, byte mode)
        {
            if (device < 0 || device > 1)
                return;

            dvb_mode[device] = mode;

            if (_settings.DvbMode == null || _settings.DvbMode.Length < 2)
                _settings.DvbMode = new byte[2];

            _settings.DvbMode[device] = mode;
            SaveSettingsFiles();

            if (device == 0)
                change_frequency(0, current_frequency_0, current_sr_0, current_rf_input_0, current_tone_22kHz_0, current_lnba_psu, current_lnbb_psu);
            else
                change_frequency(1, current_frequency_1, current_sr_1, current_rf_input_1, current_tone_22kHz_1, current_lnba_psu, current_lnbb_psu);
        }

        // ---- Memories M1 .. M4 (Special tab) ------------------------------------------------------------------------------
        private const int MemorySlots = 7;   // M1 .. M7, as many as FrequencyTunerView.MemoryButtons

        private MemorySlot MemoryAt(int slot)
        {
            if (_settings.Memory == null || slot < 0 || slot >= _settings.Memory.Length || slot >= MemorySlots)
                return null;

            MemorySlot memory = _settings.Memory[slot];
            return memory != null && memory.Used ? memory : null;
        }

        // Stores what the tuner is set to now: frequency, offset, symbol rate and RF input.
        private void StoreMemory(int device, int slot)
        {
            if (device < 0 || device > 1 || slot < 0 || slot >= MemorySlots)
                return;

            if (_settings.Memory == null || _settings.Memory.Length < MemorySlots)
            {
                var memory = new MemorySlot[MemorySlots];
                if (_settings.Memory != null)
                    Array.Copy(_settings.Memory, memory, _settings.Memory.Length);
                _settings.Memory = memory;
            }

            _settings.Memory[slot] = new MemorySlot
            {
                Used = true,
                Frequency = device == 0 ? current_frequency_0 : current_frequency_1,
                Offset = device == 0 ? current_offset_0 : current_offset_1,
                SymbolRate = device == 0 ? current_sr_0 : current_sr_1,
                RFInput = device == 0 ? current_rf_input_0 : current_rf_input_1
            };

            Log.Information("Memory M" + (slot + 1) + " stored from tuner " + (device + 1) + ": " + MemoryText(slot));
            SaveSettingsFiles();
            UpdateMemoryViews();
        }

        // Tunes the tuner to a memory: offset, RF input, frequency and symbol rate.
        private void RecallMemory(int device, int slot)
        {
            MemorySlot memory = MemoryAt(slot);
            if (device < 0 || device > 1 || memory == null)
                return;

            Log.Information("Memory M" + (slot + 1) + " recalled to tuner " + (device + 1) + ": " + MemoryText(slot));
            ChangeOffset((byte)device, (int)memory.Offset);

            if (device == 0)
                current_rf_input_0 = memory.RFInput;
            else
                current_rf_input_1 = memory.RFInput;

            SetFrequency(device, memory.Frequency, memory.SymbolRate, false);
            NotifyTunedByUser(device);
        }

        // ---- FEC mode and Wide scan (Special tab) -------------------------------------------------------------------------
        // Wide scan works (symbol rate window, see stv0910_setup_sfr_window and docs\MiniTioune_I2C_Analyse_WideScan.md).
        // FEC mode: with Manual only the MODCOD of the Manual FEC window is left in the MODCOD list of the demodulator (see ManualFec), Auto searches all.
        // Applied by tuning again to the current frequency and symbol rate.
        private void SetFecManual(int device, bool manual)
        {
            if (device < 0 || device > 1)
                return;

            if (_settings.FecManual == null || _settings.FecManual.Length < 2)
                _settings.FecManual = new bool[2];

            _settings.FecManual[device] = manual;
            Log.Information("FEC mode " + TunerLabel(device) + ": " + (manual ? "manual (" + GetManualFec(device) + ")" : "auto"));

            if (!manual)
                CloseManualFecWindow(device);
            SaveSettingsFiles();
            RetuneCurrent(device);
        }

        // The manual choice of a tuner (MODCOD list in the chip: ManualFec.ToList).
        private ManualFec GetManualFec(int device)
        {
            return ManualFec.From(Pick(_settings.FecModulation, device, 0), Pick(_settings.FecRate, device, 7),
                                  Pick(_settings.FecShort, device, false), Pick(_settings.FecPilots, device, false));
        }

        private static T Pick<T>(T[] values, int index, T fallback)
        {
            return values != null && values.Length > index ? values[index] : fallback;
        }

        // null = automatic
        private byte[] ModcodList(int device)
        {
            return Pick(_settings.FecManual, device, false) ? GetManualFec(device).ToList() : null;
        }

        // The Manual FEC window of a tuner: it stays open until Close is pressed or the FEC mode goes back to Auto; OK stores and applies the choice.
        private readonly ManualFecForm[] _manual_fec_forms = new ManualFecForm[2];

        private void ChooseManualFec(int device, IWin32Window owner)
        {
            if (device < 0 || device > 1)
                return;

            // the button opens the window and closes it again
            ManualFecForm open = _manual_fec_forms[device];
            if (open != null && !open.IsDisposed)
            {
                open.Close();
                return;
            }

            var form = new ManualFecForm(GetManualFec(device), fec => ApplyManualFec(device, fec));
            form.Text = "Manual FEC - " + TunerLabel(device);
            RestoreWindowPosition(form, Pick(_settings.FecWindowX, device, int.MinValue), Pick(_settings.FecWindowY, device, int.MinValue));

            form.FormClosed += (s, e) =>
            {
                if (_manual_fec_forms[device] == form)
                    _manual_fec_forms[device] = null;

                StoreWindowPosition(ref _settings.FecWindowX, ref _settings.FecWindowY, device, form.Location);
            };
            _manual_fec_forms[device] = form;
            form.Show(owner);
        }

        // The position of a window of the Special tab the last time, if it is still on a screen (int.MinValue = never stored: centred over the main window).
        private static void RestoreWindowPosition(Form form, int x, int y)
        {
            if (x == int.MinValue || y == int.MinValue)
                return;

            var bounds = new Rectangle(x, y, form.Width, form.Height);
            if (Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds)))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(x, y);
            }
        }

        // Remembers the position of a window when it is closed (the form still has its last location).
        private void StoreWindowPosition(ref int[] xs, ref int[] ys, int device, Point location)
        {
            if (xs == null || xs.Length < 2) xs = new int[] { int.MinValue, int.MinValue };
            if (ys == null || ys.Length < 2) ys = new int[] { int.MinValue, int.MinValue };
            xs[device] = location.X;
            ys[device] = location.Y;
            SaveSettingsFiles();
        }

        // ---- Error counter (Chip tab) ---------------------------------------------------------------------------------------
        // Error counter 1 of the demodulators: the event type, the counting mode and TS error go to the NIM thread, which writes ERRCTRL1 / TSCFGH when they
        // differ and reads the counter (no retune). store = true: the Chip tab changed them, false: the stored choice at the start.
        private void ApplyErrorEvent(bool store)
        {
            int source = Math.Max(0, Math.Min(ErrorEvent.Sources.Length - 1, _settings.ChipErrSource));
            int mode = Math.Max(0, Math.Min(ErrorEvent.Modes.Length - 1, _settings.ChipErrMode));

            if (store)
            {
                Log.Information("Error counter: " + ErrorEvent.Sources[source].Name + ", " + ErrorEvent.Modes[mode] + ", TS error " + (_settings.ChipTsError ? "on" : "off"));
                SaveSettingsFiles();
            }

            for (int t = 0; t < 2; t++)
                nim_thread?.SetEventCounter(t, source, mode, _settings.ChipTsError);
        }

        private void CloseManualFecWindow(int device)
        {
            ManualFecForm form = _manual_fec_forms[device];
            if (form == null || form.IsDisposed)
                return;

            if (form.InvokeRequired)
                form.BeginInvoke(new Action(form.Close));
            else
                form.Close();
        }

        // A change in the Manual FEC window: the choice is stored and applied by tuning again.
        private void ApplyManualFec(int device, ManualFec fec)
        {
            {
                if (_settings.FecModulation == null || _settings.FecModulation.Length < 2) _settings.FecModulation = new int[2];
                if (_settings.FecRate == null || _settings.FecRate.Length < 2) _settings.FecRate = new int[] { 7, 7 };
                if (_settings.FecShort == null || _settings.FecShort.Length < 2) _settings.FecShort = new bool[2];
                if (_settings.FecPilots == null || _settings.FecPilots.Length < 2) _settings.FecPilots = new bool[2];

                _settings.FecModulation[device] = fec.Modulation;
                _settings.FecRate[device] = fec.Rate;
                _settings.FecShort[device] = fec.Short;
                _settings.FecPilots[device] = fec.Pilots;
                Log.Information("Manual FEC " + TunerLabel(device) + ": " + fec);
                SaveSettingsFiles();
                RetuneCurrent(device);
                ResetVideo(device);
            }
        }

        // Tunes the tuner again to its current frequency and symbol rate (the chip settings are written with every tune).
        private void RetuneCurrent(int device)
        {
            if (device == 0)
                change_frequency(0, current_frequency_0, current_sr_0, current_rf_input_0, current_tone_22kHz_0, current_lnba_psu, current_lnbb_psu);
            else
                change_frequency(1, current_frequency_1, current_sr_1, current_rf_input_1, current_tone_22kHz_1, current_lnba_psu, current_lnbb_psu);
        }

        private void SetWideScan(int device, bool on)
        {
            if (device < 0 || device > 1)
                return;

            if (_settings.WideScan == null || _settings.WideScan.Length < 2)
                _settings.WideScan = new bool[2];

            wide_scan[device] = on;
            _settings.WideScan[device] = on;
            Log.Information("Wide scan tuner " + (device + 1) + ": " + (on ? "on" : "off"));
            SaveSettingsFiles();

            // applied by tuning again to the current frequency and symbol rate
            RetuneCurrent(device);
        }

        // The stored FEC mode and Wide scan in the Special tab.
        private void ShowFecAndWideScan()
        {
            for (int device = 0; device < 2; device++)
            {
                bool fec = _settings.FecManual != null && _settings.FecManual.Length > device && _settings.FecManual[device];
                bool wide = _settings.WideScan != null && _settings.WideScan.Length > device && _settings.WideScan[device];
                (device == 0 ? _frequency_1 : _frequency_2)?.SetFecAndWideScan(fec, wide);
            }
        }

        // ---- PID choice (Special tab) -------------------------------------------------------------------------------------
        // Passes the choice of one tuner to the PID filter in front of its media player.
        private void ApplyPidFilter(int device)
        {
            TsPidFilter filter = (device == 0 ? ts_thread : ts_thread2)?.PlayerFilter;
            if (filter == null || device < 0 || device > 1)
                return;

            filter.Learn = true;   // PAT and PMTs are read in the automatic mode too

            bool auto = _settings.PidAuto == null || _settings.PidAuto.Length <= device || _settings.PidAuto[device];
            if (auto)
            {
                filter.SetAutomatic();
            }
            else
            {
                int video = _settings.VideoPid != null && _settings.VideoPid.Length > device ? _settings.VideoPid[device] : 0;
                int audio = _settings.AudioPid != null && _settings.AudioPid.Length > device ? _settings.AudioPid[device] : 0;
                filter.SetManual(video, audio);
            }
        }

        // The video and audio PIDs the analysis found in the stream of a tuner (set by UpdateFoundPids).
        private volatile HashSet<int>[] _found_pid_sets = new HashSet<int>[] { new HashSet<int>(), new HashSet<int>() };

        // The video and audio PIDs that reach the media player with a PID choice: all the stream has in the automatic mode, the two typed in otherwise.
        private HashSet<int> PidsReachingPlayer(int device, bool auto, int video_pid, int audio_pid)
        {
            if (auto)
                return new HashSet<int>(_found_pid_sets[device]);

            var pids = new HashSet<int>();
            if (video_pid > 0) pids.Add(video_pid);
            if (audio_pid > 0) pids.Add(audio_pid);
            return pids;
        }

        // A new PID choice of one tuner: remembered in the settings and applied to the filter. Returns true if what reaches the media player
        // changes by it, so that the video has to be started again (switching Auto off with the PIDs of the stream changes nothing).
        private bool SetPidChoice(int device, bool auto, int video_pid, int audio_pid)
        {
            if (device < 0 || device > 1)
                return false;

            bool old_auto = _settings.PidAuto == null || _settings.PidAuto.Length <= device || _settings.PidAuto[device];
            int old_video = _settings.VideoPid != null && _settings.VideoPid.Length > device ? _settings.VideoPid[device] : 0;
            int old_audio = _settings.AudioPid != null && _settings.AudioPid.Length > device ? _settings.AudioPid[device] : 0;
            bool changes_stream = !PidsReachingPlayer(device, old_auto, old_video, old_audio).SetEquals(PidsReachingPlayer(device, auto, video_pid, audio_pid));

            if (_settings.PidAuto == null || _settings.PidAuto.Length < 2)
                _settings.PidAuto = new bool[] { true, true };
            if (_settings.VideoPid == null || _settings.VideoPid.Length < 2)
                _settings.VideoPid = new int[2];
            if (_settings.AudioPid == null || _settings.AudioPid.Length < 2)
                _settings.AudioPid = new int[2];

            _settings.PidAuto[device] = auto;
            _settings.VideoPid[device] = video_pid;
            _settings.AudioPid[device] = audio_pid;
            Log.Information("PID choice tuner " + (device + 1) + ": " + (auto ? "automatic" : "video " + video_pid + ", audio " + audio_pid) +
                            (changes_stream ? "" : " (the stream stays as it is)"));
            SaveSettingsFiles();
            ApplyPidFilter(device);
            return changes_stream;
        }

        // ---- PID memories P1 .. P7 (Special tab) --------------------------------------------------------------------------
        private const int PidMemorySlots = 7;   // as many as FrequencyTunerView.MemoryButtons

        private PidSlot PidMemoryAt(int slot)
        {
            if (_settings.PidMemory == null || slot < 0 || slot >= _settings.PidMemory.Length || slot >= PidMemorySlots)
                return null;

            PidSlot memory = _settings.PidMemory[slot];
            return memory != null && memory.Used ? memory : null;
        }

        private string PidMemoryText(int slot)
        {
            PidSlot memory = PidMemoryAt(slot);
            return memory == null ? "empty" : "video PID " + memory.VideoPid + ", audio PID " + memory.AudioPid;
        }

        // Stores the PIDs shown in the fields.
        private void StorePidMemory(int slot, int video_pid, int audio_pid)
        {
            if (slot < 0 || slot >= PidMemorySlots)
                return;

            if (_settings.PidMemory == null || _settings.PidMemory.Length < PidMemorySlots)
            {
                var memory = new PidSlot[PidMemorySlots];
                if (_settings.PidMemory != null)
                    Array.Copy(_settings.PidMemory, memory, _settings.PidMemory.Length);
                _settings.PidMemory = memory;
            }

            _settings.PidMemory[slot] = new PidSlot { Used = true, VideoPid = video_pid, AudioPid = audio_pid };
            Log.Information("PID memory P" + (slot + 1) + " stored: " + PidMemoryText(slot));
            SaveSettingsFiles();
            UpdatePidMemoryViews();
        }

        // Switches the tuner to the manual PID choice with the PIDs of the memory. false if the memory is empty or what reaches the player does not change.
        private bool RecallPidMemory(int device, int slot)
        {
            PidSlot memory = PidMemoryAt(slot);
            if (device < 0 || device > 1 || memory == null)
                return false;

            Log.Information("PID memory P" + (slot + 1) + " recalled to tuner " + (device + 1) + ": " + PidMemoryText(slot));
            bool changes_stream = SetPidChoice(device, false, memory.VideoPid, memory.AudioPid);
            ShowPidChoices();
            return changes_stream;
        }

        private void UpdatePidMemoryViews()
        {
            for (int slot = 0; slot < PidMemorySlots; slot++)
            {
                _frequency_1?.SetPidMemory(slot, PidMemoryAt(slot) != null, PidMemoryText(slot));
                _frequency_2?.SetPidMemory(slot, PidMemoryAt(slot) != null, PidMemoryText(slot));
            }
        }

        // The saved PID choice in the PID rows of the Special tab.
        private void ShowPidChoices()
        {
            for (int device = 0; device < 2; device++)
            {
                bool auto = _settings.PidAuto == null || _settings.PidAuto.Length <= device || _settings.PidAuto[device];
                int video = _settings.VideoPid != null && _settings.VideoPid.Length > device ? _settings.VideoPid[device] : 0;
                int audio = _settings.AudioPid != null && _settings.AudioPid.Length > device ? _settings.AudioPid[device] : 0;
                (device == 0 ? _frequency_1 : _frequency_2)?.SetPidChoice(auto, video, audio);
            }
        }

        private readonly long[] _pid_scan_ticks = new long[2];

        // The video and audio PIDs the analysis found in the stream of a tuner, for the PID row of the Special tab (about once a second).
        private void UpdateFoundPids(int device)
        {
            long now = Environment.TickCount64;
            if (now - _pid_scan_ticks[device] < 1000)
                return;

            _pid_scan_ticks[device] = now;

            TSParserThread parser = device == 0 ? ts_parser_thread : ts_parser_thread2;
            if (parser == null)
                return;

            var video = new List<int>();
            var audio = new List<int>();
            TSAnalysis analysis = parser.Analyzer.Get();

            foreach (TSPidInfo pid in analysis.Pids ?? new TSPidInfo[0])
            {
                if (pid.Category == 1)
                    video.Add(pid.Pid);
                else if (pid.Category == 2)
                    audio.Add(pid.Pid);
            }

            _found_pid_sets[device] = new HashSet<int>(video.Concat(audio));
            (device == 0 ? _frequency_1 : _frequency_2)?.SetFoundPids(video, audio);
        }

        // Tells the BATC spectrum that this tuner was set by hand, so that it marks it at once (inside its own range only).
        private void NotifyTunedByUser(int device)
        {
            if (device == 0)
                RaiseTunedByUser(TunerNumberOffset, current_frequency_0 + current_offset_0, current_sr_0);
            else if (device == 1)
                RaiseTunedByUser(TunerNumberOffset + 1, current_frequency_1 + current_offset_1, current_sr_1);
        }

        private string MemoryText(int slot)
        {
            MemorySlot memory = MemoryAt(slot);
            if (memory == null)
                return "empty";

            return ((memory.Frequency + memory.Offset) / 1000.0).ToString("0.000") + " MHz (LO " + memory.Frequency + " kHz), " + memory.SymbolRate + " kS, input " + (memory.RFInput == nim.NIM_INPUT_A ? "A" : "B");
        }

        // The buttons of both tuner views show which memories are in use and what they hold.
        private void UpdateMemoryViews()
        {
            for (int slot = 0; slot < MemorySlots; slot++)
            {
                _frequency_1?.SetMemory(slot, MemoryAt(slot) != null, MemoryText(slot));
                _frequency_2?.SetMemory(slot, MemoryAt(slot) != null, MemoryText(slot));
            }
        }

        private void RememberSwitchState()
        {
            if (_settings.Tone22kHz == null || _settings.Tone22kHz.Length < 2)
                _settings.Tone22kHz = new bool[2];

            if (_settings.DefaultLnbASupply == current_lnba_psu && _settings.DefaultLnbBSupply == current_lnbb_psu &&
                _settings.Tone22kHz[0] == current_tone_22kHz_0 && _settings.Tone22kHz[1] == current_tone_22kHz_1)
                return;

            _settings.DefaultLnbASupply = current_lnba_psu;
            _settings.DefaultLnbBSupply = current_lnbb_psu;
            _settings.Tone22kHz[0] = current_tone_22kHz_0;
            _settings.Tone22kHz[1] = current_tone_22kHz_1;
            SaveSettingsFiles();
        }

        // tuner specific

        // OTSource Properties
        private uint current_rf_input_0 = nim.NIM_INPUT_A;
        private uint current_rf_input_1 = nim.NIM_INPUT_A;
        private uint current_frequency_0 = 0;
        private uint current_frequency_1 = 0;
        private uint current_sr_0 = 0;
        private uint current_sr_1 = 0;

        // independent 22kHz tone per tuner (22K-A / 22K-B) - see stv0910_switch_22Khz
        private bool current_tone_22kHz_0 = false;
        private bool current_tone_22kHz_1 = false;
        // tuning trim per tuner, see MinitiounerSettings.CaptureRangeKHz / FreqCorrectionKHz
        private uint[] capture_range_khz = new uint[2];
        private byte[] dvb_mode = new byte[2];               // per tuner: 0 = automatic, 1 = DVB-S only, 2 = DVB-S2 only (MinitiounerSettings.DvbMode)
        private bool[] wide_scan = new bool[2];              // per tuner: symbol rate window 0.5 x to 1.5 x instead of +-5 % (MinitiounerSettings.WideScan)
        private double[] freq_correction_ppm = new double[2];

        // The correction is a reference (crystal) error, so it scales with the tuner frequency: kHz = ppm * IF / 1e6.
        private int CorrectionKHz(int device, uint if_khz)
        {
            return (int)Math.Round(freq_correction_ppm[device] * if_khz / 1e6);
        }

        private uint current_offset_0 = 0;
        private uint current_offset_1 = 0;

        private byte current_lnba_psu = 0;
        private byte current_lnbb_psu = 0;

        private string last_service_name_0 = "";
        private string last_service_provider_0 = "";
        private string last_dbm_0 = "";
        private string last_mer_0 = "";
        private string last_video_codec_0 = "";

        private string last_service_name_1 = "";
        private string last_service_provider_1 = "";
        private string last_dbm_1 = "";
        private string last_mer_1 = "";
        private string last_video_codec_1 = "";


        private VideoChangeCallback VideoChangeCB;

        // todo: this is the callback we want to remove and keep internal
        private SourceStatusCallback SourceStatusCB;

        public string HardwareDevice { get; set; }

        private SettingsManager<MinitiounerSettings> _settingsManager;

        // Settings per board (#29): every board keeps its values in minitiouner_settings_<chip serial>.json. The general
        // file minitiouner_settings.json stays (interface choice, PreferredBoardSerial, and the values of the board used
        // last - a board that has no file yet starts from them).
        private SettingsManager<MinitiounerSettings> _board_settings_manager;

        // read once at startup from the general file and not taken from a board file
        private static readonly string[] GeneralSettingFields = { "DefaultInterface", "PreferredBoardSerial" };

        private void LoadBoardSettings(string chip_serial)
        {
            if (string.IsNullOrWhiteSpace(chip_serial))
                return;

            string safe_serial = new string(chip_serial.Where(c => char.IsLetterOrDigit(c)).ToArray());
            if (safe_serial.Length == 0)
                return;

            string name = "minitiouner_settings_" + safe_serial;
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings", name + ".json");
            bool existed = System.IO.File.Exists(path);

            var manager = new SettingsManager<MinitiounerSettings>(name);
            try
            {
                if (existed)
                {
                    var board_settings = manager.LoadSettings(new MinitiounerSettings());
                    foreach (var field in typeof(MinitiounerSettings).GetFields())
                    {
                        if (Array.IndexOf(GeneralSettingFields, field.Name) >= 0)
                            continue;
                        field.SetValue(_settings, field.GetValue(board_settings));
                    }
                    Log.Information("Board settings: loaded " + name + ".json");
                }
                else
                {
                    // first use of this board: it takes over the values used so far
                    manager.SaveSettings(_settings);
                    Log.Information("Board settings: no file for board " + safe_serial + " yet, created " + name + ".json from the current settings");
                }
                _board_settings_manager = manager;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Board settings: " + name + ".json could not be used, keeping the general settings");
            }
        }

        // the general file and the file of the board in use
        private void SaveSettingsFiles()
        {
            _settingsManager.SaveSettings(_settings);
            _board_settings_manager?.SaveSettings(_settings);
        }
        private MinitiounerSettings _settings;

        public MinitiounerSource() 
        {
            // settings
            _settings = new MinitiounerSettings();
            _settingsManager = new SettingsManager<MinitiounerSettings>(opentuner.MediaSources.Minitiouner.BoardTable.SettingsName("minitiouner_settings"));
            _settings = (_settingsManager.LoadSettings(_settings));
        }

        public override int GetVideoSourceCount()
        {
            return ts_devices;
        }

        public override string GetDeviceName()
        {
            return HardwareDevice;
        }

        public override void RegisterTSConsumer(int device, CircularBuffer ts_buffer_queue)
        {
            switch (device)
            {
                case 0: ts_thread.RegisterTSConsumer(ts_buffer_queue); break;
                case 1: ts_thread2.RegisterTSConsumer(ts_buffer_queue); break;
            }
        }


        public override void StartStreaming(int device)
        {
            switch(device)
            {
                case 0:
                    if (ts_thread != null)
                        ts_thread.start_ts();
                    break;
                case 1:
                    if (ts_thread2 != null)
                        ts_thread2.start_ts();
                    break;
            }
        }

        public override void StopStreaming(int device)
        {
            switch (device)
            {
                case 0:
                    if (ts_thread != null)
                        ts_thread.stop_ts();
                    break;
                case 1:
                    if (ts_thread2 != null)
                        ts_thread2.stop_ts();
                    break;
            }
        }


        public override uint GetSymbolRate(int device)
        {
            return device == 0 ? current_sr_0 : (device == 1 ? current_sr_1 : 0);
        }

        public override string GetSpecialTabTitle(int device)
        {
            return _frequency_panel != null ? "Special" : null;
        }

        public override string GetPropertiesTabTitle(int device)
        {
            return "Properties";
        }

        // device 0 = tuner 1, 1 = tuner 2; the groups of the other tuner get the normal border
        public override void SetTunerHighlight(int device, System.Drawing.Color color)
        {
            System.Drawing.Color first = device == 0 ? color : System.Drawing.Color.Empty;
            System.Drawing.Color second = device == 1 ? color : System.Drawing.Color.Empty;

            _tuner1_properties?.SetHighlight(first);
            _expert_1?.SetHighlight(first);
            _tsinfo_1?.SetHighlight(first);
            _frequency_1?.SetHighlight(first);

            _tuner2_properties?.SetHighlight(second);
            _expert_2?.SetHighlight(second);
            _tsinfo_2?.SetHighlight(second);
            _frequency_2?.SetHighlight(second);
        }

        public override long GetFrequency(int device, bool offset_included)
        {
            long frequency = 0;

            switch (device)
            {
                case 0:  
                    int offset0 = offset_included ? (int)current_offset_0 : 0;
                    frequency = current_frequency_0 + offset0; 
                    break;
                case 1:  
                    int offset1 = offset_included ? (int)current_offset_1 : 0;
                    frequency = current_frequency_1 + offset1; 
                    break;
            }

            return frequency;
        }

        // device : 0 = Tuner 1, 1 = Tuner 2
        public override void SetFrequency(int device, uint frequency, uint symbol_rate, bool offset_included)
        {
            uint freq = 0;

            if (device >= 0 && device < 2)
                fallback_step[device] = -1; // any other tune (dialog, presets, fixed rate) cancels a running fallback

            if (device == 0 )
                freq = frequency - (offset_included ? current_offset_0 : 0);
            else
                freq = frequency - (offset_included ? current_offset_1 : 0);

            change_frequency((byte)(device), freq, symbol_rate,  (device == 0 ? current_rf_input_0 : current_rf_input_1 ), (device == 0 ? current_tone_22kHz_0 : current_tone_22kHz_1), current_lnba_psu, current_lnbb_psu);

            // last tuner the user actually selected (BATC spectrum click, tuner dialog, preset)
            // wins the Digole display when both tuners are locked at once.
            nim_thread?.SetPreferredTuner(device);
        }


        // ---- Automatic symbol rate fallback for a click in the BATC spectrum -----------------------------------
        // The spectrum only estimates the symbol rate from the width of the signal (a signal at the top of the
        // FFT range is measured too wide, for example). If the demodulator does not lock within FallbackWaitMs(),
        // the next smaller standard rate is tried (66 -> 33 -> 25 -> 20 kS), and if none of them locks the
        // original rate is set again. A rate typed in by hand or chosen in the spectrum's rate field never
        // falls back (SetFrequency() disarms it).
        private static readonly uint[] FallbackRates = { 66, 33, 25, 20 };
        // The lower the rate the longer the demodulator needs to find carrier and timing (MiniTioune took well over 20 s
        // for 25 kS): 1500 / rate seconds, at least 10 s - 66 kS 23 s, 33 kS 45 s, 25 kS 60 s, 20 kS 75 s.
        private static int FallbackWaitMs(uint rate_kS)
        {
            return (int)Math.Max(10000, 1500000 / Math.Max(1u, rate_kS));
        }

        private readonly int[] fallback_step = { -1, -1 };          // index into FallbackRates being tried, -1 = off
        private readonly uint[] fallback_original_sr = new uint[2];
        private readonly long[] fallback_deadline = new long[2];    // Environment.TickCount64

        public override void SetFrequencyFromSpectrum(int device, uint frequency, uint symbol_rate)
        {
            SetFrequency(device, frequency, symbol_rate, true);

            int index = Array.IndexOf(FallbackRates, symbol_rate);
            if (_settings.AutoSrFallback && device >= 0 && device < 2 && index >= 0)
            {
                fallback_step[device] = index;
                fallback_original_sr[device] = symbol_rate;
                fallback_deadline[device] = Environment.TickCount64 + FallbackWaitMs(symbol_rate);
                Log.Information("SR fallback armed: tuner " + (device + 1) + ", " + symbol_rate + " kS");
            }
        }

        // Called with every status update (NimThread): locked -> done, no lock after the wait -> next rate.
        private void CheckSrFallback(TunerStatus status)
        {
            for (int device = 0; device < 2; device++)
            {
                if (fallback_step[device] < 0)
                    continue;

                byte demod_status = device == 0 ? status.T1P2_demod_status : status.T2P1_demod_status;
                if (demod_status == stv0910.DEMOD_S || demod_status == stv0910.DEMOD_S2)
                {
                    Log.Information("SR fallback: tuner " + (device + 1) + " locked at " + (device == 0 ? current_sr_0 : current_sr_1) + " kS");
                    fallback_step[device] = -1;
                    continue;
                }

                if (Environment.TickCount64 < fallback_deadline[device])
                    continue;

                uint sr;
                int next = fallback_step[device] + 1;
                if (next < FallbackRates.Length)
                {
                    sr = FallbackRates[next];
                    fallback_step[device] = next;
                    fallback_deadline[device] = Environment.TickCount64 + FallbackWaitMs(sr);
                    Log.Information("SR fallback: tuner " + (device + 1) + " no lock, trying " + sr + " kS");
                }
                else
                {
                    sr = fallback_original_sr[device];
                    fallback_step[device] = -1;
                    Log.Information("SR fallback: tuner " + (device + 1) + " no lock with any rate, back to " + sr + " kS");
                }

                RetuneWithSymbolRate(device, sr);
            }
        }

        // ---- SR Wide scan: take over the symbol rate the demodulator locked at ------------------------------------------------
        // With SR Wide scan the demodulator also locks signals far from the set symbol rate (250 set, 333 sent). The set rate (buttons, entry, expected
        // bitrate, station name of the recording) then no longer fits the signal. When the lock is steady and the measured rate is clearly different,
        // the set rate is changed to it (rounded to the standard rate if one is close). The demodulator stays as it is (locked): no new tune, no video reset.
        private static readonly uint[] FollowRates = { 20, 25, 33, 66, 125, 250, 333, 500, 1000, 1500, 2000 };
        private const double FollowMinDeviation = 0.02;     // measured rate differs from the set one by more than 2 %
        private const double FollowSnapTolerance = 0.03;    // a standard rate this close is used instead of the measured value
        private const int FollowSteadyMs = 3000;            // the same rate measured for this long
        private const int FollowPauseMs = 5000;             // after a take-over: no further one for this long
        private readonly uint[] follow_candidate = new uint[2];
        private readonly long[] follow_since = new long[2];
        private readonly long[] follow_last = new long[2];

        private void CheckSrFollow(TunerStatus status)
        {
            for (int device = 0; device < 2; device++)
            {
                byte demod_status = device == 0 ? status.T1P2_demod_status : status.T2P1_demod_status;
                uint measured_hz = device == 0 ? status.T1P2_symbol_rate : status.T2P1_symbol_rate;
                uint current = device == 0 ? current_sr_0 : current_sr_1;
                bool locked = demod_status == stv0910.DEMOD_S || demod_status == stv0910.DEMOD_S2;

                if (!wide_scan[device] || !locked || measured_hz == 0 || current == 0)
                {
                    follow_candidate[device] = 0;
                    continue;
                }

                double measured_ks = measured_hz / 1000.0;
                if (Math.Abs(measured_ks / current - 1.0) <= FollowMinDeviation)
                {
                    follow_candidate[device] = 0;
                    continue;
                }

                uint wanted = (uint)Math.Round(measured_ks);
                foreach (uint rate in FollowRates)
                {
                    if (Math.Abs(measured_ks / rate - 1.0) <= FollowSnapTolerance)
                        wanted = rate;
                }

                long now = Environment.TickCount64;
                if (wanted == current || wanted == 0)
                {
                    follow_candidate[device] = 0;
                    continue;
                }

                if (follow_candidate[device] != wanted)
                {
                    follow_candidate[device] = wanted;
                    follow_since[device] = now;
                    continue;
                }

                if (now - follow_since[device] < FollowSteadyMs || now - follow_last[device] < FollowPauseMs)
                    continue;

                follow_last[device] = now;
                follow_candidate[device] = 0;
                fallback_step[device] = -1;
                Log.Information("SR Wide scan: tuner " + (device + 1) + " locked at " + measured_ks.ToString("0.0") + " kS, set rate " + current + " -> " + wanted + " kS (no new tune)");

                // no tune and no video reset: the demodulator is locked already, only the stored rate changes
                if (device == 0)
                    current_sr_0 = wanted;
                else
                    current_sr_1 = wanted;

                nim_thread?.SetConfiguredSymbolRate(device, wanted);
                _tuner_forms[device]?.UpdateTuner(device == 0 ? current_frequency_0 : current_frequency_1, wanted, device == 0 ? current_offset_0 : current_offset_1);
            }
        }

        // Tunes again to the current frequency with another symbol rate, on the UI thread like every other tune.
        private void RetuneWithSymbolRate(int device, uint sr)
        {
            Action tune = () =>
            {
                if (device == 0)
                    change_frequency(0, current_frequency_0, sr, current_rf_input_0, current_tone_22kHz_0, current_lnba_psu, current_lnbb_psu);
                else
                    change_frequency(1, current_frequency_1, sr, current_rf_input_1, current_tone_22kHz_1, current_lnba_psu, current_lnbb_psu);
            };

            if (_parent != null && _parent.IsHandleCreated)
                _parent.BeginInvoke(tune);
            else
                tune();
        }

        public void change_frequency(byte device, UInt32 freq, UInt32 sr,  uint rf_input, bool tone_22kHz_P1, byte lnbA_supply, byte lnbB_supply)
        {
            if (!hardware_connected)
                return;

            if (device >= GetVideoSourceCount())
            {
                return;
            }

            Log.Information("Change Frequency: " + device.ToString());

            switch (rf_input)
            {
                case nim.NIM_INPUT_TOP: Log.Information("RF Input: Nim Input Top Specified"); break;
                case nim.NIM_INPUT_BOTTOM: Log.Information("RF Input: Nim Input Bottom Specified"); break;
                default: Log.Information("Error: Invalid RF Input: " + rf_input.ToString()); break;
            }

            TunerConfig newConfig = new TunerConfig();

            newConfig.tuner = (byte)(device+1);
            newConfig.frequency = freq;
            newConfig.symbol_rate = sr;
            newConfig.rf_input = rf_input;
            newConfig.tone_22kHz_P1 = tone_22kHz_P1;
            newConfig.lnba_psu = lnbA_supply;
            newConfig.lnbb_psu = lnbB_supply;
            newConfig.capture_range_khz = capture_range_khz[device];
            newConfig.dvb_mode = dvb_mode[device];
            newConfig.wide_scan = wide_scan[device];
            newConfig.modcod_list = ModcodList(device);
            newConfig.freq_correction_khz = CorrectionKHz(device, freq);

            if (newConfig.frequency < 144000 || newConfig.frequency > 2450000)
            {
                Log.Information("Error: Invalid Frequency: " + newConfig.frequency);
                return;
            }

            Log.Information("Main: New Config: " + newConfig.ToString());

            if (device == 0)
            {
                current_frequency_0 = newConfig.frequency;
                current_sr_0 = sr;
                current_rf_input_0 = rf_input;
                current_tone_22kHz_0 = tone_22kHz_P1;

                if (VideoChangeCB != null)
                {
                    VideoChangeCB(1, false);
                }

            }
            else
            {
                current_frequency_1 = newConfig.frequency;
                current_sr_1 = sr;
                current_rf_input_1 = rf_input;
                current_tone_22kHz_1 = tone_22kHz_P1;

                if (VideoChangeCB != null)
                {
                    VideoChangeCB(2, false);
                }

            }

            config_queue.Enqueue(newConfig);

            

            if (_tuner_forms[device] != null)
            {
                _tuner_forms[device].UpdateTuner( (device == 0) ? current_frequency_0 : current_frequency_1, (device == 0) ? current_sr_0 : current_sr_0, (device == 0 ) ? current_offset_0 : current_offset_1);
            }

        }

        //public byte set_polarization_supply(byte lnb_num, bool supply_enable, bool supply_horizontal)
        //{
        //    return hardware_interface.hw_set_polarization_supply(lnb_num, supply_enable, supply_horizontal);
        //}

        public override int Initialize(VideoChangeCallback VideoChangeCB, Control Parent)
        {            
            switch (_settings.DefaultInterface)
            {
                case 0:
                    var hw_ask = new ChooseMinitiounerHardwareInterfaceForm();

                    if (hw_ask.ShowDialog() == DialogResult.OK)
                    {
                        switch(hw_ask.comboHardwareSelect.SelectedIndex)
                        {
                            case 0: hardware_interface = new FTDIInterface(); break;
                            case 1: hardware_interface = new PicoTunerInterface(); break;
                        }
                    }
                    else return -1;

                    break;
                case 1: hardware_interface = new FTDIInterface(); break;
                case 2: hardware_interface = new PicoTunerInterface(); break;
            }

            // with several boards connected: which one to use (setting PreferredBoardSerial, "" = automatic)
            if (hardware_interface is FTDIInterface ftdi_interface)
                ftdi_interface.PreferredBoardSerial = _settings.PreferredBoardSerial ?? "";

            return Initialize(VideoChangeCB, SourceStatusCB, false, "", "", "", Parent);
        }

        // Several boards at once (#29): the number the first tuner of this board has in the whole source, minus 1
        // (0 = tuner 1 for the first board, 2 = tuner 3 for a second board behind a Pro), only used for names.
        public int TunerNumberOffset { get; set; } = 0;

        private string TunerLabel(int local_tuner_index)
        {
            return "Tuner " + (TunerNumberOffset + local_tuner_index + 1).ToString();
        }

        // the board found for this source (null with the PicoTuner interface or before connecting)
        public MiniTiounerBoard SelectedBoard => (hardware_interface as FTDIInterface)?.SelectedBoard;

        public bool UseAllBoards => _settings.UseAllBoards;

        // Connects to one given board (a chip serial number of it) instead of asking for the interface and choosing
        // one: for the further boards of a source with several boards.
        public int InitializeForBoard(string chip_serial, VideoChangeCallback VideoChangeCB, Control Parent)
        {
            hardware_interface = new FTDIInterface { PreferredBoardSerial = chip_serial ?? "" };
            return Initialize(VideoChangeCB, SourceStatusCB, false, "", "", "", Parent);
        }

        // Set when Close() starts: the NIM thread's status callbacks then return at once instead of marshalling
        // to the UI thread, and Close() only pumps messages while one of them is still running (issue #34).
        private volatile bool _closing = false;
        private volatile bool _status_callback_active = false;

        public void nim_status_feedback(TunerStatus nim_status)
        {
            if (_closing)
                return;

            _status_callback_active = true;
            try
            {
                HandleNimStatus(nim_status);
            }
            finally
            {
                _status_callback_active = false;
            }
        }

        private void HandleNimStatus(TunerStatus nim_status)
        {
            _last_status = nim_status;

            bool T1P2locked = false;
            bool T2P1Locked = false;

            if (nim_status.T1P2_demod_status >= 2) T1P2locked = true;
            if (nim_status.T2P1_demod_status >= 2) T2P1Locked = true;


            if (T1P2_prevLocked != T1P2locked)
            {
                Log.Information("T1P2 (" + TunerLabel(0) + ") - Lock State Change: " + T1P2_prevLocked.ToString() + "->" + T1P2locked.ToString());

                if (nim_status.T1P2_demod_status >= 2)
                {
                    if (VideoChangeCB != null)
                    {
                        VideoChangeCB(1, true);
                    }
                    hardware_interface.hw_ts_led(0, true);
                }
                else
                {
                    if (VideoChangeCB != null)
                    {
                        VideoChangeCB(1, false);
                    }

                    hardware_interface.hw_ts_led(0, false);
                }

                T1P2_prevLocked = T1P2locked;
            }

            if (GetVideoSourceCount() == 2)
            {
                if (T2P1_prevLocked != T2P1Locked)
                {
                    Log.Information("T2P1 (" + TunerLabel(1) + ") - Lock State Change: " + T2P1_prevLocked.ToString() + "->" + T2P1Locked.ToString());

                    if (nim_status.T2P1_demod_status >= 2)
                    {
                        if (VideoChangeCB != null)
                        {
                            VideoChangeCB(2, true);
                        }

                        hardware_interface.hw_ts_led(1, true);
                    }
                    else
                    {
                        if (VideoChangeCB != null)
                        {
                            VideoChangeCB(2, false);
                        }

                        hardware_interface.hw_ts_led(1, false);
                    }

                    T2P1_prevLocked = T2P1Locked;
                }
            }

            nim_status.T2P1_requested_frequency = current_frequency_0;
            nim_status.T1P2_requested_frequency = current_frequency_1;

            // update properties
            UpdateTunerProperties(nim_status);
        }

        private void ChangeRFInput(byte Tuner, uint RFInput)
        {
            switch(Tuner)
            {
                case 0:
                    change_frequency(Tuner, current_frequency_0, current_sr_0, RFInput, current_tone_22kHz_0, current_lnba_psu, current_lnbb_psu);
                    break;
                case 1:
                    change_frequency(Tuner, current_frequency_1, current_sr_1, RFInput, current_tone_22kHz_1, current_lnba_psu, current_lnbb_psu);
                    break;
            }

        }

        private void ChangeSymbolRate(byte Tuner, uint SymbolRate)
        {
            if (Tuner < 2)
                fallback_step[Tuner] = -1; // chosen by hand: no automatic fallback

            switch (Tuner)
            {
                case 0:
                    change_frequency(Tuner, current_frequency_0, SymbolRate, current_rf_input_0, current_tone_22kHz_0, current_lnba_psu, current_lnbb_psu);
                    break;
                case 1:
                    change_frequency(Tuner, current_frequency_1, SymbolRate, current_rf_input_1, current_tone_22kHz_1, current_lnba_psu, current_lnbb_psu);
                    break;
            }
        }

        private void ChangeOffset(byte Tuner, int offset)
        {
            switch(Tuner)
            {
                case 0:
                    current_offset_0 = (uint)offset;
                    break;
                case 1:
                    current_offset_1 = (uint)offset;
                    break;
            }

            if (_tuner_forms[Tuner] != null)
            {
                _tuner_forms[Tuner].UpdateTuner((Tuner == 0) ? current_frequency_0 : current_frequency_1, (Tuner == 0) ? current_sr_0 : current_sr_0, (Tuner == 0) ? current_offset_0 : current_offset_1);
            }

        }

        private int Initialize(VideoChangeCallback VideoChangeCB, SourceStatusCallback SourceStatusCB, bool manual, string i2c_serial, string ts_serial, string ts2_serial, Control Parent)
        {

            this.VideoChangeCB = VideoChangeCB;
            this.SourceStatusCB = SourceStatusCB;

            hardware_init(manual, i2c_serial, ts_serial, ts2_serial);

            if (!hardware_connected)
            {
                MessageBox.Show("Error: No Working Hardware Detected");
                return -1;
            }

            // the settings of the board that was found (before anything reads them)
            LoadBoardSettings((hardware_interface as FTDIInterface)?.SelectedBoard?.ChipSerials.FirstOrDefault());

            // build properties
            _parent = Parent;

            // Restore the last used Switches state (LNB supply, 22kHz tone) BEFORE the properties are
            // built: the Switches dropdowns take their initial selection from these fields.
            if (_settings.Tone22kHz == null || _settings.Tone22kHz.Length < 2)
                _settings.Tone22kHz = new bool[2];
            current_lnba_psu = _settings.DefaultLnbASupply;
            current_lnbb_psu = _settings.DefaultLnbBSupply;
            current_tone_22kHz_0 = _settings.Tone22kHz[0];
            current_tone_22kHz_1 = _settings.Tone22kHz[1];

            // tuning trim per tuner: the sliders and rows of the properties take these values when they are built
            for (int t = 0; t < 2; t++)
            {
                capture_range_khz[t] = (_settings.CaptureRangeKHz != null && _settings.CaptureRangeKHz.Length > t) ? _settings.CaptureRangeKHz[t] : 0;
                dvb_mode[t] = (_settings.DvbMode != null && _settings.DvbMode.Length > t && _settings.DvbMode[t] <= 2) ? _settings.DvbMode[t] : (byte)0;
                wide_scan[t] = _settings.WideScan != null && _settings.WideScan.Length > t && _settings.WideScan[t];
                freq_correction_ppm[t] = (_settings.FreqCorrectionPpm != null && _settings.FreqCorrectionPpm.Length > t) ? _settings.FreqCorrectionPpm[t] : 0;
            }

            BuildSourceProperties();

            _source_properties.UpdateValue("source_hw_interface", hardware_interface.GetName);

            Log.Information("Main: Starting Nim Thread");

            Log.Information("Switch LED's");
            
            
            // switch off ts leds
            hardware_interface.hw_ts_led(0, false);
            hardware_interface.hw_ts_led(1, false);

            // configure nim thread
            nim_thread = new NimThread(config_queue, hardware_interface, nim_status_feedback, false,
                _settings.EnableDigoleDisplay, _settings.DigoleI2cAddress, HardwareDevice,
                new uint[] { _settings.Offset1, _settings.Offset2 }, _settings.DigoleCallsign,
                _settings.DigoleLocator, _settings.DigoleName);
            nim_thread.ApplyReceiverSettings(_settings);
            ApplyErrorEvent(false);
            nim_thread.TunerCount = ts_devices;
            nim_thread_t = new Thread(nim_thread.worker_thread);
            nim_thread_t.IsBackground = true;



            /*
             
            TODO: default lnb voltage supply settings

            // set default lnb supply
            Log.Information(current_enable_lnb_supply.ToString());
            Log.Information(current_enable_horiz_supply.ToString());

            */

            /*
            current_lnba_psu = _settings.DefaultLnbASupply;
            switch (current_lnba_psu)
            {
                case 0: hardware_interface.hw_set_polarization_supply(0, false, false);
                    break;
                case 1:
                    hardware_interface.hw_set_polarization_supply(0, true, false);
                    break;
                case 2:
                    hardware_interface.hw_set_polarization_supply(0, true, true);
                    break;
            }

            current_lnbb_psu = _settings.DefaultLnbBSupply;
            switch (current_lnbb_psu)
            {
                case 0:
                    hardware_interface.hw_set_polarization_supply(1, false, false);
                    break;
                case 1:
                    hardware_interface.hw_set_polarization_supply(1, true, false);
                    break;
                case 2:
                    hardware_interface.hw_set_polarization_supply(1, true, true);
                    break;
            }
            */

            current_lnba_psu = _settings.DefaultLnbASupply;
            current_lnbb_psu = _settings.DefaultLnbBSupply;

            // (Removed: an unconditional hardware_interface.hw_set_polarization_supply(1, false,
            // false) used to sit here - leftover from the commented-out settings-switch block
            // above, ignoring current_lnbb_psu entirely. It force-killed LNB-B's supply for
            // ~600ms before the real per-settings enable further down turned it back on - an
            // extra disable/re-enable cycle LNB-A never got. If the RT5047 needs a minimum
            // off-time or has fault-latch behavior on a fast re-enable, this alone could explain
            // LNB-B never powering up cleanly while LNB-A (no such pulse) works fine.)

            // set startup rf inputs according to settings
            current_rf_input_0 = nim.NIM_INPUT_A;
            current_rf_input_1 = nim.NIM_INPUT_A;

            switch (_settings.DefaultRFInput )
            {
                case 1:
                    current_rf_input_0 = nim.NIM_INPUT_A;
                    current_rf_input_1 = nim.NIM_INPUT_B;
                    break;
                case 2:
                    current_rf_input_0 = nim.NIM_INPUT_B;
                    current_rf_input_1 = nim.NIM_INPUT_A;
                    break;
                case 3:
                    current_rf_input_0 = nim.NIM_INPUT_B;
                    current_rf_input_1 = nim.NIM_INPUT_B;
                    break;
            }

            current_frequency_0 = 741525;
            current_frequency_1 = 741525;

            current_offset_0 = _settings.Offset1;
            current_offset_1 = _settings.Offset2;


            current_sr_0 = 1500;
            current_sr_1 = 1500;

            // setup tuner 0
            change_frequency(0, current_frequency_0, current_sr_0, current_rf_input_0, current_tone_22kHz_0, current_lnba_psu, current_lnbb_psu);

            if (ts_devices == 2)
            {
                // setup tuner 1
                change_frequency(1, current_frequency_1, current_sr_1, current_rf_input_1, current_tone_22kHz_1, current_lnba_psu, current_lnbb_psu);
            }

            nim_thread_t.Start();

            // TS thread - T1P2
            ts_thread = new TSThread(ts_data_queue, FlushTS2, ReadTS2, "MT TS2");
            ts_thread_t = new Thread(ts_thread.worker_thread);
            ts_thread_t.IsBackground = true;
            ts_thread_t.Start();

            if (ts_devices == 2)
            {
                ts_thread2 = new TSThread(ts_data_queue2, FlushTS1, ReadTS1, "MT TS1");
                ts_thread_2_t = new Thread(ts_thread2.worker_thread);
                ts_thread_2_t.IsBackground = true;
                ts_thread_2_t.Start();
            }

            // the PID choice of the Special tab (automatic unless a PID was typed in and the manual mode saved)
            ApplyPidFilter(0);
            ApplyPidFilter(1);

            // start TS Parser
            ts_parser_thread = new TSParserThread(parse_ts_data_callback);
            RegisterTSConsumer(0, ts_parser_thread.parser_ts_data_queue);
            ts_parser_t = new Thread(ts_parser_thread.worker_thread);
            ts_parser_t.IsBackground = true;
            ts_parser_t.Start();

            if (ts_devices == 2)
            {
                ts_parser_thread2 = new TSParserThread(parse_ts2_data_callback);
                RegisterTSConsumer(1, ts_parser_thread2.parser_ts_data_queue);
                ts_parser_2_t = new Thread(ts_parser_thread2.worker_thread);
                ts_parser_2_t.IsBackground = true;
                ts_parser_2_t.Start();
            }

            return ts_devices;
        }

        public void parse_ts_data_callback(TSStatus ts_status)
        {
            UpdateTSProperties(1, ts_status);
        }

        public void parse_ts2_data_callback(TSStatus ts_status)
        {
            UpdateTSProperties(2, ts_status);
        }


        public override CircularBuffer GetVideoDataQueue(int device)
        {
            switch (device)
            {
                case 0: return ts_data_queue;
                case 1: return ts_data_queue2;
            }

            return ts_data_queue;
        }

        void FlushTS2()
        {
            hardware_interface.transport_flush(PicoTunerInterface.TS2);
        }

        byte ReadTS2(ref byte[] data, ref uint dataRead)
        {
            byte err = hardware_interface.transport_read(PicoTunerInterface.TS2, ref data, ref dataRead);
            DataReceived(PicoTunerInterface.TS2);
            return err;
        }

        byte ReadTS1(ref byte[] data, ref uint dataRead)
        {
            byte err = hardware_interface.transport_read(PicoTunerInterface.TS1, ref data, ref dataRead);
            DataReceived(PicoTunerInterface.TS1);
            return err;
        }


        void FlushTS1()
        {
            hardware_interface.transport_flush(PicoTunerInterface.TS1);
        }


        private void DataReceived(byte TS)
        {
            if (TS == 1)
            {
                ts_thread.NewDataPresent();
            }
            else
            {
                ts_thread2.NewDataPresent();
            }
        }

        private void hardware_init(bool manual, string i2c_serial, string ts_serial, string ts2_serial)
        {

            // detect ftdi devices
            uint i2c_port = 99;
            uint ts_port = 99;
            uint ts_port2 = 99;
            uint aux_port = 99;

            string deviceName = "Unknown";

            byte err = 0;

            if (manual)
            {
                err = hardware_interface.hw_detect(ref i2c_port, ref ts_port, ref ts_port2, ref aux_port, ref deviceName, i2c_serial, ts_serial, ts2_serial, "");
            }
            else
            {
                err = hardware_interface.hw_detect(ref i2c_port, ref ts_port, ref ts_port2, ref aux_port, ref deviceName);
            }


            if (ts_port2 == 99)
                ts_devices = 1;
            else
                ts_devices = 2;

            //ts_devices = 1;

            if (i2c_port == 99 || ts_port == 99)    // not detected properly, revert to 0 and 1 and hope for the best
            {
                ts_devices = 1;
                Log.Information("Hardware not detected properly, reverting to 0,1");
                err = hardware_interface.hw_init(0, 1, 99, 99);
            }
            else
            {
                Log.Information("Trying detected ports:");
                Log.Information("i2c port: " + i2c_port.ToString());
                Log.Information("ts port: " + ts_port.ToString());
                Log.Information("ts2 port: " + ts_port2.ToString());
                Log.Information("aux port: " + aux_port.ToString());
                err = hardware_interface.hw_init(i2c_port, ts_port, ts_port2, aux_port);
            }

            if (err != 0)
            {
                Log.Information("Main: Error: FTDI Failed " + err.ToString());
                hardware_connected = false;
                return;
            }

            hardware_connected = true;

            HardwareDevice = deviceName;

            Func<uint, string> port_text = p => p == 99 ? "-" : p.ToString();
            _detected_ports = "I2C " + port_text(i2c_port) + ", TS " + port_text(ts_port) + ", TS2 " + port_text(ts_port2) + ", AUX " + port_text(aux_port);

            if (AuxAvailable)
            {
                byte externValue = 0;
                for (int i = 0; i < _settings.ExternState.Length; i++)
                {
                    if (_settings.ExternState[i])
                        externValue |= (byte)(1 << i);
                }
                hardware_interface.aux_gpio_write(externValue);
            }
        }



        public override void ConfigureVideoPlayers(List<OTMediaPlayer> MediaPlayers)
        {
            if (MediaPlayers.Count != ts_devices)
            {
                Log.Information("Error: MinitiounerSource Expected " +  ts_devices.ToString() + " video players, but only received " + MediaPlayers.Count);
            }

            for (int c = 0; c < MediaPlayers.Count; c++)
            {
                _media_player.Add(MediaPlayers[c]);
                _media_player[c].onVideoOut += MinitiounerSource_onVideoOut;
                if (_settings.DefaultMuted[c])
                {
                    _media_player[c].SetVolume(0);
                }
                else
                {
                    _media_player[c].SetVolume((int)_settings.DefaultVolume[c]);
                }
            }

        }

        private void MinitiounerSource_onVideoOut(object sender, MediaStatus e)
        {
            for (int c = 0; c < _media_player.Count; c++)
            {
                if ((OTMediaPlayer)sender == _media_player[c])
                {
                    preMute[c] = (int)_settings.DefaultVolume[c];
                    muted[c] = _settings.DefaultMuted[c];
                    if (muted[c] == true)
                    {
                        _media_player[c].SetVolume(0);
                    }
                    else
                    {
                        _media_player[c].SetVolume(preMute[c]);
                    }

                    UpdateMediaProperties(c, e);
                    break;
                }
            }
        }

        public override string GetName()
        {
            return "Minitiouner Variant";
        }

        // What is already known after connect; no extra I2C access (the NIM thread owns the bus).
        public override List<KeyValuePair<string, string>> GetHardwareInfo()
        {
            var info = new List<KeyValuePair<string, string>>();
            info.Add(new KeyValuePair<string, string>("Source", GetName()));

            if (!hardware_connected || hardware_interface == null)
            {
                info.Add(new KeyValuePair<string, string>("Hardware", "not connected"));
                return info;
            }

            info.Add(new KeyValuePair<string, string>("Hardware interface", hardware_interface.GetName));
            info.Add(new KeyValuePair<string, string>("Board", HardwareDevice ?? "unknown"));
            info.Add(new KeyValuePair<string, string>("TS streams", ts_devices.ToString()));
            info.Add(new KeyValuePair<string, string>("Used FTDI devices (index)", _detected_ports));
            string detection = (hardware_interface as FTDIInterface)?.DetectionSummary;
            if (!string.IsNullOrEmpty(detection))
                info.Add(new KeyValuePair<string, string>("Board detection", detection));
            info.Add(new KeyValuePair<string, string>("AUX chip (EXTERN outputs)", AuxAvailable ? "open" : "not available"));

            var status = _last_status;
            if (status == null)
            {
                info.Add(new KeyValuePair<string, string>("Demodulator", "no status received yet"));
            }
            else
            {
                info.Add(new KeyValuePair<string, string>("STV0910",
                    "MID 0x" + status.chip_mid.ToString("X2") + " (ident " + (status.chip_mid >> 4) + ", release " + (status.chip_mid & 0x0F) + "), DID 0x" + status.chip_did.ToString("X2")));
                info.Add(new KeyValuePair<string, string>("STV0910 PLL", status.pll_locked ? "locked" : "not locked"));
                info.Add(new KeyValuePair<string, string>("LNA input 1", status.lna_top_ok ? "found" : "not present"));
                info.Add(new KeyValuePair<string, string>("LNA input 2", status.lna_bottom_ok ? "found" : "not present"));
                info.Add(new KeyValuePair<string, string>("Status refresh", status.refresh_ms + " ms"));
            }

            return info;
        }

        public override void OverrideDefaultMuted(bool Override)
        {
            if (Override)
            {
                preMute[0] = (int)_settings.DefaultVolume[0];               // save DefaultVolume in preMute
                _tuner1_properties.UpdateValue("volume_slider_1", "0");     // side effect: will set DefaultVolume to 0
                _tuner1_properties.UpdateMuteButtonColor("media_controls_1", Color.PaleVioletRed);
                muted[0] = _settings.DefaultMuted[0] = true;
                _settings.DefaultVolume[0] = (uint)preMute[0];              // restore DefaultVolume

                if (ts_devices == 2)
                {
                    preMute[1] = (int)_settings.DefaultVolume[1];           // save DefaultVolume in preMute
                    _tuner2_properties.UpdateValue("volume_slider_2", "0"); // side effect: will set DefaultVolume to 0
                    _tuner2_properties.UpdateMuteButtonColor("media_controls_2", Color.PaleVioletRed);
                    muted[1] = _settings.DefaultMuted[1] = true;
                    _settings.DefaultVolume[1] = (uint)preMute[1];          // restore DefaultVolume
                }
            }
        }

        public override string GetDescription()
        {
            return "Minitiouner Variant" +
            Environment.NewLine + Environment.NewLine +
            "Should work with most Minitiouner Variants" +
            Environment.NewLine + Environment.NewLine +
            "Select FTDI or PicoTuner interface in Settings";
        }

        // Processes at most maxMessages pending window messages of this thread and returns. Application.DoEvents() runs
        // until the queue is empty and never returned when messages kept coming (issue #34); with the cap and the
        // 5 s limit of the caller waiting for the NIM thread cannot hang any more.
        [StructLayout(LayoutKind.Sequential)]
        private struct PumpMsg
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int pt_x;
            public int pt_y;
        }

        [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
        private static extern bool PeekMessageNative(out PumpMsg msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int exitCode);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref PumpMsg msg);

        [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
        private static extern IntPtr DispatchMessageNative(ref PumpMsg msg);

        private static void PumpMessages(int maxMessages)
        {
            const uint PM_REMOVE = 0x0001;
            const uint WM_QUIT = 0x0012;

            for (int i = 0; i < maxMessages; i++)
            {
                if (!PeekMessageNative(out PumpMsg msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                    return;

                if (msg.message == WM_QUIT)
                {
                    PostQuitMessage((int)msg.wParam);   // not ours to swallow
                    return;
                }

                TranslateMessage(ref msg);
                DispatchMessageNative(ref msg);
            }
        }

        private static void CloseStep(string name, Action action)
        {
            Log.Information("Close: " + name + "...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Close: " + name + " failed");
            }
            Log.Information("Close: " + name + " done in " + sw.ElapsedMilliseconds + " ms");
        }

        public override void Close()
        {
            _closing = true;

            // Every step is logged when it starts and when it ends, so a hang on exit shows where it stands (issue #34).
            CloseStep("save settings", () => SaveSettingsFiles());

            // Thread.Abort() doesn't exist on modern .NET (throws PlatformNotSupportedException,
            // which used to take the whole process down since these are foreground threads) -
            // signal each worker cooperatively instead and let it exit its own loop.
            CloseStep("stop TS parser threads", () =>
            {
                ts_parser_thread?.Stop();
                ts_parser_thread2?.Stop();
            });
            bool ts_thread_stopped = false;
            CloseStep("stop TS thread 1", () => ts_thread?.Stop(ref ts_thread_stopped));
            bool ts_thread2_stopped = false;
            CloseStep("stop TS thread 2", () => ts_thread2?.Stop(ref ts_thread2_stopped));
            CloseStep("stop NIM thread (request)", () => nim_thread?.Stop());

            // Wait for NimThread to finish - WITH message pumping. This is the actual fix for
            // "Digole doesn't clear on exit" and the hang in Close(): NimThread runs
            // get_nim_status() -> nim_status_feedback() -> UpdateTunerProperties() -> OnSourceData
            // -> MainForm.UpdateInfo(), which does a SYNCHRONOUS Control.Invoke onto the UI
            // thread. We are on the UI thread here (FormClosing). A plain Join() or lock(HwLock)
            // without pumping therefore deadlocks whenever a get_nim_status() cycle is in flight
            // (NimThread waits for the UI thread inside Invoke, the UI thread waits for NimThread):
            // NimThread never reaches its "Loop exited" / Digole shutdown write, and Close() never
            // returns ("Bye!" never logged). Pumping lets that pending Invoke complete so the
            // worker can leave its loop, send the Digole greeting/clear and exit.
            // Normal case: finishes within one status cycle (~100-300 ms).
            bool nim_thread_joined = true;
            if (nim_thread_t != null)
            {
                Log.Information("Close: waiting for the NIM thread (alive=" + nim_thread_t.IsAlive + ")...");
                var join_sw = System.Diagnostics.Stopwatch.StartNew();

                // Reports the real state every 2 s from its own thread, so it also logs while the UI thread is stuck (issue #34)
                bool wait_done = false;
                var wait_reporter = new Thread(() =>
                {
                    for (int i = 0; i < 5 && !wait_done; i++)
                    {
                        Thread.Sleep(2000);
                        if (!wait_done)
                            Log.Warning("Close: still waiting for the NIM thread after " + join_sw.ElapsedMilliseconds + " ms: state=" + nim_thread_t.ThreadState
                                + ", status callback active=" + _status_callback_active);
                    }
                });
                wait_reporter.IsBackground = true;
                wait_reporter.Start();
                long next_report_ms = 1000;
                while (!(nim_thread_joined = nim_thread_t.Join(10)))
                {
                    if (join_sw.Elapsed > TimeSpan.FromSeconds(5))
                        break;

                    // Pump messages only while a status callback of the NIM thread is still running (it may wait for
                    // the UI thread in Control.Invoke). Application.DoEvents() returns only when the message queue is
                    // empty, so with a steady stream of messages (repaints, updates) it never returned and Close()
                    // hung in it, with the NIM thread long gone (issue #34).
                    if (_status_callback_active)
                    {
                        var events_sw = System.Diagnostics.Stopwatch.StartNew();
                        PumpMessages(50);
                        if (events_sw.ElapsedMilliseconds >= 100)
                            Log.Warning("Close: pumping messages took " + events_sw.ElapsedMilliseconds + " ms while waiting for the NIM thread");
                    }

                    if (join_sw.ElapsedMilliseconds >= next_report_ms)
                    {
                        Log.Information("Close: still waiting for the NIM thread after " + join_sw.ElapsedMilliseconds + " ms (state=" + nim_thread_t.ThreadState + ")");
                        next_report_ms += 1000;
                    }
                }
                wait_done = true;
                Log.Information("Nim Thread Join returned, joined=" + nim_thread_joined + ", waited=" + join_sw.ElapsedMilliseconds + "ms");
            }

            // Switch off TS LEDs and LNB-A/LNB-B power supply, then EXTERN-0..7.
            // NimThread has normally exited by now, so nothing races us on the shared FTDI
            // I2C-channel state (MPSSEbuffer etc.). If the join timed out it is still alive, so
            // take HwLock (bounded wait, never on an unbounded lock from the UI thread) to at
            // least not interleave with a transaction that is in flight.
            //
            // Previously missing entirely: the LNB supply (and its LNBA/LNBB indicator LEDs)
            // stayed on after Close() - nothing ever called hw_set_polarization_supply(..,
            // false, ..) on shutdown, only the TS LEDs were turned off.
            bool have_hw_lock = false;
            if (!nim_thread_joined && nim_thread != null)
            {
                have_hw_lock = Monitor.TryEnter(nim_thread.HwLock, TimeSpan.FromSeconds(2));
                if (!have_hw_lock)
                    Log.Warning("Close(): NimThread still running and HwLock not acquired, switching hardware off without it");
            }
            try
            {
                CloseStep("switch off TS LEDs and LNB supply", () =>
                {
                    hardware_interface?.hw_ts_led(0, false);
                    hardware_interface?.hw_ts_led(1, false);
                    if (_settings.LnbSupplyControl)
                    {
                        hardware_interface?.hw_set_polarization_supply(0, false, false);
                        hardware_interface?.hw_set_polarization_supply(1, false, false);
                    }
                });
            }
            finally
            {
                if (have_hw_lock)
                    Monitor.Exit(nim_thread.HwLock);
            }

            // EXTERN-0..7 (AUX chip GPIO) - separate FTDI device/USB connection from the NIM
            // I2C bus (see aux_gpio_write's doc comment), so unlike the writes above this isn't
            // racing NimThread and doesn't need HwLock either. Not tied to _settings.ExternState
            // (that's just the persisted UI checkbox state for next connect) - this
            // unconditionally drives the physical pins off so nothing stays energized after
            // OpenTuner closes.
            CloseStep("switch off EXTERN outputs", () => hardware_interface?.aux_gpio_write(0));
        }

        // for the settings dialog of a source with several boards: what to add to the title ("V2 - tuner 3") and how many
        // tuners the board has (0 = as found after connecting, else 2)
        public string SettingsTitleSuffix { get; set; } = "";
        public int SettingsTunerCount { get; set; } = 0;

        // The settings of one given board, e.g. of a further board before anything is connected (a chip serial of it).
        public void ShowSettingsForBoard(string chip_serial)
        {
            LoadBoardSettings(chip_serial);
            ShowSettingsDialog();
        }

        // the board that is chosen when several are connected, from the setting PreferredBoardSerial ("" = automatic)
        public string PreferredBoardSerial => _settings.PreferredBoardSerial;

        public override void ShowSettings()
        {
            // Not connected yet: the board is not known, so the values shown (and saved) would only reach the general file
            // and the file of the board would overwrite them at the next connect. Find the board the same way the connect
            // does (from the USB device list, nothing is opened) and work on its settings.
            int detected_tuners = 0;
            if (_board_settings_manager == null && !hardware_connected)
            {
                try
                {
                    var devices = HardwareInfo.EnumerateFtdiDevices(out string list_error);
                    var boards = BoardDetection.Detect(devices, new List<string>());
                    var board = BoardDetection.Select(boards, _settings.PreferredBoardSerial);

                    // No board on USB: the values would only reach the general file (and show tuners that are not there).
                    if (board == null)
                    {
                        MessageBox.Show("No MiniTiouner board found. Connect a board first, then open the settings.", "Source Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    detected_tuners = board.TsCount;
                    LoadBoardSettings(board?.ChipSerials.FirstOrDefault());
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Board settings: could not find the board before connecting, using the general settings");
                }
            }

            ShowSettingsDialog(detected_tuners);
        }

        // detected_tuners: tuners of the board found in the USB device list when not connected (0 = not known, two are shown)
        private void ShowSettingsDialog(int detected_tuners = 0)
        {
            int tuner_count = SettingsTunerCount > 0 ? SettingsTunerCount : (hardware_connected ? ts_devices : (detected_tuners > 0 ? detected_tuners : 2));
            MinitiounerSettingsForm settings_form = new MinitiounerSettingsForm(ref _settings, SettingsTitleSuffix, TunerNumberOffset, tuner_count);
            if (settings_form.ShowDialog() == DialogResult.OK)
            {
                SaveSettingsFiles();
                // Digole text can be applied live; other settings (interface, offsets, LNB defaults,
                // Digole enable/address) are read on connect and need a reconnect.
                nim_thread?.UpdateDigoleIdentity(_settings.DigoleCallsign, _settings.DigoleLocator, _settings.DigoleName, _settings.DigoleCallsignCharWidth); 

                // tuning trim (correction / capture range) is applied live: sliders, properties and a new tune
                for (int t = 0; t < 2; t++)
                {
                    uint capture = (_settings.CaptureRangeKHz != null && _settings.CaptureRangeKHz.Length > t) ? _settings.CaptureRangeKHz[t] : 0;
                    double correction = (_settings.FreqCorrectionPpm != null && _settings.FreqCorrectionPpm.Length > t) ? _settings.FreqCorrectionPpm[t] : 0;

                    if (capture != capture_range_khz[t] || correction != freq_correction_ppm[t])
                    {
                        (t == 0 ? _frequency_1 : _frequency_2)?.SetTrim(capture, correction);
                        ApplyTunerTrim(t, capture, correction);
                    }
                }
            }
            
        }

        public override void ConfigureTSRecorders(List<TSRecorder> TSRecorders)
        {
            _ts_recorders = TSRecorders;

            for (int c = 0; c < _ts_recorders.Count; c++)
            {
                _ts_recorders[c].onRecordStatusChange += MinitiounerSource_onRecordStatusChange;
            }

            if (_ts_recorders.Count > 0)
                _tuner1_properties?.SetRecordTooltip("media_controls_1", _ts_recorders[0].Path);
            if (_ts_recorders.Count > 1)
                _tuner2_properties?.SetRecordTooltip("media_controls_2", _ts_recorders[1].Path);
        }

        private void MinitiounerSource_onRecordStatusChange(object sender, bool e)
        {
            Log.Information(((TSRecorder)(sender)).ID.ToString() + " recording status : " + e.ToString());
        }

        public override void ConfigureTSStreamers(List<TSUdpStreamer> TSStreamers)
        {
            _ts_streamers = TSStreamers;
            
            for (int c = 0; c < _ts_streamers.Count; c++)
            {
                _ts_streamers[c].onStreamStatusChange += MinitiounerSource_onStreamStatusChange;
            }

            // steady UDP streams are on from the start
            if (_ts_streamers.Count > 0 && _ts_streamers[0].stream)
                _tuner1_properties?.UpdateStreamButtonColor("media_controls_1", opentuner.Utilities.CommonFunctions.UdpActiveColor);
            if (_ts_streamers.Count > 1 && _ts_streamers[1].stream)
                _tuner2_properties?.UpdateStreamButtonColor("media_controls_2", opentuner.Utilities.CommonFunctions.UdpActiveColor);
        }

        private void MinitiounerSource_onStreamStatusChange(object sender, bool e)
        {
            Log.Information(((TSUdpStreamer)(sender)).ID.ToString() + " streaming status : " + e.ToString());
        }

        public override void ConfigureMediaPath(string MediaPath)
        {
            _mediapath = MediaPath;
            _tuner1_properties?.SetSnapshotTooltip("media_controls_1", _mediapath);
            _tuner2_properties?.SetSnapshotTooltip("media_controls_2", _mediapath);
        }

        public override string GetMoreInfoLink()
        {
            return "https://www.zr6tg.co.za/opentuner-minitiouner-source/";
        }
    }
}
