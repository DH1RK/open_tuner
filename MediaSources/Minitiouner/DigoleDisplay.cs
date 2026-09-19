using System;
using System.Globalization;
using System.Linq;
using System.Text;
using opentuner.MediaSources.Minitiouner.HardwareInterfaces;

namespace opentuner.MediaSources.Minitiouner
{
    // Drives a Digole serial OLED/LCD wired in I2C mode to JP3 ("I2C-NIM") on a
    // MiniTiounerPro V2 board - the same physical I2C bus as the STV0910/STV6120 NIM chips
    // (see MiniTiounerPro V2 schematic sheet 3). Digole's I2C command set is the same ASCII
    // protocol as its UART mode, just framed as a plain I2C write instead of a serial byte
    // stream - see https://www.digole.com (Serial Display Adapter Manual).
    public class DigoleDisplay
    {
        private static readonly CultureInfo de = CultureInfo.GetCultureInfo("de-DE");

        private readonly MTHardwareInterface hw;
        private readonly byte i2c_address;
        private string last_rendered = null;

        public DigoleDisplay(MTHardwareInterface hardware, byte i2cAddress)
        {
            hw = hardware;
            i2c_address = i2cAddress;
        }

        // device_name: e.g. "MiniTiouner-V2" / "MiniTiouner-PRO2" (MinitiounerSource.HardwareDevice)
        // tuner_label: "TUNER A" / "TUNER B"
        // frequency_kHz / symbol_rate_kS: the currently tuned values (not live-measured, matches
        // MiniTioune's own display, which shows the requested tuning, not a jittering live read)
        // rf_level_dBm: NimStatus.T1P2_input_power_level / T2P1_input_power_level
        // mer_dB: NimStatus.T1P2_mer / T2P1_mer already divided by 10 (see MinitiounerProperties.cs)
        // service_name: decoded TS service name (SDT), truncated to 14 chars - matches the
        // "TSStatus.ServiceName"/"last_service_name_0/1" fields in MinitiounerSource
        // video_codec / modcod_name: e.g. "h264" / "8PSK 3/4" - combined into one "Inf:" line
        public void UpdateStatus(string device_name, string tuner_label, long frequency_kHz, uint symbol_rate_kS, short rf_level_dBm, double mer_dB, string service_name, string video_codec, string modcod_name)
        {
            double frequency_MHz = frequency_kHz / 1000.0;

            string service_short = service_name ?? "";
            if (service_short.Length > 14)
                service_short = service_short.Substring(0, 14);

            string info = string.Join(" ", new[] { video_codec, modcod_name }.Where(s => !string.IsNullOrEmpty(s)));

            string line0 = device_name ?? "";
            string line1 = "Serit FTS-4334L";
            string line2 = "    " + tuner_label;
            string line3 = "Frq: " + frequency_MHz.ToString("N3", de) + " MHz";
            string line4 = "SR:  " + symbol_rate_kS.ToString(de) + " KS/s";
            string line5 = "RF:  " + rf_level_dBm.ToString(de) + "dBm";
            string line6 = "MER: " + mer_dB.ToString("F1", de) + "dB";
            string line7 = "Srv: " + service_short;
            string line8 = "Inf: " + info;

            string[] lines = { line0, line1, line2, line3, line4, line5, line6, line7, line8 };

            string rendered = string.Join("\n", lines);

            // Change detection - avoid needless traffic on the shared NIM I2C bus (this is
            // called every ~200ms from NimThread's polling loop, most values don't change
            // that often).
            if (rendered == last_rendered)
                return;

            last_rendered = rendered;

            var cmd = new System.Collections.Generic.List<byte>();

            cmd.AddRange(Encoding.ASCII.GetBytes("CL")); // clear screen

            for (byte row = 0; row < lines.Length; row++)
            {
                cmd.AddRange(Encoding.ASCII.GetBytes("TP"));
                cmd.Add(0);   // column
                cmd.Add(row); // row

                cmd.AddRange(Encoding.ASCII.GetBytes("TT"));
                cmd.AddRange(Encoding.ASCII.GetBytes(lines[row]));
                cmd.Add(0); // null terminator, per Digole's "TT" command
            }

            hw.i2c_write_raw(i2c_address, cmd.ToArray());
        }

        // Clears the display - call on shutdown so a stale reading isn't left on screen
        // after OpenTuner closes. Bypasses change detection (always sends). Returns the
        // i2c_write_raw error code (0 = ok) so callers can log a shutdown failure instead
        // of it being silently discarded.
        public byte Clear()
        {
            last_rendered = null;
            return hw.i2c_write_raw(i2c_address, Encoding.ASCII.GetBytes("CL"));
        }

        // Greeting screen shown before the first frequency is tuned on either channel, and
        // again on shutdown instead of a blank Clear() - so the display always shows something
        // meaningful rather than sitting blank/stale between sessions. Bypasses change detection
        // (always sends) since it's called at most once per state transition, not every poll tick.
        // phase: short state label shown on its own line so the screens can be told apart at a
        // glance - "START" (first screen after connect), "NO SIGNAL" (waiting for lock again),
        // "END" (shutdown / manual test; also adds the "73!").
        public byte ShowGreeting(string device_name, string callsign, string phase = "")
        {
            last_rendered = null;

            string[] lines =
            {
                device_name ?? "",
                "Serit FTS-4334L",
                "",
                string.IsNullOrEmpty(callsign) ? "" : callsign,
                "",
                phase ?? "",
                phase == "END" ? "73!" : "",
            };

            var cmd = new System.Collections.Generic.List<byte>();
            cmd.AddRange(Encoding.ASCII.GetBytes("CL"));

            for (byte row = 0; row < lines.Length; row++)
            {
                cmd.AddRange(Encoding.ASCII.GetBytes("TP"));
                cmd.Add(0);
                cmd.Add(row);
                cmd.AddRange(Encoding.ASCII.GetBytes("TT"));
                cmd.AddRange(Encoding.ASCII.GetBytes(lines[row]));
                cmd.Add(0);
            }

            return hw.i2c_write_raw(i2c_address, cmd.ToArray());
        }
    }
}
