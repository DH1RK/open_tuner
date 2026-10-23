using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace opentuner.Utilities
{
    // Tab headers for a TabControl whose own headers are hidden (ItemSize 0 x 1). With several boards the pages are
    // called "Properties-Pro", "Expert-Pro", ..., "Properties-V2", ...: every board gets its own row, the board name
    // ("Pro", "V2") stands in front as a title and the tabs show the name without it. Without such a suffix (one
    // board, other sources) there is one row of tabs. A row that is wider than the strip wraps into another line.
    public class TabRowStrip : Control
    {
        public const int TabWidth = 84;
        public const int TabHeight = 26;

        private class Entry
        {
            public TabPage Page;
            public string Text;
            public string Group;     // null = no title
            public Rectangle Bounds;
        }

        private readonly TabControl _tabs;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<KeyValuePair<string, Rectangle>> _titles = new List<KeyValuePair<string, Rectangle>>();
        private readonly Font _title_font;

        public TabRowStrip(TabControl tabs)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _tabs = tabs;
            _title_font = new Font(tabs.Font, FontStyle.Bold);
            Dock = DockStyle.Top;
            Height = TabHeight;

            _tabs.SelectedIndexChanged += (s, e) => Invalidate();
            Rebuild();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _title_font.Dispose();
            base.Dispose(disposing);
        }

        // Reads the pages of the TabControl again (call after pages were added, removed or renamed).
        public void Rebuild()
        {
            _entries.Clear();

            foreach (TabPage page in _tabs.TabPages)
                _entries.Add(new Entry { Page = page, Text = page.Text });

            // one row per board if every page has a "-<board>" suffix and there are at least two boards
            var split = _entries.Select(e => e.Text.LastIndexOf('-')).ToList();
            if (_entries.Count > 1 && split.All(i => i > 0))
            {
                var groups = _entries.Select((e, i) => e.Text.Substring(split[i] + 1)).Distinct().ToList();
                if (groups.Count > 1)
                {
                    for (int i = 0; i < _entries.Count; i++)
                    {
                        _entries[i].Group = _entries[i].Text.Substring(split[i] + 1);
                        _entries[i].Text = _entries[i].Text.Substring(0, split[i]);
                    }
                }
            }

            LayoutTabs();
            Invalidate();
        }

        // Width the widest row needs (title and all its tabs), so the strip does not have to wrap.
        public int PreferredWidth { get; private set; }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutTabs();
        }

        private void LayoutTabs()
        {
            _titles.Clear();

            var groups = _entries.Select(e => e.Group).Distinct().ToList();
            bool titled = groups.Count > 0 && groups[0] != null;
            int title_width = 0;
            if (titled)
            {
                foreach (string group in groups)
                    title_width = Math.Max(title_width, TextRenderer.MeasureText(group, _title_font).Width);
                title_width += 14;
            }

            int widest = 0;
            foreach (string group in groups)
                widest = Math.Max(widest, _entries.Count(e => e.Group == group));
            PreferredWidth = title_width + widest * TabWidth;

            int y = 0;
            foreach (string group in groups)
            {
                if (titled)
                    _titles.Add(new KeyValuePair<string, Rectangle>(group, new Rectangle(0, y, title_width, TabHeight)));

                int x = title_width;
                foreach (Entry entry in _entries.Where(e => e.Group == group))
                {
                    if (x > title_width && x + TabWidth > Width)   // wrap
                    {
                        x = title_width;
                        y += TabHeight;
                    }

                    entry.Bounds = new Rectangle(x, y, TabWidth, TabHeight);
                    x += TabWidth;
                }

                y += TabHeight;
            }

            int height = Math.Max(TabHeight, y);
            if (Height != height)
                Height = height;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.FromArgb(48, 48, 48));

            foreach (var title in _titles)
            {
                TextRenderer.DrawText(g, title.Key, _title_font, title.Value, Color.White,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            foreach (Entry entry in _entries)
            {
                bool selected = entry.Page == _tabs.SelectedTab;

                // a button like the ones of the Properties: blue for the shown tab
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                FlexButton.DrawFace(g, new RectangleF(entry.Bounds.X + 1.5f, entry.Bounds.Y + 1.5f, entry.Bounds.Width - 3, entry.Bounds.Height - 3), selected, true, entry == _hover ? 14 : 0);

                TextRenderer.DrawText(g, entry.Text, _tabs.Font, entry.Bounds, Color.White,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private Entry _hover;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            Entry hit = _entries.FirstOrDefault(entry => entry.Bounds.Contains(e.Location));
            if (hit != _hover)
            {
                _hover = hit;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (_hover != null)
            {
                _hover = null;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            Entry hit = _entries.FirstOrDefault(entry => entry.Bounds.Contains(e.Location));
            if (hit != null && _tabs.TabPages.Contains(hit.Page))
                _tabs.SelectedTab = hit.Page;
        }
    }
}
