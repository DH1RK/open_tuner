using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using opentuner.MediaSources;
using System.Globalization;
using opentuner.ExtraFeatures.BATCSpectrum;

namespace opentuner.Utilities
{
    // The symbols of the info line that can be clicked
    public enum InfoLineItem { Mute, Udp, Record }

    public class StreamInfoContainer : Label
    {
        // Left click on the mute symbol (toggles the audio) and on a lit U or R (switches the stream / recording off)
        public event Action<InfoLineItem> ItemClicked;

        // click areas: the audio symbol in the coordinates of the (possibly scaled) content, U and R in client coordinates
        private RectangleF _audio_rect = RectangleF.Empty;
        private RectangleF _udp_rect = RectangleF.Empty;
        private RectangleF _rec_rect = RectangleF.Empty;
        private float _hit_scale = 1f;
        private float _hit_offset_y = 0f;

        OTSourceData last_info_data = null;
        private Font font = new Font("Arial", 12, FontStyle.Bold);
        private readonly ToolTip _toolTip = new ToolTip { ShowAlways = true };
        private string _toolTipText = "";

        // the tuner selected by a click on its video: frame of the info line in the colour of the tuner instead of white
        private Color _highlight_color = Color.Empty;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color HighlightColor
        {
            get { return _highlight_color; }
            set
            {
                if (_highlight_color == value)
                    return;

                _highlight_color = value;
                Invalidate();
            }
        }

        public StreamInfoContainer(bool show) 
        {
            BackColor = Color.Black;
            Dock = DockStyle.Top;
            DoubleBuffered = true;   // the line is repainted several times a second: no flicker

            //Height = 30;
            Visible = show;
        }

        public void UpdateInfo(OTSourceData info_data)
        {
            last_info_data = info_data;
            UpdateToolTip();
            Invalidate();
        }

        // Below this scale the text is clipped at the window edge instead of shrinking further.
        private const float MinScale = 0.6f;

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);

            ForeColor = Color.Black;

            Graphics g = pe.Graphics;

            SizeF temp_text_size = g.MeasureString("Temp", font);
            int wanted_height = (int)temp_text_size.Height + 10;
            if (Height != wanted_height)   // changing Height inside OnPaint re-triggers layout: only when needed
                Height = wanted_height;

            g.FillRectangle(Brushes.Black, 0, 0, Width, Height);
            if (_highlight_color.IsEmpty)
            {
                g.DrawRectangle(Pens.White, 0, 0, Width - 1, Height - 1);
            }
            else
            {
                // twice as thick as the normal frame so that it is seen at a glance
                using (Pen highlight_pen = new Pen(_highlight_color, 2))
                {
                    g.DrawRectangle(highlight_pen, 1, 1, Width - 3, Height - 3);
                }
            }

            _audio_rect = _udp_rect = _rec_rect = RectangleF.Empty;

            if (last_info_data == null)
                return;

            // In a narrow window the right end (icons, U/R, TS error flags) used to be cut off:
            // measure first and shrink the whole line to fit, down to MinScale.
            // The "R" (recording) and the "U" (UDP stream) sit at the right edge and are not shrunk: the line gets the room it needs.
            float udp_width = last_info_data.streaming ? g.MeasureString("U", font).Width + 4 : 0;
            float rec_width = last_info_data.recording ? g.MeasureString("R", font).Width + 4 : 0;
            float needed = DrawContent(g, false);
            float available = Width - 4 - udp_width - rec_width;
            float scale = 1f;
            if (needed > available && needed > 0)
                scale = Math.Max(MinScale, available / needed);

            _hit_scale = scale;
            _hit_offset_y = scale < 1f ? Height * (1f - scale) / 2f : 0f;

            if (scale < 1f)
            {
                g.TranslateTransform(0, Height * (1f - scale) / 2f);   // keep it vertically centred
                g.ScaleTransform(scale, scale);
            }

            DrawContent(g, true);

