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

        // "1280 x 720", empty while the size is not known (0 x 0)
        public string ResolutionText()
        {
            return VideoWidth > 0 && VideoHeight > 0 ? VideoWidth + " x " + VideoHeight : "";
        }

        // The media players report the codec differently (VLC: FourCC like "hevc"/"h264", FFMPEG: ffmpeg names like
        // "hevc"/"h264"/"mpeg2video", MPV: sometimes a description like "H.264 / AVC / MPEG-4 AVC / MPEG-4").
        // Shown everywhere (tuner properties, info line, Digole, signal report, as the TS Info tab) with one name:
        // H.264 (= AVC), H.265 (= HEVC), H.266 (= VVC), MPEG-2, MPEG-4. Unknown names are just upper-cased.
        public static string CodecDisplayName(string codec)
        {
            if (string.IsNullOrWhiteSpace(codec))
                return "";

            string c = codec.Trim().ToLowerInvariant();
            switch (c)
            {
                case "h264": case "h.264": case "avc": case "avc1": case "x264":
                    return "H.264";
                case "hevc": case "h265": case "h.265": case "hev1": case "hvc1": case "x265":
                    return "H.265";
                case "vvc": case "h266": case "h.266": case "vvc1": case "vvi1":
                    return "H.266";
                case "mpeg2video": case "mpeg2": case "mpgv": case "mp2v":
                    return "MPEG-2";
                case "mpeg4": case "mp4v":
                    return "MPEG-4";
            }

            // a description (mpv "video-codec"): the newer standards first, "MPEG-4" is also part of the H.264 text
            if (c.Contains("h.264") || c.Contains("h264") || c.Contains("avc"))
                return "H.264";
            if (c.Contains("h.265") || c.Contains("h265") || c.Contains("hevc"))
                return "H.265";
            if (c.Contains("h.266") || c.Contains("h266") || c.Contains("vvc"))
                return "H.266";

            return c.ToUpperInvariant();
        }

        // The same for the audio codec (VLC: FourCC like "mpga"/"mp4a", FFMPEG/MPV: ffmpeg names like "mp2"/"aac_latm").
        // "mpga" is VLC's name for MPEG audio of any layer; it is shown as MP2, the audio of the DVB-S beacons.
        public static string AudioCodecDisplayName(string codec)
        {
            if (string.IsNullOrWhiteSpace(codec))
                return "";

            string c = codec.Trim().ToLowerInvariant();
            switch (c)
            {
                case "mp2": case "mp2a": case "mpga":
                    return "MP2";
                case "mp3": case ".mp3":
                    return "MP3";
                case "aac": case "aac_latm": case "mp4a": case "latm": case "aac_fixed":
                    return "AAC";
                case "ac3": case "a52": case "a52b":
                    return "AC-3";
                case "eac3": case "ec-3": case "eac-3":
                    return "E-AC-3";
                default:
                    return c.ToUpperInvariant();
            }
        }

    }
}
