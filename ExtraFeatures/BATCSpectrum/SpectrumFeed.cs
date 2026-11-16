using System;

namespace opentuner
{
    // A source of spectrum frames in the BATC format (922 values of 16 bit over 10490.5 - 10499.5 MHz): the BATC server
    // (socket), an ADI Pluto read over libiio (IioSpectrumFeed) or a bridge on another machine (socket with its URL).
    abstract class SpectrumFeed
    {
        public Action<ushort[]> callback;

        public bool connected;

        public DateTime lastdata;

        public event EventHandler<bool> ConnectionStatusChanged;

        protected void RaiseConnectionStatusChanged(bool is_connected)
        {
            ConnectionStatusChanged?.Invoke(this, is_connected);
        }

        // what the feed connects to, for the message when it is not reachable
        public abstract string Description { get; }

        public abstract void start();

        // only acts while connected (the watchdog calls it when no data comes any more)
        public abstract void stop();

        // closes for good (another setup is chosen, or OpenTuner closes)
        public virtual void Shutdown()
        {
            stop();
        }
    }
}
