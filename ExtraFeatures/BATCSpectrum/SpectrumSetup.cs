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
        public double CenterMHz = 1136;     // LocalBridge: centre frequency (IF) written to the Pluto, 0 = leave it
        public double SpanMHz = 16;         // LocalBridge: span written to the PlutoSky R2 (16 MHz is what 500 kS gives), 0 = leave it
        public double FrameMs = 250;        // LocalBridge: frame interval of the spectrometer (VFW of the dashboard, 50 to 500 ms), 0 = leave it
        public double AverageSeconds = 1.0; // LocalBridge

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
                case LocalBridge: return new PlutoSpectrumFeed(PlutoHost, CenterMHz, SpanMHz, FrameMs, AverageSeconds);
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
                    return PlutoHost == other.PlutoHost && CenterMHz == other.CenterMHz && SpanMHz == other.SpanMHz && FrameMs == other.FrameMs && AverageSeconds == other.AverageSeconds;
                case OtherBridge:
                    return Url == other.Url;
                default:
                    return true;
            }
        }
    }
}
