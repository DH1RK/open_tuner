using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Serilog;

namespace opentuner.MediaPlayers.VLC
{
    public class VLCMediaPlayer : OTMediaPlayer
    {
        LibVLC libVLC = new LibVLC("--aout=directsound");
        Media media;
        TSStreamMediaInput media_input;
        LibVLCSharp.WinForms.VideoView videoView;

        MediaPlayer _mediaplayer;

        int player_volume;

        public override event EventHandler<MediaStatus> onVideoOut;

        int _id = 0;

        CircularBuffer ts_data_queue;
        public VLCMediaPlayer(LibVLCSharp.WinForms.VideoView VideoView)
        {
            libVLC.Log += LibVLC_Log;
            videoView = VideoView;
        }

        private void LibVLC_Log(object sender, LogEventArgs e)
        {
            //Log.Information("VLCMediaPlayer: " + e.FormattedLog);
        }

        public override string GetName()
        {
            return "VLC";
        }

        // update mediaplayer reference invoking if required
        private delegate void updateMediaPlayerDelegate(MediaPlayer newPlayer, bool play);

        private void updateVideoPlayer(MediaPlayer newPlayer, bool play)
        {
            if (videoView.InvokeRequired)
            {
                updateMediaPlayerDelegate ump = new updateMediaPlayerDelegate(updateVideoPlayer);

                videoView.Invoke(ump, new object[] { newPlayer, play });
            }
            else
            {
                videoView.MediaPlayer = newPlayer;

                if (play)
                {
                    Thread.Sleep(10);
                    Log.Information("HWND: " + videoView.MediaPlayer.Hwnd.ToString());
                    videoView.MediaPlayer.Play(media);
                }
            }
        }




        private void MediaPlayer_EncounteredError(object sender, EventArgs e)
        {
            Log.Information("VLC: Error: " + libVLC.LastLibVLCError);
        }

        private void MediaPlayer_Playing(object sender, EventArgs e)
        {
            Log.Information("VLC: Playing ");
        }

        private void MediaPlayer_Stopped(object sender, EventArgs e)
        {
            Log.Information("VLC: Stopped");
        }
        

        public override void Initialize(CircularBuffer TSDataQueue)
        {
            ts_data_queue = TSDataQueue;

            _mediaplayer = new MediaPlayer(libVLC);
            _mediaplayer.Stopped += MediaPlayer_Stopped;
            _mediaplayer.Playing += MediaPlayer_Playing;
            _mediaplayer.EncounteredError += MediaPlayer_EncounteredError;
            _mediaplayer.Vout += MediaPlayer_Vout;

            _mediaplayer.EnableMouseInput = false;
            _mediaplayer.EnableKeyInput = false;

            _mediaplayer.SetMarqueeInt(VideoMarqueeOption.Size, 20);
            _mediaplayer.SetMarqueeInt(VideoMarqueeOption.X, 10);
            _mediaplayer.SetMarqueeInt(VideoMarqueeOption.Y, 10);

            media_input = new TSStreamMediaInput(ts_data_queue);
            media = new Media(libVLC, media_input);

            MediaConfiguration media_config = new MediaConfiguration();
            media_config.EnableHardwareDecoding = false;
            media.AddOption(media_config);

        }

        // LibVLC's own CodecDescription() returns a verbose human-readable string (e.g.
        // "MPEG-H Part2/HEVC (H.265)"), unlike the short codec names FFMPEG/MPV report (e.g.
        // "hevc") - decode the raw FourCC into its 4-char mnemonic instead, for consistency
        // across all three media player backends.
        private static string FourCCToShortName(uint fourcc)
        {
            char[] chars = new char[4];
            for (int i = 0; i < 4; i++)
            {
                chars[i] = (char)((fourcc >> (8 * i)) & 0xFF);
            }
            return new string(chars).Trim().ToUpperInvariant();
        }

        private void MediaPlayer_Vout(object sender, MediaPlayerVoutEventArgs e)
        {
            if (videoView.MediaPlayer == null)
                return;

            // volume changes only take affect when media is playing
            videoView.MediaPlayer.Volume = player_volume;

            MediaStatus media_status = ReadMediaStatus();
            Log.Information("VLC : size at Vout " + media_status.VideoWidth + " x " + media_status.VideoHeight);

            if (onVideoOut != null)
            {
                onVideoOut(this, media_status);
            }

            if (media_status.VideoWidth == 0 || media_status.VideoHeight == 0)
                ReportSizeWhenKnown(media);
        }

