using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace opentuner.Utilities
{
    // Shown once at startup when a native playback dependency (ffmpeg shared build, libmpv) is
    // missing - guides the user to the right download page and lets them point Settings at the
    // extracted folder, instead of two silent MessageBox warnings that just point at SETUP.md.
    // "Skip" leaves the existing fallback behavior (bundled "ffmpeg\" folder / default DLL search
    // order) untouched, so someone who doesn't need video right now isn't blocked from starting.
    public class PlaybackPathsSetupForm : Form
    {
        private readonly TextBox _ffmpegPathBox = new TextBox();
        private readonly TextBox _libmpvPathBox = new TextBox();
        private readonly bool _showFfmpeg;
        private readonly bool _showLibmpv;

        public string FfmpegPath => _ffmpegPathBox.Text;
        public string LibmpvPath => _libmpvPathBox.Text;

        public PlaybackPathsSetupForm(string ffmpegPath, string libmpvPath, bool showFfmpeg, bool showLibmpv)
        {
            _showFfmpeg = showFfmpeg;
            _showLibmpv = showLibmpv;
            _ffmpegPathBox.Text = ffmpegPath;
            _libmpvPathBox.Text = libmpvPath;

            Text = "OpenTuner - Playback Setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 40);
            Font = new Font("Microsoft Sans Serif", 8.25F);

            var intro = new Label
            {
                Text = "OpenTuner needs one or more native playback libraries that can't be installed via NuGet\n" +
                       "(the exact build has to match this version of OpenTuner). Point OpenTuner at where you\n" +
                       "extracted them, or download them now - see SETUP.md for details. You can skip this and\n" +
                       "set it later under Settings > Playback Paths.",
                AutoSize = false,
                Location = new Point(12, 12),
                Size = new Size(536, 60)
            };
            Controls.Add(intro);

            int y = 80;

            if (_showFfmpeg)
            {
                y = AddPathRow(y, "ffmpeg (video/audio playback):",
                    "Shared build, e.g. from gyan.dev - point at the folder containing avcodec-*.dll etc.",
                    _ffmpegPathBox, "https://www.gyan.dev/ffmpeg/builds/");
            }

            if (_showLibmpv)
            {
                y = AddPathRow(y, "libmpv (MPV player option):",
                    "Point at the folder containing libmpv-2.dll.",
                    _libmpvPathBox, "https://sourceforge.net/projects/mpv-player-windows/files/libmpv/");
            }

            var buttonSave = new Button
            {
                Text = "Save && Continue",
                DialogResult = DialogResult.OK,
                Location = new Point(340, y + 8),
                Size = new Size(120, 28)
            };
            Controls.Add(buttonSave);

            var buttonSkip = new Button
            {
                Text = "Skip for now",
                DialogResult = DialogResult.Cancel,
                Location = new Point(468, y + 8),
                Size = new Size(80, 28)
            };
            Controls.Add(buttonSkip);

            AcceptButton = buttonSave;
            CancelButton = buttonSkip;
            ClientSize = new Size(560, y + 48);
        }

        private int AddPathRow(int y, string title, string hint, TextBox pathBox, string downloadUrl)
        {
            var titleLabel = new Label { Text = title, AutoSize = true, Location = new Point(12, y), Font = new Font(Font, FontStyle.Bold) };
            Controls.Add(titleLabel);

            var hintLabel = new Label { Text = hint, AutoSize = true, Location = new Point(12, y + 18) };
            Controls.Add(hintLabel);

            pathBox.Location = new Point(12, y + 38);
            pathBox.Size = new Size(340, 23);
            Controls.Add(pathBox);

            var browseButton = new Button { Text = "Browse...", Location = new Point(358, y + 37), Size = new Size(80, 25) };
            browseButton.Click += (s, e) =>
            {
                using (var fbd = new FolderBrowserDialog { SelectedPath = pathBox.Text })
                {
                    if (fbd.ShowDialog() == DialogResult.OK)
                        pathBox.Text = fbd.SelectedPath + "\\";
                }
            };
            Controls.Add(browseButton);

            var downloadButton = new Button { Text = "Download page", Location = new Point(444, y + 37), Size = new Size(104, 25) };
            downloadButton.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show("Could not open the browser: " + ex.Message); }
            };
            Controls.Add(downloadButton);

            return y + 70;
        }

        // Effective ffmpeg dir either way (configured, or the bundled fallback next to the exe) actually
        // has the DLLs FlyleafLib loads - Directory.Exists alone would pass on an empty leftover folder.
        public static bool FfmpegPathValid(string path)
        {
            string effective = string.IsNullOrWhiteSpace(path) ? @"ffmpeg\" : path;
            return Directory.Exists(effective) && Directory.EnumerateFiles(effective, "avcodec-*.dll").GetEnumerator().MoveNext();
        }

        public static bool LibmpvPathValid(string path)
        {
            string effective = string.IsNullOrWhiteSpace(path) ? AppDomain.CurrentDomain.BaseDirectory : path;
            return File.Exists(Path.Combine(effective, "libmpv-2.dll"));
        }
    }
}
