using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace opentuner.MediaSources.Minitiouner
{
    public class MinitiounerSettings
    {
        public int Version = 2;

        public byte DefaultInterface = 0;   // 0 = always ask, 0 = FTDI, 2 = PicoTuner, 3 = Ethernet (Future)

        public uint Offset1 = 9750000;
        public uint Offset2 = 9750000;

        public byte DefaultLnbASupply = 0;   // 0 = off, 1 = vert, 2 = horiz
        public byte DefaultLnbBSupply = 0;   // 0 = off, 1 = vert, 2 = horiz
        public bool[] Tone22kHz = new bool[2];   // last used 22kHz tone per tuner (Switches panel: 22K-A/22K-B)

        // Tuning trim per tuner (Frequency tab): derotator capture range in kHz on each side (0 = automatic,
        // 1.5 x symbol rate) and a correction in kHz for the frequency the tuner is really set to
        public uint[] CaptureRangeKHz = new uint[2];
        public int[] FreqCorrectionKHz = new int[2];

        public byte DefaultRFInput = 0;     // 0 = both tuners fed through A, 1 = Tuner1 is A, Tuner2 is B

        public uint[] DefaultVolume = new uint[] { 50, 50 };
        public bool[] DefaultMuted = new bool[] { true, true };

        // Digole status display wired to JP3 ("I2C-NIM") on the NIM I2C bus.
        public bool EnableDigoleDisplay = false;
        public byte DigoleI2cAddress = 0x27;
        // Shown as a greeting screen until the first frequency is tuned on either channel,
        // and again on shutdown (so the display never sits blank between sessions).
        public string DigoleCallsign = "";
        // Optional extra lines for the Welcome/Final screens (empty = line not shown).
        public string DigoleLocator = "";   // Maidenhead locator, e.g. JN48
        public string DigoleName = "";      // operator name

        // EXTERN-0..7 LED outputs (AUX chip GPIO, MiniTiounerPro V2 only) - persisted so the
        // last state is restored on reconnect.
        public bool[] ExternState = new bool[8];
    }
}