        private MediaStatus ReadMediaStatus()
        {
            MediaStatus media_status = new MediaStatus();

            foreach (var track in media.Tracks)
            {
                switch (track.TrackType)
                {
                    case TrackType.Audio:
                        media_status.AudioChannels = track.Data.Audio.Channels;
                        media_status.AudioCodec = FourCCToShortName(track.Codec);
                        media_status.AudioRate = track.Data.Audio.Rate;
                        break;
                    case TrackType.Video:
                        media_status.VideoCodec = FourCCToShortName(track.Codec);
                        media_status.VideoWidth = track.Data.Video.Width;
                        media_status.VideoHeight = track.Data.Video.Height;
                        break;
                }
            }

            // the track of a stream that is read from a callback often has no size, the video output knows it
            if ((media_status.VideoWidth == 0 || media_status.VideoHeight == 0) && videoView.MediaPlayer != null)
            {
                uint width = 0, height = 0;
                if (videoView.MediaPlayer.Size(0, ref width, ref height))
                {
                    media_status.VideoWidth = width;
                    media_status.VideoHeight = height;
                }
            }

            return media_status;
        }

        // The size is not known yet at the Vout event (0 x 0): ask again until it is there and report once more.
        // Stops when this media is replaced or the player is gone.
        private void ReportSizeWhenKnown(Media started_media)
        {
            Task.Run(() =>
            {
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < 15000)
                    {
                        System.Threading.Thread.Sleep(250);

                        if (videoView.MediaPlayer == null || !ReferenceEquals(started_media, media))
                            return;

                        MediaStatus media_status = ReadMediaStatus();
                        if (media_status.VideoWidth > 0 && media_status.VideoHeight > 0)
                        {
                            Log.Information("VLC : size known after " + watch.ElapsedMilliseconds + " ms: " + media_status.VideoWidth + " x " + media_status.VideoHeight);
                            onVideoOut?.Invoke(this, media_status);
                            return;
                        }
                    }

                    Log.Information("VLC : size still unknown after 15 s");
                }
                catch (Exception ex)
                {
                    Log.Information("VLC : size check ended: " + ex.Message);
                }
            });
        }

        public override void SnapShot(string FileName)
        {
            videoView.MediaPlayer.TakeSnapshot(0, FileName, 0, 0);
        }


        public override void Close()
        {
            if (media_input != null)
                media_input.Dispose();
            if (media != null)
                media.Dispose();
        }

        public override void Stop()
        {
            media_input.end = true;

            Log.Information("VLC: Stop Command");

            if (_mediaplayer != null)
                _mediaplayer.Dispose();
            _mediaplayer = null;
            GC.Collect();
            GC.Collect();

            updateVideoPlayer(null, false);
        }

        public override void Play()
        {
            ts_data_queue.Clear();

            media_input.ts_sync = false;
            media_input.end = false;

            Stop();

            Log.Information("VLC: Play Command");

            _mediaplayer = new MediaPlayer(libVLC);
            _mediaplayer.Stopped += MediaPlayer_Stopped;
            _mediaplayer.Playing += MediaPlayer_Playing;
            _mediaplayer.EncounteredError += MediaPlayer_EncounteredError;
            _mediaplayer.Vout += MediaPlayer_Vout;

            _mediaplayer.EnableMouseInput = false;
            _mediaplayer.EnableKeyInput = false;

            _mediaplayer.SetMarqueeInt(VideoMarqueeOption.Size, 20);
            _mediaplayer.SetMarqueeInt(VideoMarqueeOption.X, 10);
            _mediaplayer.SetMarqueeInt(VideoMarqueeOption.Y, 10);

            media_input = new TSStreamMediaInput(ts_data_queue);
            media = new Media(libVLC, media_input);

            MediaConfiguration mediaConfig1 = new MediaConfiguration();
            mediaConfig1.EnableHardwareDecoding = false;
            media.AddOption(mediaConfig1);

            updateVideoPlayer(_mediaplayer, true);
        }

        public override void SetVolume(int Volume)
        {
            player_volume = Volume;

            if (videoView.MediaPlayer != null)
            {
                videoView.MediaPlayer.Volume = player_volume;
            }

        }

        public override int GetVolume()
        {
            return player_volume;
        }

        public override int getID()
        {
            return _id;
        }

        public override void Initialize(CircularBuffer TSDataQueue, int ID)
        {
            _id = ID;
            Initialize(TSDataQueue);
        }
    }
}
