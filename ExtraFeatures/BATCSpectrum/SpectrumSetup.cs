using System;

namespace opentuner
{
    // The setups of the QO-100 spectrum (#215): where the spectrum comes from.
    public class SpectrumSetup
    {
        public const int Default = 0;       // the BATC server
        // 1 is free: the source for a Tezuka receiver (the PlutoSky R2 source was taken out again, #220), saved settings keep their number
        public const int OtherBridge = 2;   // a bridge on another machine (e.g. a Pi), by URL
        public const int IioDevice = 3;     // an ADI Pluto (AD9361/AD9363/AD9364) read over libiio, the spectrum is calculated by OpenTuner

        public int Mode = Default;
        public string Url = "";             // OtherBridge
        public double ViewMHz = 10496.0;    // IioDevice: QO-100 frequency (RF) in the middle of the view
        public double LnbLoMHz = 9360;      // IioDevice: LNB LO (the receiver LO of the Pluto is ViewMHz - LnbLoMHz)

        // IioDevice
        public string IioUri = "";          // libiio URI of the device: ip:192.168.6.1, usb:..., empty = not set
        public double IioAverageSeconds = 0.5;  // smoothing: time constant of the average of the spectrum over time (the BATC server delivers an averaged spectrum, drawspectrum does not smooth)
        public double IioBlocksPerSecond = 20;  // how often the device is read (blocks of 4 ms); with the average this gives the smoothing, each block is 262 KB over the link
        public int IioSmoothBins = 3;           // smoothing: passes of [1 2 1] over the neighbouring bins, 0 = off (2 passes = 1 4 6 4 1, the ripple of the curve about halved)
        public bool IioShowDbb = true;          // show the level of the signals relative to the beacon (dBb): the ADI receiver is linear and sees the whole beacon
        public double IioGainDb = 20;       // manual gain; the LNB gain is high, the gain of the SDR mainly shifts the level
        public double IioSampleRateMHz = 16;    // 16 MS/s with 16 MHz bandwidth is flat over the 9 MHz of the band
        public double IioBandwidthMHz = 16;

        // not the BATC server: the level of the beacon is not reliable (a part of it may be out of view, the receiver is not
        // linear), so no dBb is shown and no overpower, except for the ADI Pluto (see below)
        public bool External => Mode != Default;

        // the level of the signals relative to the beacon (dBb) is shown: with the BATC server, and with the ADI Pluto unless it is switched
        // off. The other bridge does not show it (its receiver may not be linear and may not see the whole beacon).
        public bool ShowDbb => Mode == Default || (Mode == IioDevice && IioShowDbb);

        // overpower (a signal stronger than the beacon minus 0.75 dB) is evaluated with the BATC server and with the ADI Pluto: its
        // receiver is linear, sees the whole beacon and its levels agree with the BATC server within about 0.2 dB
        public bool Overpower => Mode == Default || Mode == IioDevice;

        public string Name
        {
            get
            {
                switch (Mode)
                {
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
