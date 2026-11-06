using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using opentuner.Utilities;

namespace opentuner.MediaSources.Minitiouner
{
    // A line of memory buttons (M1 .. M7, P1 .. P7): a click recalls the memory, holding the button stores into it (the button flashes orange).
    // Empty memories are grey. In a narrow tab the buttons wrap into a second line and the row grows, like the rate buttons.
    public class MemoryButtonRow : FlowLayoutPanel
    {
        public const int StandardHeight = 32;
        private const int LongPressMs = 800;     // holding a button this long stores into it
        private const int FlashMs = 350;

        private readonly string _prefix;
        private readonly string _store_what;
        private readonly List<FlexButton> _buttons = new List<FlexButton>();
        private readonly Color _empty_color = Color.FromArgb(255, 128, 128, 128);
        private readonly Color _used_color;
        private readonly ToolTip _tips = new ToolTip { ShowAlways = true };
        private readonly Timer _long_press_timer = new Timer();
        private readonly Timer _flash_timer = new Timer();
        private int _long_press_slot = -1;
        private bool _long_press_fired = false;   // the click that ends a long press must not recall
        private int _flash_slot = -1;

        // a button was clicked: recall this memory (0 = first)
        public event Action<int> Recall;

        // a button was held: store into this memory
        public event Action<int> Store;

        // label: text at the left ("Memory:"), prefix: caption of the buttons ("M" gives M1, M2 ...), store_what: what holds the memory,
        // for the tooltip ("the setting of this tuner")
        public MemoryButtonRow(string label, string prefix, int count, string store_what)
        {
            _prefix = prefix;
            _store_what = store_what;

            Dock = DockStyle.Top;
            WrapContents = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(0, StandardHeight);
            Controls.Add(new Label { Text = label, AutoSize = false, Width = 90, Height = 26, TextAlign = ContentAlignment.MiddleLeft });

            _long_press_timer.Interval = LongPressMs;
            _long_press_timer.Tick += (s, e) =>
            {
                _long_press_timer.Stop();
                if (_long_press_slot < 0)
                    return;

                _long_press_fired = true;
                int slot = _long_press_slot;
                Store?.Invoke(slot);

                // a short orange flash says that it was stored
                _flash_slot = slot;
                _buttons[slot].BackColor = Color.FromArgb(255, 204, 128);
                _flash_timer.Stop();
                _flash_timer.Start();
            };
            _flash_timer.Interval = FlashMs;
            _flash_timer.Tick += (s, e) =>
            {
                _flash_timer.Stop();
                if (_flash_slot >= 0)
                    _buttons[_flash_slot].BackColor = SystemColors.Control;
                _flash_slot = -1;
            };

            for (int slot = 0; slot < count; slot++)
            {
                int number = slot;
                var button = new FlexButton { Text = prefix + (slot + 1), Width = 44, Height = 26, Margin = new Padding(1, 0, 3, 0), FlatStyle = FlatStyle.Flat, KeepFontSize = true };
                button.Font = new Font("Segoe UI Semibold", 8f);
                button.FlatAppearance.BorderColor = Color.Gray;
                _used_color = button.ForeColor;
                button.ForeColor = _empty_color;

                button.MouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left)
                        return;

                    _long_press_fired = false;
                    _long_press_slot = number;
                    _long_press_timer.Stop();
                    _long_press_timer.Start();
                };
                button.MouseUp += (s, e) => _long_press_timer.Stop();
                button.MouseLeave += (s, e) => _long_press_timer.Stop();
                button.Click += (s, e) =>
                {
                    if (_long_press_fired)
                    {
                        _long_press_fired = false;
                        return;
                    }

                    Recall?.Invoke(number);
                };

                _tips.SetToolTip(button, prefix + (slot + 1) + ": empty (hold the button to store)");
                _buttons.Add(button);
                Controls.Add(button);
            }
        }

        // Shows whether a memory is in use (grey = empty) and what it holds. May be called from any thread.
        public void SetSlot(int slot, bool used, string text)
        {
            if (slot < 0 || slot >= _buttons.Count || !IsHandleCreated || IsDisposed)
                return;

            try
            {
                BeginInvoke((MethodInvoker)(() =>
                {
                    _buttons[slot].ForeColor = used ? _used_color : _empty_color;
                    _tips.SetToolTip(_buttons[slot], _prefix + (slot + 1) + ": " + text +
                                     (used ? " - click to recall, hold to store " + _store_what + " into it" : " (hold the button to store)"));
                }));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _long_press_timer.Dispose();
                _flash_timer.Dispose();
                _tips.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
