using System;

namespace opentuner.MediaPlayers
{
    // The audio balance of a tuner (#257): -100 = left ear only, 0 = centre (both ears), +100 = right ear only.
    // Two tuners can be listened to at the same time, one in each ear.
    public static class AudioBalance
    {
        public const int Left = -100;
        public const int Centre = 0;
        public const int Right = 100;

        public static int Clamp(int balance)
        {
            return Math.Max(Left, Math.Min(Right, balance));
        }

        // Short text for the user: "C", "L 40", "R 100".
        public static string ToText(int balance)
        {
            balance = Clamp(balance);
            if (balance == 0)
                return "C";

            return (balance < 0 ? "L " : "R ") + Math.Abs(balance).ToString();
        }

        // The audio filter for mpv ("af" option): upmixes to stereo and mixes as Matrix() does. Empty for the centre (no filter, the level stays as it is).
        public static string MpvFilter(int balance)
        {
            balance = Clamp(balance);
            if (balance == 0)
                return "";

            float[] m = Matrix(balance, 2, 2);
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return "lavfi=[aformat=channel_layouts=stereo,pan=stereo|c0=" + m[0].ToString("0.###", c) + "*c0+" + m[1].ToString("0.###", c) +
                   "*c1|c1=" + m[2].ToString("0.###", c) + "*c0+" + m[3].ToString("0.###", c) + "*c1]";
        }

        // The mixing matrix for an output with two (or more) channels, as XAudio2 wants it: levelMatrix[destination * sourceChannels + source].
        // Towards one side the channel of the other side fades out and is mixed into this side, so a stereo signal ends as
        // mono on this side, a mono signal (the same on both channels) keeps its level on this side. Only the first two output channels are used.
        public static float[] Matrix(int balance, int sourceChannels, int destinationChannels)
        {
            float[] matrix = new float[sourceChannels * destinationChannels];
            float t = Math.Abs(Clamp(balance)) / 100f;
            bool towards_left = balance < 0;

            // the share of every source channel in the left [0] and right [1] output
            float[,] gain = new float[2, 2];
            if (towards_left)
            {
                gain[0, 0] = 1f / (1f + t); gain[0, 1] = t / (1f + t);
                gain[1, 0] = 0f;            gain[1, 1] = 1f - t;
            }
            else
            {
                gain[0, 0] = 1f - t;        gain[0, 1] = 0f;
                gain[1, 0] = t / (1f + t);  gain[1, 1] = 1f / (1f + t);
            }

            for (int dst = 0; dst < Math.Min(2, destinationChannels); dst++)
            {
                if (sourceChannels == 1)
                {
                    // one source channel is the same in both ears: what the stereo case adds up
                    matrix[dst * sourceChannels] = gain[dst, 0] + gain[dst, 1];
                }
                else
                {
                    for (int src = 0; src < Math.Min(2, sourceChannels); src++)
                        matrix[dst * sourceChannels + src] = gain[dst, src];
                }
            }

            return matrix;
        }
    }
}
