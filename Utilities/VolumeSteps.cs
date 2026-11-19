using System;

namespace opentuner.Utilities
{
    // The steps of the volume (0 to 200, shown as %) for the mouse wheel on the video and on the Volume slider of the Properties (#254):
    // 1 % up to 20 %, above that 5 %. A loud station is already loud at 10 %, so the low end needs fine steps. Whatever the volume is
    // (a value set by the slider or by a Stream Deck step of 3), the next step lands on the scale: 22 -> 25, 21 -> 20.
    public static class VolumeSteps
    {
        public const int FineLimit = 20;
        public const int Maximum = 200;

        public static int Up(int volume)
        {
            int next = volume < FineLimit ? volume + 1 : (volume / 5 + 1) * 5;
            return Math.Min(Maximum, next);
        }

        public static int Down(int volume)
        {
            int next = volume <= FineLimit ? volume - 1 : ((volume - 1) / 5) * 5;
            return Math.Max(0, next);
        }
    }
}
