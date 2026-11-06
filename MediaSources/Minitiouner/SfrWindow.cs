using System;

namespace opentuner.MediaSources.Minitiouner
{
    // The symbol rate window of the demodulator (SFRLOW .. SFRUP, around SFRINIT) as MiniTioune sets it, measured with an I2C capture at 333 kS and at
    // 1500 kS (MCLK 135 MHz): Wide scan off +-5 %, on 0.5 x to 1.5 x of the symbol rate. The window is always manual (TMGCFG3 = 0x00).
    // The limits are calculated from the symbol rate itself, not from the rounded SFRINIT: 1500 kS -> SFR 728.18: off 765 / 692, on 1092 / 364;
    // 333 kS -> SFR 161.66: off 170 / 154, on 242 / 81.
    public static class SfrWindow
    {
        public const double NarrowUp = 1.05;
        public const double NarrowLow = 0.95;
        public const double WideUp = 1.5;
        public const double WideLow = 0.5;

        private const int MinLow = 32;       // the timing loop minimum is MCLK / 2048 whatever the clock: 65536 / 2048 units
        private const int MaxUp = 0x7FFF;    // 15 bit: SFRUP1 bits 6..0 and SFRUP0 (bit 7 of SFRUP1 is the flag for the automatic limit)

        // sr_kS: symbol rate in kS, mclk_hz: master clock. up / low in the units of the SFR registers.
        public static void Compute(uint sr_kS, double mclk_hz, bool wide, out int up, out int low)
        {
            double sfr = sr_kS * 1000.0 * 65536.0 / mclk_hz;
            int init = Math.Max(MinLow, (int)Math.Round(sfr));   // as the low symbol rate setup: SFRINIT is not below the minimum either

            up = (int)Math.Round(sfr * (wide ? WideUp : NarrowUp));
            low = (int)Math.Round(sfr * (wide ? WideLow : NarrowLow));

            // the window has to contain SFRINIT and stay inside what the chip can do
            up = Math.Min(MaxUp, Math.Max(init + 1, up));
            low = Math.Max(MinLow, Math.Min(init - 1, low));
        }
    }
}
