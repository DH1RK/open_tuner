using System;

namespace opentuner.MediaSources.Minitiouner
{
    // The choice of the manual FEC mode (Special tab, as MiniTioune's "Manual FEC" window): one modulation, one code rate, long or short frames,
    // pilots on or off. With FEC mode Manual only this MODCOD is left in the demodulator's MODCOD list (STV0910 MODCODLST1..F, "1 = prohibited"),
    // as measured with an I2C capture of MiniTioune (D:\Logging\Logic2\ManualFecMode.txt, QPSK 1/4 to 9/10, long, pilots on and off, short):
    //   - only MODCODLST1..F are written (MODCODLST0 stays as it is), DMDMODCOD (MANUAL_MODCOD) is not used,
    //   - every bit is set except the chosen one; the reserved bits of the 9/10 entries keep MiniTioune's values (BaseList),
    //   - the list is written with every tune and again after the TS reset.
    // Mapping (data sheet and capture): MODCOD number m (DVB-S2, QPSK 1/4 = 1 ... 32APSK 9/10 = 28) sits in MODCODLST(15 - m / 2), the lower nibble
    // for an even m, the upper one for an odd m; bit 0 = long without pilots, 1 = long with pilots, 2 = short without pilots, 3 = short with pilots.
    // The 9/10 entries have the two bits of the long frames only (bits 4 and 5 of their nibble).
    public sealed class ManualFec
    {
        public const int ModulationCount = 4;
        public static readonly string[] Modulations = { "QPSK", "8PSK", "16APSK", "32APSK" };

        private static readonly string[][] RateTexts =
        {
            new[] { "1/4", "1/3", "2/5", "1/2", "3/5", "2/3", "3/4", "4/5", "5/6", "8/9", "9/10" },
            new[] { "3/5", "2/3", "3/4", "5/6", "8/9", "9/10" },
            new[] { "2/3", "3/4", "4/5", "5/6", "8/9", "9/10" },
            new[] { "3/4", "4/5", "5/6", "8/9", "9/10" },
        };

        // MODCOD number of the first rate of every modulation
        private static readonly int[] FirstModcod = { 1, 12, 18, 24 };

        // MiniTioune's list in the manual mode with nothing allowed (MODCODLST1..F), measured
        private static readonly byte[] BaseList = { 0xF7, 0xFF, 0xFF, 0x3F, 0xFF, 0xFF, 0xBF, 0xFF, 0xFF, 0xBF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

        // The list MiniTioune writes when FEC mode goes back to Auto (MODCODLST1..F, capture ManualFecMode1.txt): FC, 13 x 00, 0F. The reserved bits of
        // MODCODLST7 and A are 0 here, the register table of the chip setup has C0.
        public static readonly byte[] AutoList = { 0xFC, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0F };

        public int Modulation = 0;   // 0 = QPSK ... 3 = 32APSK
        public int Rate = 7;         // index into the rates of the modulation (QPSK 4/5)
        public bool Short = false;   // false = long frames (64800 bits), true = short frames
        public bool Pilots = false;

        public static string[] Rates(int modulation)
        {
            return RateTexts[Clamp(modulation, 0, ModulationCount - 1)];
        }

        // 9/10 exists with long frames only
        public static bool IsNineTenth(int modulation, int rate)
        {
            string[] rates = Rates(modulation);
            return rate >= 0 && rate < rates.Length && rates[rate] == "9/10";
        }

        public static ManualFec Default()
        {
            return new ManualFec();
        }

        public static ManualFec From(int modulation, int rate, bool is_short, bool pilots)
        {
            var fec = new ManualFec();
            fec.Modulation = Clamp(modulation, 0, ModulationCount - 1);
            fec.Rate = Clamp(rate, 0, Rates(fec.Modulation).Length - 1);
            fec.Short = is_short && !IsNineTenth(fec.Modulation, fec.Rate);
            fec.Pilots = pilots;
            return fec;
        }

        public override string ToString()
        {
            return Modulations[Modulation] + " " + Rates(Modulation)[Rate] + (Short ? " short" : " long") + (Pilots ? " pilots on" : " pilots off");
        }

        // MODCODLST1..F (15 bytes) with only the chosen MODCOD allowed
        public byte[] ToList()
        {
            byte[] list = (byte[])BaseList.Clone();
            int modcod = FirstModcod[Modulation] + Rate;
            int index = 15 - modcod / 2;                    // MODCODLSTx
            int shift = (modcod & 1) == 1 ? 4 : 0;
            int bit = (Short ? 2 : 0) + (Pilots ? 1 : 0);   // within the nibble

            list[index - 1] &= (byte)~(1 << (shift + bit));
            return list;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
