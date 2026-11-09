using System;
using System.Globalization;

namespace opentuner.MediaSources.Minitiouner
{
    // Error counter 1 of the demodulator (Chip tab, group "error counter"; MiniTioune's "Error/Event" panel): what it counts (ERRCTRL1.ERR_SOURCE1, the event
    // type), how it counts (ERRCTRL1.NUM_EVENT1, "Nbr of Event") and how the 23 bit result (ERRCNT12..10) is shown. All as MiniTioune writes them (I2C capture
    // ErrorEventDialog.txt, see stv0910_read_event_counter).
    public static class ErrorEvent
    {
        // ERR_SOURCE1 (data sheet, ERRCTRLx): the name, the counting unit and the decimal point of the rate modes (value = register / 2^point).
        // The sources without a name in MiniTioune's list are the DVB-S ones (Viterbi / Reed-Solomon).
        public sealed class Source
        {
            public string Name;
            public string Unit;      // what is counted: "bytes", "packets", "frames", "clocks / 4096", "clocks / 512"
            public int Point;
            public Source(string name, string unit, int point) { Name = name; Unit = unit; Point = point; }
        }

        public static readonly Source[] Sources =
        {
            new Source("Demod bit errors (DVB-S)", "bytes", 19),
            new Source("LDPC errors (DVB-S2)", "frames", 7),
            new Source("BCH errors", "frames", 19),
            new Source("BCH error flag", "frames", 23),
            new Source("Frame rate LDPC", "clocks / 4096", 23),
            new Source("BB header error", "frames", 23),
            new Source("Packet errors PER (CRC-8)", "packets", 23),
            new Source("Bit errors Reed-Solomon (DVB-S)", "bytes", 23),
            new Source("Byte errors Reed-Solomon (DVB-S)", "bytes", 23),
            new Source("Packet errors (DVB-S)", "packets", 23),
            new Source("Packet error number (DVB-S)", "packets", 19),
            new Source("Bad DFL (DVB-S2)", "frames", 23),
            new Source("TS error count", "packets", 23),
            new Source("TS FIFO", "clocks / 4096", 7),
            new Source("Frame7 DFL", "frames", 7),
            new Source("Packet bitrate", "clocks / 512", 23),
        };

        public static string[] SourceNames()
        {
            var names = new string[Sources.Length];
            for (int n = 0; n < names.Length; n++)
                names[n] = Sources[n].Name;
            return names;
        }

        // ERRCTRL1.NUM_EVENT1: 0 and 1 count, 2 to 6 give a rate over 2^14 ... 2^22 bytes (2^8 ... 2^16 packets, 2^4 ... 2^12 frames), 7 the average
        public static readonly string[] Modes =
        {
            "count with reset", "count without reset", "rate 2^14", "rate 2^16", "rate 2^18", "rate 2^20", "rate 2^22", "average",
        };

        // The register value in a readable unit, one line: a count as it is, a rate as register / 2^point per counting unit; the frame rate of the LDPC decoder
        // and the packet bitrate (rate modes) are converted with the master clock to frames / s and kbit/s.
        public static string Format(int source, int mode, uint raw, double mclk_hz)
        {
            Source info = Sources[Math.Max(0, Math.Min(Sources.Length - 1, source))];
            var ci = CultureInfo.InvariantCulture;

            if (mode <= 1)
                return raw.ToString("N0", ci) + " " + info.Unit;

            double ratio = raw / Math.Pow(2, info.Point);

            if (mclk_hz > 0 && source == 4)
                return (ratio * mclk_hz / 4096.0).ToString("N1", ci) + " frames/s";

            if (mclk_hz > 0 && source == 15)
                return (ratio * mclk_hz / 512.0 * 188.0 * 8.0 / 1000.0).ToString("N0", ci) + " kbit/s";

            return ratio.ToString("G4", ci) + " per " + info.Unit.TrimEnd('s');
        }
    }
}
