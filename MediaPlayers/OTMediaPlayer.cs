using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace opentuner.MediaPlayers
{
    public abstract class OTMediaPlayer
    {
        public abstract int getID();
        public abstract event EventHandler<MediaStatus> onVideoOut;
        public abstract void Initialize(CircularBuffer TSDataQueue);
        public abstract void Initialize(CircularBuffer TSDataQueue, int ID);

        public abstract void Close();
        public abstract void Play();
        public abstract void Stop();
        public abstract void SnapShot(string FileName);
        public abstract void SetVolume(int Volume);
        public abstract int GetVolume();

        // Audio balance of this player, -100 (left ear only) .. 0 (centre, both ears) .. +100 (right ear only), see AudioBalance (#257).
        // A player without a way to pan keeps the default: it plays in the centre.
        public virtual bool SupportsBalance { get { return false; } }
        public virtual void SetBalance(int Balance) { }
        public virtual int GetBalance() { return 0; }

        public abstract string GetName();
    }
}
