using System;

namespace opentuner
{
    // The three setups of the QO-100 spectrum (#215): where the spectrum comes from.
    public class SpectrumSetup
    {
        public const int Default = 0;       // the BATC server
        public const int LocalBridge = 1;   // the PlutoSky R2, read by OpenTuner itself
        public const int OtherBridge = 2;   // a bridge on another machine (e.g. a Pi), by URL
        public const int IioDevice = 3;     // an ADI Pluto (AD9361/AD9363/AD9364) read over libiio, the spectrum is calculated by OpenTuner

        public int Mode = Default;
        public string Url = "";             // OtherBridge
        public string PlutoSkyHost = "";       // LocalBridge
        public double ViewMHz = 10496.0;    // LocalBridge: QO-100 frequency (RF) in the middle of the view, 0 = leave the centre of the PlutoSky R2
        public double LnbLoMHz = 9360;      // LocalBridge: LNB LO (the centre of the PlutoSky R2 is ViewMHz - LnbLoMHz)
        // LocalBridge (PlutoSky R2): fixed values, no settings (the R2 changes the span with the symbol rate by itself)
        public double SpanMHz = 16;         // span written to the PlutoSky R2 at the start (16 MHz is what 500 kS gives), 0 = leave it
        public double FrameMs = 250;        // frame interval of the spectrometer (VFW of the dashboard; at 16 MHz the spectrometer takes 4 frames/s)
        public double AverageSeconds = 1.0; // average of the frames
        public bool FollowRate = true;      // LocalBridge: the centre follows the symbol rate of the transmitter (dt/pluto/<call>/tx/dvbs2/sr)
        public double OffsetLowMHz = 2;     // centre shift for symbol rates up to 125 kS (the span is only 4 to 6 MHz there)
        public double OffsetHighMHz = -1.25; // centre shift for symbol rates of 1000 kS and more (1134.75 MHz: the 1000 kS channels are in the middle)
        public bool FlatFloor = true;       // LocalBridge (not the ADI Pluto, it is measured without): the noise floor is made flat (the signals are measured against the local noise)

        // IioDevice (ViewMHz, LnbLoMHz and FlatFloor above are used as well)
        public string IioUri = "";          // libiio URI of the device: ip:192.168.6.1, usb:..., empty = not set
        public double IioAverageSeconds = 0.5;  // smoothing: time constant of the average of the spectrum over time (the BATC server delivers an averaged spectrum, drawspectrum does not smooth)
        public double IioBlocksPerSecond = 20;  // how often the device is read (blocks of 4 ms); with the average this gives the smoothing, each block is 262 KB over the link
        public int IioSmoothBins = 3;           // smoothing: passes of [1 2 1] over the neighbouring bins, 0 = off (2 passes = 1 4 6 4 1, the ripple of the curve about halved)
        public bool IioShowDbb = true;          // show the level of the signals relative to the beacon (dBb): the ADI receiver is linear and sees the whole beacon
        public double IioGainDb = 20;       // manual gain; the LNB gain is high, the gain of the SDR mainly shifts the level
        public double IioSampleRateMHz = 16;    // 16 MS/s with 16 MHz bandwidth is flat over the 9 MHz of the band
        public double IioBandwidthMHz = 16;

        // not the BATC server: the level of the beacon is not reliable (a part of it may be out of view, the receiver is not
        // linear), so no dBb is shown and there is no overpower
        public bool External => Mode != Default;

        // the level of the signals relative to the beacon (dBb) is shown: with the BATC server, and with the ADI Pluto unless it is switched
        // off. The other bridges do not show it (the PlutoSky is not linear and may not see the whole beacon); overpower is only evaluated
        // with the BATC server.
        public bool ShowDbb => Mode == Default || (Mode == IioDevice && IioShowDbb);

        public string Name
        {
            get
            {
                switch (Mode)
                {
                    case LocalBridge: return "Bridge PC (PlutoSky R2 " + PlutoSkyHost + ")";
                    case OtherBridge: return "Bridge (" + Url + ")";
                    case IioDevice: return "ADI Pluto (" + (string.IsNullOrWhiteSpace(IioUri) ? "no address" : IioUri) + ")";
                    default: return "BATC";
                }
            }
        }

        internal SpectrumFeed CreateFeed()
        {
            switch (Mode)
            {
                case LocalBridge: return new PlutoSkySpectrumFeed(PlutoSkyHost, ViewMHz, LnbLoMHz, SpanMHz, FrameMs, AverageSeconds, FlatFloor, FollowRate, OffsetLowMHz, OffsetHighMHz);
                case OtherBridge: return new socket(Url);
                case IioDevice: return new IioSpectrumFeed(IioUri, ViewMHz, LnbLoMHz, IioSampleRateMHz, IioBandwidthMHz, IioGainDb, IioAverageSeconds, false, IioSmoothBins, IioBlocksPerSecond);
                default: return new socket();
            }
        }

        // the same source: nothing has to be reconnected when only the name or a flag differs
        public bool SameSourceAs(SpectrumSetup other)
        {
            if (other == null || Mode != other.Mode)
                return false;
            switch (Mode)
            {
                case LocalBridge:
                    return PlutoSkyHost == other.PlutoSkyHost && ViewMHz == other.ViewMHz && LnbLoMHz == other.LnbLoMHz && SpanMHz == other.SpanMHz && FrameMs == other.FrameMs && AverageSeconds == other.AverageSeconds && FlatFloor == other.FlatFloor
                           && FollowRate == other.FollowRate && OffsetLowMHz == other.OffsetLowMHz && OffsetHighMHz == other.OffsetHighMHz;
                case OtherBridge:
                    return Url == other.Url;
                case IioDevice:
                    return IioUri == other.IioUri && ViewMHz == other.ViewMHz && LnbLoMHz == other.LnbLoMHz && IioSampleRateMHz == other.IioSampleRateMHz && IioBandwidthMHz == other.IioBandwidthMHz
                           && IioGainDb == other.IioGainDb && IioAverageSeconds == other.IioAverageSeconds && IioSmoothBins == other.IioSmoothBins && IioBlocksPerSecond == other.IioBlocksPerSecond;
                default:
                    return true;
            }
        }
    }
}
