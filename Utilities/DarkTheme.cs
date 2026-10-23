using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace opentuner
{
    // The dark grey (48,48,48) of the form frame: title bar (Windows 11), menu bar and its drop-downs.
    public static class DarkTheme
    {
        public static readonly Color Frame = Color.FromArgb(48, 48, 48);
        public static readonly Color Surface = Color.FromArgb(128, 128, 128);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        private static int Bgr(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        public static void Apply(Form form, MenuStrip menu)
        {
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
            ForeColorAll(menu.Items);
            menu.Font = new Font("Segoe UI Semibold", 10f);   // like the Tuner groups

            ApplyTitleBar(form);
        }

        public static readonly Color Input = Color.FromArgb(63, 70, 76);        // the colour of the text of the chat

        // A dialog in the dark look of the main window: dark grey background, white text, dark input fields, dark title bar.
        // Labels, group boxes, radio buttons and panels take the colours of the form; the buttons, check boxes and combo boxes
        // are the Flex controls and draw themselves.
        public static void ApplyDialog(Form form)
        {
            form.BackColor = Frame;
            form.ForeColor = Color.White;
            ApplyTitleBar(form);
            StyleChildren(form);
        }

        private static void StyleChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                StyleControl(c);
                StyleChildren(c);
            }
        }

        private static void StyleControl(Control c)
        {
            if (c is TabPage page)
            {
                page.UseVisualStyleBackColor = false;
                page.BackColor = Frame;
                page.ForeColor = Color.White;
            }
            else if (c is SplitContainer split)
            {
                split.BackColor = Frame;
            }
            else if (c is TextBoxBase || c is NumericUpDown || c is ListBox || c is ListView || c is TreeView)
            {
                c.BackColor = Input;
                c.ForeColor = Color.White;

                if (c is TextBoxBase text_box)
                    text_box.BorderStyle = BorderStyle.FixedSingle;
                else if (c is ListBox list_box)
                    list_box.BorderStyle = BorderStyle.FixedSingle;
                else if (c is ListView list_view)
                    list_view.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is LinkLabel link)
            {
                link.LinkColor = Color.FromArgb(86, 156, 255);
                link.VisitedLinkColor = Color.FromArgb(86, 156, 255);
                link.ActiveLinkColor = Color.White;
            }
            else if (c is ToolStrip strip)
            {
                strip.BackColor = Frame;
                strip.ForeColor = Color.White;
                if (strip is MenuStrip || strip is StatusStrip || strip is ToolStrip)
                    strip.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
                ForeColorAll(strip.Items);
            }
        }

        // Title bar in the dark grey of the frame; the attributes need a window handle, older Windows versions ignore them.
        public static void ApplyTitleBar(Form form)
        {
            EventHandler set_attributes = (s, e) =>
            {
                int dark = 1, caption = Bgr(Frame), text = Bgr(Color.White);
                DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                DwmSetWindowAttribute(form.Handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            };

            form.HandleCreated += set_attributes;
            if (form.IsHandleCreated)
                set_attributes(form, EventArgs.Empty);
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        // Dark scroll bars (Windows 10 1809 and newer; the older ones keep the normal ones).
        public static void DarkScrollBars(Control control)
        {
            EventHandler set_theme = (s, e) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);

            control.HandleCreated += set_theme;
            if (control.IsHandleCreated)
                set_theme(control, EventArgs.Empty);
        }

        // Text of the labels, check boxes and group boxes below `root` in white (for a dark background); text boxes and
        // lists keep their own colours.
        public static void WhiteLabels(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is Label || c is CheckBox || c is GroupBox)
                    c.ForeColor = Color.White;

                WhiteLabels(c);
            }
        }

        private static void ForeColorAll(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.ForeColor = Color.White;
                if (item is ToolStripMenuItem menu_item)
                    ForeColorAll(menu_item.DropDownItems);
            }
        }

        private class DarkColorTable : ProfessionalColorTable
        {
            public DarkColorTable() { UseSystemColors = false; }

            public override Color MenuStripGradientBegin { get { return Frame; } }
            public override Color MenuStripGradientEnd { get { return Frame; } }
            public override Color ToolStripDropDownBackground { get { return Frame; } }
            public override Color ImageMarginGradientBegin { get { return Frame; } }
            public override Color ImageMarginGradientMiddle { get { return Frame; } }
            public override Color ImageMarginGradientEnd { get { return Frame; } }
            public override Color MenuBorder { get { return Surface; } }
            public override Color MenuItemBorder { get { return Surface; } }
            public override Color MenuItemSelected { get { return Surface; } }
            public override Color MenuItemSelectedGradientBegin { get { return Surface; } }
            public override Color MenuItemSelectedGradientEnd { get { return Surface; } }
            public override Color MenuItemPressedGradientBegin { get { return Frame; } }
            public override Color MenuItemPressedGradientMiddle { get { return Frame; } }
            public override Color MenuItemPressedGradientEnd { get { return Frame; } }
            public override Color SeparatorDark { get { return Surface; } }
            public override Color SeparatorLight { get { return Frame; } }
        }
    }
}