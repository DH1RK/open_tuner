namespace opentuner.MediaSources
{
    public class OTSourceData
    {
        public int video_number = 0;
        public bool demod_locked = false;
        public string demode_state = "";
        public double db_margin = 0.0;
        public double mer = 0.0;
        public long frequency = 0;
        public int symbol_rate = 0;
        public string service_name = "";
        public string modcode = "";
        public string video_codec = "";   // as the media player reports it, see MediaStatus.CodecDisplayName
        public int volume = 0;
        public bool streaming = false;
        public bool streaming_steady = false;   // the UDP stream is a steady one (stays on without a signal): the "U" of the info line is green
        public bool recording = false;
        public bool pid_manual = false;         // the PID choice of the tuner is manual (only the typed in video/audio PID reach the player): the red "P" of the info line
        public bool fec_manual = false;        // the FEC mode of the tuner is Manual: the red "M" of the info line (does not fit every stream)

        // TS errors over the last few seconds (issue #19), see TSHealth
        public uint ts_cc_errors = 0;
        public uint ts_tei_errors = 0;
        public uint ts_sync_losses = 0;
        public uint ts_scrambled = 0;
        public uint ts_buffer_overflows = 0;
    }
}
