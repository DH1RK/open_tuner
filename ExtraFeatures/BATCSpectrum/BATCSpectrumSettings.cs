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
        // 0 = the BATC server, 1 = local bridge (OpenTuner reads the PlutoSky R2 itself), 2 = other bridge (URL, e.g. on a Pi),
        // 3 = ADI Pluto (OpenTuner reads an AD9361/AD9363/AD9364 over libiio and calculates the spectrum).
        // spectrumSetup is the setup OpenTuner starts with, spectrumFallback (-1 = none, else 0 to 2) the one it switches to when the first
        // gives no spectrum for a while. A right click beside the bandplan bars chooses a setup for the running session only.
        public int spectrumSetup = 0;
        public int spectrumFallback = -1;
        public string spectrumUrl = "ws://localhost:8096/wb/fft";    // other bridge
        public string plutoSkyHost = "192.168.2.40";                    // local bridge: the PlutoSky R2
        // The view of the PlutoSky R2 is given in QO-100 frequencies (RF): the centre frequency of the receiver (IF) is the view minus
        // the LNB LO. The LO comes from the Source Settings of the connected source (Freq Offset of tuner 1) unless plutoSkyLnbLoMhz is set.
        public double plutoSkyViewMhz = 10496.0;                        // RF frequency in the middle of the view, written at the start, 0 = leave the centre of the PlutoSky
        public double plutoSkyLnbLoMhz = 0;                             // 0 = from the source (9360 if none is connected)
        // Span at the start (16 MHz, what 500 kS gives; the PlutoSky R2 changes it with the symbol rate afterwards), VFW (frame interval of the
        // spectrometer, 250 ms) and the average of the frames (1.0 s) of the PlutoSky R2 are fixed in SpectrumSetup, not settings any more.
        public bool plutoSkyFollowRate = true;                          // the centre follows the symbol rate of the transmitter
        public double plutoSkyOffsetLowMhz = 2;                         // ... shifted by this for up to 125 kS
        public double plutoSkyOffsetHighMhz = -1.25;                    // ... and by this for 1000 kS and more (1134.75 MHz: the 1000 kS channels are in the middle)
        public bool plutoSkyFlatFloor = true;                           // flat noise floor: the signals are measured against the local noise

        // ---- the ADI Pluto (setup 3, libiio). The view (plutoSkyViewMhz), the LNB LO (plutoSkyLnbLoMhz) and the flat floor above are
        // used by it as well. Set in the file for now (there are no fields for it in the settings window yet). ----
        public string iioUri = "";                                      // libiio URI: ip:192.168.6.1, usb:..., empty = not set
        public double iioAverageSeconds = 0.6;                          // smoothing: time constant of the average of the spectrum over time (0 to 10 s, 0 = off)
        public double iioBlocksPerSecond = 30;                          // how often the device is read, 2 to 60 (4 ms of signal and 262 KB over the link each)
        public int iioSmoothBins = 2;                                   // smoothing: passes of [1 2 1] over the neighbouring bins (0 to 3, 0 = off; 2 = 1 4 6 4 1)
        public bool iioShowDbb = true;                                  // show dBb (level relative to the beacon) with this source
        public double iioGainDb = 20;                                   // manual gain of the receiver
        public double iioSampleRateMhz = 16;                            // sampling rate; 16 MS/s with 16 MHz bandwidth is flat over the 9 MHz of the band
        public double iioBandwidthMhz = 16;                             // RF bandwidth of the receiver

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
                PlutoSkyHost = plutoSkyHost ?? "",
                ViewMHz = plutoSkyViewMhz,
                LnbLoMHz = plutoSkyLnbLoMhz,
                FlatFloor = plutoSkyFlatFloor,
                FollowRate = plutoSkyFollowRate,
                OffsetLowMHz = plutoSkyOffsetLowMhz,
                OffsetHighMHz = plutoSkyOffsetHighMhz,
                IioUri = iioUri ?? "",
                IioAverageSeconds = iioAverageSeconds,
                IioBlocksPerSecond = iioBlocksPerSecond,
                IioSmoothBins = System.Math.Max(0, System.Math.Min(3, iioSmoothBins)),
                IioShowDbb = iioShowDbb,
                IioGainDb = iioGainDb,
                IioSampleRateMHz = iioSampleRateMhz,
                IioBandwidthMHz = iioBandwidthMhz
            };
        }

        // the setup to fall back to, null = none
        public SpectrumSetup BuildFallback()
        {
            return spectrumFallback >= SpectrumSetup.Default && spectrumFallback <= SpectrumSetup.IioDevice ? BuildSetup(spectrumFallback) : null;
        }
    }
}
