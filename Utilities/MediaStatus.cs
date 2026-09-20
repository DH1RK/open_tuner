using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace opentuner
{
    public class MediaStatus
    {
        public uint VideoWidth;
        public uint VideoHeight;
        public string VideoCodec;

        public uint AudioRate;
        public uint AudioChannels;
        public string AudioCodec;        

        // The media players report the codec differently (VLC: FourCC like "hevc"/"h264", FFMPEG/MPV:
        // ffmpeg names like "hevc"/"h264"/"mpeg2video"). Shown (Digole, tuner properties) in the standard's proper
        // capitalised short name: AVC (= H.264 = MPEG-4 Part 10), HEVC (= H.265), VVC (= H.266).
        // Unknown names are just upper-cased.
        public static string CodecDisplayName(string codec)
        {
            if (string.IsNullOrWhiteSpace(codec))
                return "";

            string c = codec.Trim().ToLowerInvariant();
            switch (c)
            {
                case "h264": case "avc": case "avc1": case "x264":
                    return "AVC";
                case "hevc": case "h265": case "hev1": case "hvc1": case "x265":
                    return "HEVC";
                case "vvc": case "h266": case "vvc1": case "vvi1":
                    return "VVC";
                case "mpeg2video": case "mpeg2": case "mpgv": case "mp2v":
                    return "MPEG-2";
                case "mpeg4": case "mp4v":
                    return "MPEG-4";
                default:
                    return c.ToUpperInvariant();
            }
        }

    }
}
