using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace opentuner
{
    public class TunerConfig
    {
        // tuner / demod specific

        public byte tuner { get; set; }

        public UInt32 frequency { get; set; }
        public UInt32 symbol_rate { get; set; }
        public uint rf_input { get; set; }

        // tuning trim (Frequency tab): capture range of the derotator in kHz on each side (0 = automatic,
        // 1.5 x symbol rate) and a correction in kHz added to the frequency the tuner is really set to
        public uint capture_range_khz { get; set; }
        public int freq_correction_khz { get; set; }

        // misc 
        
        public byte lnba_psu { get; set; }
        public byte lnbb_psu { get; set; }

        public bool tone_22kHz_P1 { get; set; }

        public override string ToString() 
        {
            return "Config: Freq: " + frequency.ToString() + "," + symbol_rate.ToString() + " - LNB Supply :" + lnba_psu.ToString() + "," + lnbb_psu.ToString()  + ", RF Input : " + rf_input.ToString() + ", Capture: " + capture_range_khz.ToString() + " kHz, Correction: " + freq_correction_khz.ToString() + " kHz";
        }

    }
}
