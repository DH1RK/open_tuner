using Newtonsoft.Json.Linq;
using opentuner.ExtraFeatures.StreamDeck;
using Serilog;
using System;
using System.Windows.Forms;

namespace opentuner
{
    // The Stream Deck plugin (#88) talks to OpenTuner through StreamDeckServer: commands come in on the socket's thread and are
    // run on the UI thread, the state is built on the UI thread and sent when it changed.
    public partial class MainForm
    {
        private StreamDeckServer _streamdeck;
        private System.Windows.Forms.Timer _streamdeck_timer;

        // The tuner selected by the last click, key or command. Unlike the highlight it stays (-1 = none yet).
        private int _selected_tuner = -1;

        // lock state per tuner, set by the source data (any thread)
        private readonly bool[] _tuner_locked = new bool[8];

        private const int StreamDeckStateIntervalMs = 250;

        private void StartStreamDeck()
        {
            if (!_settings.streamdeck_enabled || _streamdeck != null)
                return;

            var server = new StreamDeckServer();
            server.CommandReceived += (command, message) =>
            {
                try { BeginInvoke(new Action(() => RunStreamDeckCommand(command, message))); }
                catch (InvalidOperationException) { }   // the window is closing
            };

            if (!server.Start(_settings.streamdeck_port))
                return;

            _streamdeck = server;

            _streamdeck_timer = new System.Windows.Forms.Timer { Interval = StreamDeckStateIntervalMs };
            _streamdeck_timer.Tick += (s, e) => _streamdeck?.PublishState(BuildStreamDeckState().ToString(Newtonsoft.Json.Formatting.None));
            _streamdeck_timer.Start();
        }

        private void StopStreamDeck()
        {
            _streamdeck_timer?.Stop();
            _streamdeck_timer?.Dispose();
            _streamdeck_timer = null;

            _streamdeck?.Stop();
            _streamdeck = null;
        }

        private JObject BuildStreamDeckState()
        {
            int count = videoSource != null ? Math.Min(videoSource.GetVideoSourceCount(), _tuner_locked.Length) : 0;

            var muted = new JArray();
            var volume = new JArray();
            var locked = new JArray();
            for (int i = 0; i < count; i++)
            {
                muted.Add(videoSource.IsMuted(i) ?? false);
                volume.Add(videoSource.GetVolume(i));
                locked.Add(_tuner_locked[i]);
            }

            string view = _large_video < 0 ? "grid" : (_large_only ? "only" : "large");

            return new JObject
            {
                ["type"] = "state",
                ["tuners"] = count,
                ["selected"] = _selected_tuner >= 0 && _selected_tuner < count ? _selected_tuner + 1 : 0,   // 1 based, 0 = none
                ["view"] = view,
                ["large"] = _large_video >= 0 ? _large_video + 1 : 0,
                ["tab"] = tabControl1.SelectedTab?.Text ?? "",
                ["muted"] = muted,
                ["volume"] = volume,
                ["locked"] = locked
            };
        }

        // "tuner" of a message is 1 based, left out = the selected tuner; -1 if there is no such tuner
        private int StreamDeckTuner(JObject message)
        {
            int count = videoSource != null ? videoSource.GetVideoSourceCount() : 0;

            JToken token = message["tuner"];
            int tuner = token != null && token.Type == JTokenType.Integer ? (int)token - 1 : _selected_tuner;

            return tuner >= 0 && tuner < count ? tuner : -1;
        }

        private void RunStreamDeckCommand(string command, JObject message)
        {
            if (_closing || videoSource == null)
                return;

            // the grid needs no tuner
            if (command == "view" && (string)message["view"] == "grid")
            {
                ShowVideoGrid();
                return;
            }

            int tuner = StreamDeckTuner(message);
            if (tuner < 0)
            {
                Log.Information("Stream Deck: command " + command + " without a valid tuner ignored");
                return;
            }

            switch (command)
            {
                case "select":
                    SelectTuner(tuner);
                    break;

                case "mute":
                    if (videoSource.IsMuted(tuner).HasValue)
                        ToggleMuteAndShow(tuner);
                    break;

                case "tab":
                    ShowTunerTab(tuner, (string)message["tab"]);
                    break;

                case "view":
                    switch ((string)message["view"])
                    {
                        // the tuner shown large is the selected one
                        case "large": SelectTuner(tuner); ShowLargeVideo(tuner, false); break;
                        case "only": SelectTuner(tuner); ShowLargeVideo(tuner, true); break;
                        case "grid": ShowVideoGrid(); break;
                    }
                    break;

                case "volume":
                    JToken delta = message["delta"];
                    if (delta != null && delta.Type == JTokenType.Integer)
                    {
                        videoSource.UpdateVolume(tuner, (int)delta);
                        if (tuner < info_display.Count && info_display[tuner] != null)
                            info_display[tuner].SetVolume(videoSource.GetVolume(tuner));
                    }
                    break;

                default:
                    Log.Information("Stream Deck: unknown command " + command);
                    break;
            }
        }

        // shows the tab of the given kind (Properties, Expert, TS Info, Special, Chip) of the board of this tuner
        private void ShowTunerTab(int tuner, string kind)
        {
            string properties_title = videoSource.GetPropertiesTabTitle(tuner);
            if (string.IsNullOrEmpty(kind) || properties_title == null || !properties_title.StartsWith("Properties"))
                return;

            string suffix = properties_title.Substring("Properties".Length);   // "" or "-Pro"

            foreach (TabPage page in tabControl1.TabPages)
            {
                if (page.Text == kind + suffix)
                {
                    SelectTuner(tuner);
                    tabControl1.SelectedTab = page;
                    return;
                }
            }
        }
    }
}
