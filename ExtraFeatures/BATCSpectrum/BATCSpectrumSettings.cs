namespace opentuner.ExtraFeatures.BATCSpectrum
{
    // Settings of the BATC spectrum, stored in settings\spectrumSettings.json. AutoTune design and the settings window
    // after DL1RF's version (upstream tomvdb/open_tuner#105).
    public class BATCSpectrumSettings
    {
        public const int ModeManual = 0;
        public const int ModeAutoHold = 1;      // stay on the signal, go to the nearest one after it was lost for the hold time
        public const int ModeAutoNextNew = 2;   // like hold, and after the timed interval to the nearest signal without callsign
        public const int ModeAutoTimed = 3;     // to the next signal (ascending frequency) after the timed interval

        // per tuner (RX 1 to 4)
        public int[] tuneMode = { ModeManual, ModeManual, ModeManual, ModeManual };
        public bool[] avoidBeacon = { true, true, true, true };

        // only signals at least this strong relative to the beacon (dBb) are tuned by AutoTune
        public float threshold = -6.0f;

        public int autoHoldTimeValue = 5;       // seconds
        public int autoTuneTimeValue = 30;      // seconds

        // 0 classic, 1 classic + line, 2 box from top to line, 3 box from line to bottom, 4 line
        public int overPowerIndicatorLayout = 0;

        // ---- the source of the spectrum (#215) ----------------------------------------------------------------------
        // 0 = the BATC server, 1 = local bridge (OpenTuner reads the PlutoSky R2 itself), 2 = other bridge (URL, e.g. on a Pi).
        // spectrumSetup is the setup OpenTuner starts with, spectrumFallback (-1 = none, else 0 to 2) the one it switches to when the first
        // gives no spectrum for a while. A right click beside the bandplan bars chooses a setup for the running session only.
        public int spectrumSetup = 0;
        public int spectrumFallback = -1;
        public string spectrumUrl = "ws://localhost:8096/wb/fft";    // other bridge
        public string plutoHost = "192.168.2.40";                    // local bridge: the PlutoSky R2
        // The view of the PlutoSky R2 is given in QO-100 frequencies (RF): the centre frequency of the receiver (IF) is the view minus
        // the LNB LO. The LO comes from the Source Settings of the connected source (Freq Offset of tuner 1) unless plutoLnbLoMhz is set.
        public double plutoViewMhz = 10496.0;                        // RF frequency in the middle of the view, written at the start, 0 = leave the centre of the Pluto
        public double plutoLnbLoMhz = 0;                             // 0 = from the source (9360 if none is connected)
        public double plutoFrameMs = 250;                            // frame interval of the spectrometer (VFW, 50 to 500 ms) written at the start, 0 = leave it
        public double plutoSpanMhz = 16;                             // span written to the PlutoSky R2 at the start (what 500 kS gives), 0 = leave it
        public double plutoAverageSeconds = 1.0;                     // averaging of the frames
        public bool plutoFollowRate = true;                          // the centre follows the symbol rate of the transmitter
        public double plutoOffsetLowMhz = 2;                         // ... shifted by this for up to 125 kS
        public double plutoOffsetHighMhz = -1.25;                    // ... and by this for 1000 kS and more (1134.75 MHz: the 1000 kS channels are in the middle)
        public bool plutoFlatFloor = true;                           // flat noise floor: the signals are measured against the local noise

        // Vertical range of the spectrum in dB above the noise floor: the floor stays where it is, the signals get more room with a
        // smaller value. The default shows the same as before.
        public const double RangeDefaultDb = 15.6;
        public double rangeDb = RangeDefaultDb;

        public SpectrumSetup BuildSetup(int mode)
        {
            return new SpectrumSetup
            {
                Mode = mode,
                Url = spectrumUrl ?? "",
                PlutoHost = plutoHost ?? "",
                ViewMHz = plutoViewMhz,
                LnbLoMHz = plutoLnbLoMhz,
                SpanMHz = plutoSpanMhz,
                FrameMs = plutoFrameMs,
                AverageSeconds = plutoAverageSeconds,
                FlatFloor = plutoFlatFloor,
                FollowRate = plutoFollowRate,
                OffsetLowMHz = plutoOffsetLowMhz,
                OffsetHighMHz = plutoOffsetHighMhz
            };
        }

        // the setup to fall back to, null = none
        public SpectrumSetup BuildFallback()
        {
            return spectrumFallback >= SpectrumSetup.Default && spectrumFallback <= SpectrumSetup.OtherBridge ? BuildSetup(spectrumFallback) : null;
        }
    }
}
