using System;

namespace opentuner
{
    // The three setups of the QO-100 spectrum (#215): where the spectrum comes from.
    public class SpectrumSetup
    {
        public const int Default = 0;       // the BATC server
        public const int LocalBridge = 1;   // the PlutoSky R2, read by OpenTuner itself
        public const int OtherBridge = 2;   // a bridge on another machine (e.g. a Pi), by URL

        public int Mode = Default;
        public string Url = "";             // OtherBridge
        public string PlutoHost = "";       // LocalBridge
        public double ViewMHz = 10496.0;    // LocalBridge: QO-100 frequency (RF) in the middle of the view, 0 = leave the centre of the PlutoSky R2
        public double LnbLoMHz = 9360;      // LocalBridge: LNB LO (the centre of the PlutoSky R2 is ViewMHz - LnbLoMHz)
        public double SpanMHz = 16;         // LocalBridge: span written to the PlutoSky R2 (16 MHz is what 500 kS gives), 0 = leave it
        public double FrameMs = 250;        // LocalBridge: frame interval of the spectrometer (VFW of the dashboard, 50 to 500 ms), 0 = leave it
        public double AverageSeconds = 1.0; // LocalBridge
        public bool FollowRate = true;      // LocalBridge: the centre follows the symbol rate of the transmitter (dt/pluto/<call>/tx/dvbs2/sr)
        public double OffsetLowMHz = 2;     // centre shift for symbol rates up to 125 kS (the span is only 4 to 6 MHz there)
        public double OffsetHighMHz = -1.25; // centre shift for symbol rates of 1000 kS and more (1134.75 MHz: the 1000 kS channels are in the middle)
        public bool FlatFloor = true;       // LocalBridge: the noise floor is made flat (the signals are measured against the local noise)

        // not the BATC server: the level of the beacon is not reliable (a part of it may be out of view, the receiver is not
        // linear), so no dBb is shown and there is no overpower
        public bool External => Mode != Default;

        public string Name
        {
            get
            {
                switch (Mode)
                {
                    case LocalBridge: return "Bridge PC (PlutoSky R2 " + PlutoHost + ")";
                    case OtherBridge: return "Bridge (" + Url + ")";
                    default: return "BATC";
                }
            }
        }

        internal SpectrumFeed CreateFeed()
        {
            switch (Mode)
            {
                case LocalBridge: return new PlutoSpectrumFeed(PlutoHost, ViewMHz, LnbLoMHz, SpanMHz, FrameMs, AverageSeconds, FlatFloor, FollowRate, OffsetLowMHz, OffsetHighMHz);
                case OtherBridge: return new socket(Url);
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
                    return PlutoHost == other.PlutoHost && ViewMHz == other.ViewMHz && LnbLoMHz == other.LnbLoMHz && SpanMHz == other.SpanMHz && FrameMs == other.FrameMs && AverageSeconds == other.AverageSeconds && FlatFloor == other.FlatFloor
                           && FollowRate == other.FollowRate && OffsetLowMHz == other.OffsetLowMHz && OffsetHighMHz == other.OffsetHighMHz;
                case OtherBridge:
                    return Url == other.Url;
                default:
                    return true;
            }
        }
    }
}
