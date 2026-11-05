using Serilog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace opentuner
{
    // "Mute all" at the right end of the menu bar: the first press mutes every tuner that is on and remembers which ones they were,
    // the next press switches on only those again (if none is remembered, all of them). The symbol is the one of the info line (green loudspeaker, red = all muted).
    public partial class MainForm
    {
        private const string MuteAllSymbolOn = "🔊";    // loudspeaker, as in the info line
        private const string MuteAllSymbolMuted = "\U0001F507";   // muted loudspeaker

        private ToolStripMenuItem _mute_all_item;
        private System.Windows.Forms.Timer _mute_all_timer;

        // the tuners that were on when "Mute all" was pressed
        private readonly List<int> _mute_all_restore = new List<int>();

        private void BuildMuteAllButton()
        {
            _mute_all_item = new ToolStripMenuItem
            {
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font = new Font("Segoe UI Emoji", 12, FontStyle.Bold),
                AutoToolTip = false,
                ToolTipText = "Mute all streams; press again to switch on the ones that were on before"
            };
            _mute_all_item.Click += (s, e) => ToggleMuteAll();
            menuStrip1.ShowItemToolTips = true;
            menuStrip1.Items.Add(_mute_all_item);

            // the audio is also changed by keys, buttons and the Stream Deck: look at it again and again
            _mute_all_timer = new System.Windows.Forms.Timer { Interval = 500 };
            _mute_all_timer.Tick += (s, e) => UpdateMuteAllButton();
            _mute_all_timer.Start();
            UpdateMuteAllButton();
        }

        // true if no tuner is on (and there is at least one that can be muted)
        private bool AllMuted()
        {
            if (videoSource == null)
                return false;

            bool any_mutable = false;
            for (int i = 0; i < videoSource.GetVideoSourceCount(); i++)
            {
                bool? muted = videoSource.IsMuted(i);
                if (!muted.HasValue)
                    continue;

                any_mutable = true;
                if (!muted.Value)
                    return false;
            }

            return any_mutable;
        }

        private void ToggleMuteAll()
        {
            if (videoSource == null)
            {
                Log.Information("Mute all: no source connected, nothing to do");
                return;
            }

            int count = videoSource.GetVideoSourceCount();

            var on = new List<int>();
            var states = new List<string>();
            for (int i = 0; i < count; i++)
            {
                bool? is_muted = videoSource.IsMuted(i);
                states.Add((i + 1) + "=" + (is_muted.HasValue ? (is_muted.Value ? "muted" : "on") : "no player"));
                if (is_muted == false)
                    on.Add(i);
            }

            Log.Information("Mute all: tuners " + string.Join(", ", states) + "; " + (on.Count > 0 ? "muting " + on.Count : "switching on again " + _mute_all_restore.Count));

            if (on.Count > 0)
            {
                _mute_all_restore.Clear();
                _mute_all_restore.AddRange(on);

                foreach (int i in on)
                    ToggleMuteAndShow(i);
            }
            else
            {
                // nothing remembered (e.g. all tuners were muted from the start): switch on every tuner that has a player
                var switch_on = new List<int>(_mute_all_restore);
                if (switch_on.Count == 0)
                {
                    for (int i = 0; i < count; i++)
                        switch_on.Add(i);
                }

                foreach (int i in switch_on)
                {
                    if (i < count && videoSource.IsMuted(i) == true)
                        ToggleMuteAndShow(i);
                }

                _mute_all_restore.Clear();
            }

            UpdateMuteAllButton();
        }

        private void UpdateMuteAllButton()
        {
            if (_mute_all_item == null)
                return;

            bool muted = AllMuted();
            _mute_all_item.Enabled = videoSource != null;
            _mute_all_item.Text = muted ? MuteAllSymbolMuted : MuteAllSymbolOn;
            _mute_all_item.ForeColor = videoSource == null ? Color.Gray : (muted ? Color.IndianRed : Color.LimeGreen);
        }
    }
}