            g.ResetTransform();
            if (last_info_data.streaming)
            {
                using (Brush udp_brush = new SolidBrush(CommonFunctions.UdpActiveColor))
                    g.DrawString("U", font, udp_brush, new PointF(Width - udp_width - 2, 5));
                _udp_rect = new RectangleF(Width - udp_width - 2, 0, udp_width + 2, Height);
            }
            if (last_info_data.recording)
            {
                using (Brush rec_brush = new SolidBrush(CommonFunctions.RecordActiveColor))
                    g.DrawString("R", font, rec_brush, new PointF(Width - udp_width - rec_width - 2, 5));
                _rec_rect = new RectangleF(Width - udp_width - rec_width - 2, 0, rec_width, Height);
            }
        }

        private InfoLineItem? ItemAt(Point location)
        {
            if (_udp_rect.Contains(location))
                return InfoLineItem.Udp;
            if (_rec_rect.Contains(location))
                return InfoLineItem.Record;

            // the content may have been shrunk to fit: back to its coordinates
            var content = new PointF(location.X / _hit_scale, (location.Y - _hit_offset_y) / _hit_scale);
            if (_audio_rect.Contains(content))
                return InfoLineItem.Mute;

            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = ItemAt(e.Location).HasValue ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            if (e.Button != MouseButtons.Left)
                return;

            InfoLineItem? item = ItemAt(e.Location);
            if (item.HasValue)
                ItemClicked?.Invoke(item.Value);
        }

        // The recording was switched on or off (button, menu, R key): show it at once, not with the next data update.
        public void SetRecording(bool recording)
        {
            if (last_info_data == null || last_info_data.recording == recording)
                return;

            last_info_data.recording = recording;
            Invalidate();
        }

        // The UDP stream was switched on or off (button, menu, U key): show it at once, not with the next data update.
        public void SetStreaming(bool streaming)
        {
            if (last_info_data == null || last_info_data.streaming == streaming)
                return;

            last_info_data.streaming = streaming;
            Invalidate();
        }

        // Draws the info line (or, with draw = false, only measures it) and returns its width.
        private float DrawContent(Graphics g, bool draw)
        {
            string info = "";

            string locked_info = (last_info_data.service_name.Length == 0 ? "Lock" : last_info_data.service_name) + " - " + " D" + last_info_data.db_margin.ToString("F1");

            if (last_info_data.demod_locked)
            {
                info = "MER: " + last_info_data.mer.ToString("F1") + " dB";
            }
            // the channel of the bandplan (the "CH:" of the spectrum) in front of the frequency
            string channel = BandplanChannels.Find(last_info_data.frequency, last_info_data.symbol_rate);
            if (channel.Length > 0)
            {
                info = info + " - " + channel;
            }
            info = info + " - " + last_info_data.frequency.ToString("N0", CultureInfo.CurrentCulture);
            string codec = MediaStatus.CodecDisplayName(last_info_data.video_codec);
            if (codec.Length > 0)
            {
                info = info + " - " + codec;
            }
            info = info + " - " + last_info_data.symbol_rate.ToString();
            if (last_info_data.demod_locked && !string.IsNullOrEmpty(last_info_data.modcode))
            {
                info = info + " - " + last_info_data.modcode;
            }

            float x_pos = 2;
            int y_pos = 5;

            if (last_info_data.demod_locked)
            {
                if (draw) g.DrawString(locked_info, font, Brushes.LimeGreen, new PointF(x_pos, y_pos));
                x_pos += g.MeasureString(locked_info, font).Width;
            }
            else
            {
                if (draw) g.DrawString("No Lock", font, Brushes.PaleVioletRed, new PointF(x_pos, y_pos));
                x_pos += g.MeasureString("No Lock", font).Width;
            }

            if (draw) g.DrawString(info, font, Brushes.AntiqueWhite, new PointF(x_pos, y_pos));
            x_pos += g.MeasureString(info, font).Width;

            using (Font emojiFont = new Font("Segoe UI Emoji", 12, FontStyle.Bold))
            {
                string audio_icon_mute = "\U0001F507";
                string audio_icon_high = "\uD83D\uDD0A";

                if (draw)
                {
                    if (last_info_data.volume > 0)
                        g.DrawString(audio_icon_high, emojiFont, Brushes.LimeGreen, new PointF(x_pos - 5, 2));
                    else
                        g.DrawString(audio_icon_mute, emojiFont, Brushes.IndianRed, new PointF(x_pos - 5, 2));
                }

                if (draw) _audio_rect = new RectangleF(x_pos - 5, 0, g.MeasureString(audio_icon_mute, emojiFont).Width + 5, Height);
                x_pos += g.MeasureString(audio_icon_mute, emojiFont).Width;
            }

            // TS health flags (issue #19): only shown while errors occurred in the last few seconds
            x_pos = DrawFlag(g, draw, "CC", last_info_data.ts_cc_errors, true, Brushes.PaleVioletRed, x_pos, y_pos);
            x_pos = DrawFlag(g, draw, "TEI", last_info_data.ts_tei_errors, true, Brushes.Orange, x_pos, y_pos);
            x_pos = DrawFlag(g, draw, "SYNC", last_info_data.ts_sync_losses, true, Brushes.Orange, x_pos, y_pos);
            x_pos = DrawFlag(g, draw, "BUF", last_info_data.ts_buffer_overflows, true, Brushes.Orange, x_pos, y_pos);
            x_pos = DrawFlag(g, draw, "ENC", last_info_data.ts_scrambled, false, Brushes.Gold, x_pos, y_pos);   // no count: the number of packets says nothing

            return x_pos;
        }

        private float DrawFlag(Graphics g, bool draw, string name, uint count, bool showCount, Brush brush, float x_pos, int y_pos)
        {
            if (count == 0)
                return x_pos;

            string text = showCount ? name + " " + count.ToString() : name;
            if (draw) g.DrawString(text, font, brush, new PointF(x_pos, y_pos));
            return x_pos + g.MeasureString(text + " ", font).Width;
        }

        private void UpdateToolTip()
        {
            string text = "";

            if (last_info_data != null &&
                (last_info_data.ts_cc_errors > 0 || last_info_data.ts_tei_errors > 0 || last_info_data.ts_sync_losses > 0 ||
                 last_info_data.ts_buffer_overflows > 0 || last_info_data.ts_scrambled > 0))
            {
                text = "TS problems in the last " + TSHealth.WindowSeconds + " s:\n" +
                       "CC = continuity errors (TS packets lost)\n" +
                       "TEI = packets flagged uncorrectable by the demodulator\n" +
                       "SYNC = lost TS sync, bytes skipped\n" +
                       "BUF = a queue overflowed, bytes dropped (player/recorder/UDP too slow)\n" +
                       "ENC = scrambled (encrypted) packets";
            }

            // only touch the ToolTip when the text changed - this runs several times a second
            if (text != _toolTipText)
            {
                _toolTipText = text;
                _toolTip.SetToolTip(this, text);
            }
        }
    }
}
