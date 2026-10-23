using SocketIOClient;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Threading;
using Serilog;
using opentuner.ExtraFeatures.BATCWebchat;
using opentuner.MediaSources;

namespace opentuner
{
    public partial class WebChatForm : Form
    {
        static Font consoleFont; 
        static Font consoleFontBold;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string prop_title { set { this.Text = value; } }

        WebChatSettings _settings;
        OTSource _source;

        public WebChatForm(WebChatSettings Settings, OTSource Source)
        {
            Font = new System.Drawing.Font("Segoe UI", 9f);   // the chat keeps the normal weight
            InitializeComponent();

            // the window frame in the dark grey of the main window
            BackColor = DarkTheme.Frame;
            splitContainer1.BackColor = DarkTheme.Frame;
            // the four report buttons in two columns that share the width of the frame equally, as high as the buttons in Properties
            var report_buttons = new[] { btnSigReportTuner1, btnSigReportTuner2, btnSigReportTuner3, btnSigReportTuner4 };
            foreach (var report_button in report_buttons)
                report_button.Font = new Font("Segoe UI Semibold", 10f);   // like the values in Properties

            groupBox1.Height = 100;
            groupBox1.Resize += (s, e) =>
            {
                const int margin = 12, gap = 10, top = 24, button_height = 28, row_gap = 8;
                int button_width = Math.Max(40, (groupBox1.ClientSize.Width - 2 * margin - gap) / 2);

                for (int i = 0; i < report_buttons.Length; i++)
                {
                    report_buttons[i].Size = new Size(button_width, button_height);
                    report_buttons[i].Location = new Point(margin + (i % 2) * (button_width + gap), top + (i / 2) * (button_height + row_gap));
                }
            };

            btnFilterClear.Font = new Font("Segoe UI Semibold", 10f);
            lblFilter.Font = btnFilterClear.Font;
            checkStayOnTop.Font = btnFilterClear.Font;
            checkTranslate.Font = btnFilterClear.Font;

            // the frame title in the font of the Tuner groups in Properties
            groupBox1.Font = new Font("Segoe UI Semibold", 10f);

            // the footer in the font of the values in Properties (MER ...)
            var footer_font = new Font("Segoe UI Semibold", 10f);
            foreach (ToolStripItem item in statusStrip1.Items)
                item.Font = footer_font;
            txtNick.LinkColor = Color.White;
            txtNick.VisitedLinkColor = Color.White;
            txtNick.ActiveLinkColor = Color.White;
            txtNick.LinkBehavior = LinkBehavior.HoverUnderline;

            // "Stay On Top" and "Translate" below the frame "Signal Report" (the frame only holds the four report buttons)
            var options = new Panel { Dock = DockStyle.Bottom, Height = 30 };
            options.Resize += (s, e) =>
            {
                // the two check boxes with the same distance to each other and to the edges
                int w1 = checkStayOnTop.PreferredSize.Width, w2 = checkTranslate.PreferredSize.Width;
                int space = Math.Max(6, (options.ClientSize.Width - w1 - w2) / 3);
                checkStayOnTop.Location = new Point(space, 6);
                checkTranslate.Location = new Point(space + w1 + space, 6);
            };
            splitContainer1.SplitterDistance = 230;   // the two check boxes in the larger font need the room
            options.Controls.Add(checkStayOnTop);
            options.Controls.Add(checkTranslate);
            splitContainer1.Panel1.Controls.Add(options);
            splitContainer1.Panel1.Controls.SetChildIndex(options, splitContainer1.Panel1.Controls.GetChildIndex(pnlFilter));

            DarkTheme.WhiteLabels(splitContainer1.Panel1);
            txtMessage.TextChanged += (s, e) =>
            {
                // the report button stays blue as long as its report is in the input field
                if (txtMessage.Text != _sig_report_text)
                    MarkSigReportButton(null);
            };
            DarkTheme.ApplyTitleBar(this);

            // footer in the dark grey, the frames of the user list, chat and input field all 2 px in the colour of the chat
            statusStrip1.BackColor = DarkTheme.Frame;
            statusStrip1.ForeColor = Color.White;
            foreach (ToolStripItem item in statusStrip1.Items)
                item.ForeColor = Color.White;

            var chat_color = Color.FromArgb(63, 70, 76);
            lbChat.BorderStyle = BorderStyle.None;
            FrameControl(lbUsers, chat_color);
            FrameControl(richChat, chat_color);
            FrameControl(txtMessage, chat_color);

            _settings = Settings;
            _source = Source;
            consoleFont = new Font("Consolas", _settings.chat_font_size);
            consoleFontBold = new Font("Consolas", _settings.chat_font_size + 1, FontStyle.Bold);

            checkTranslate.Checked = _settings.translate_enabled;
        }

        // After the settings window with the chat open: the signal report template and the auto login are read where they
        // are used, so they apply at once, the nick is used for the next login. The font size cannot be changed for the
        // lines already written: the chat is built again then (BATCChat.CloseForRestart).
        public void ApplySettings()
        {
            if (_settings.nickname.Length > 0)
                txtNick.Text = _settings.nickname;
        }

        // Closing the window with the X only hides it (wbchat_FormClosing); this closes it for real and drops the connection.
        private bool _shutdown = false;

        public void Shutdown()
        {
            _shutdown = true;

            try
            {
                client?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Chat: closing the connection failed");
            }

            Close();
        }

        private SocketIO client = null;

        private void wbchat_Load(object sender, EventArgs e)
        {
            if (_settings.nickname.Length > 0)
            {
                txtNick.Text = _settings.nickname;
            }
            else
            {
                txtNick.Text = "NONICK";
            }

            client = new SocketIO("https://eshail.batc.org.uk/", new SocketIOOptions
            {
                Path = "/wb/chat/socket.io",
                Query = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("room", "eshail-wb"),
                    }
            });

            client.OnConnected += Client_OnConnected;
            client.OnDisconnected += Client_OnDisconnected;

            Action<SocketIOResponse> callbackHistory = new Action<SocketIOResponse>(onHistoryCallback);
            Action<SocketIOResponse> callbackMessage = new Action<SocketIOResponse>(onMessageCallback);
            Action<SocketIOResponse> callbackNicks = new Action<SocketIOResponse>(onNicksCallback);
            Action<SocketIOResponse> callbackViewers = new Action<SocketIOResponse>(onViewersCallback);

            client.On("history", callbackHistory);
            client.On("message", callbackMessage);
            client.On("nicks", callbackNicks);
            client.On("viewers", callbackViewers);

            lbUsers.Font = consoleFontBold;
            lbUsers.DrawMode = DrawMode.OwnerDrawFixed;
            lbUsers.ItemHeight = consoleFontBold.Height + 2;
            lbUsers.DrawItem += lbUsers_DrawItem;
            lbUsers.ForeColor = Color.FromArgb(204, 204, 204);
            txtMessage.BackColor = Color.FromArgb(63, 70, 76);
            txtMessage.ForeColor = Color.FromArgb(204, 204, 204);
            txtMessage.Font = consoleFontBold;

            AddMessage("", "", "Connecting...");
            client.ConnectAsync();

            if (_source.GetVideoSourceCount() > 0)
                btnSigReportTuner1.Enabled = true;
            if (_source.GetVideoSourceCount() > 1)
                btnSigReportTuner2.Enabled = true;
            if (_source.GetVideoSourceCount() > 2)
            {
                btnSigReportTuner3.Enabled = true;
                btnSigReportTuner4.Enabled = true;
            }
        }

        private void Client_OnDisconnected(object sender, string e)
        {
            Log.Information("Chat: disconnected ({Reason}), disconnects so far: {Count}", e, Interlocked.Increment(ref _disconnectCount));
            SetConnectedLabel(false);
            _logged_in = false;
            ShowNickLink(true);
        }

        private int _connectCount;
        private int _disconnectCount;

        // socket callbacks run on a worker thread, never block it waiting for the UI thread
        private void SetConnectedLabel(bool connected)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            BeginInvoke(new Action(() => lblConnected.Text = "Connected: " + connected));
        }

        private delegate void UpdateLBDelegate(System.Windows.Forms.ListBox LB, Object obj);

        public static void AddItem(System.Windows.Forms.ListBox LB, Object obj)
        {
            if (LB.InvokeRequired)
            {
                UpdateLBDelegate ulb = new UpdateLBDelegate(AddItem);
                
                    LB.Invoke(ulb, new object[] { LB, obj });
            }
            else
            {
                if (LB.Items.Count > 1000)
                {
                    LB.Items.Remove(0);
                }

                int i = LB.Items.Add(obj);
                LB.TopIndex = i;
            }
        }

        private delegate void UpdateFormTitle(WebChatForm frm, string new_title);

        public void updateTitle(WebChatForm frm, string new_title)
        {

            if (frm.InvokeRequired)
            {
                UpdateFormTitle ulb = new UpdateFormTitle(updateTitle);

                    frm.Invoke(ulb, new object[] { frm, new_title });
            }
            else
            {
                frm.prop_title = new_title;
            }
        }

        private const int MaxChatLines = 500;
        private const int TrimChatBatch = 100;
        private const int WM_SETREDRAW = 0x000B;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private delegate void UpdateRTBDelegate(RichTextBox LB, string tstr, string nick, string msg, string translation);

        // appends one chat line (with the translation instead of the text, if there is one), formatting only, no scrolling or trimming
        private static void AppendChatLine(RichTextBox rtb, string tstr, string nick, string msg, string translation = null)
        {
            // 204, 204, 204
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionFont = consoleFont;
            rtb.SelectionColor = Color.FromArgb(204, 204, 204);
            rtb.AppendText(tstr);

            rtb.SelectionFont = consoleFontBold;
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionColor = Color.FromArgb(251, 222, 45);
            rtb.AppendText(" <" + nick + "> ");

            if (!string.IsNullOrEmpty(translation))
            {
                // the translation takes the place of the text, in italics: italic = translated, normal = in the language of the user
                using (var italic = new Font(consoleFont, FontStyle.Italic))
                {
                    rtb.SelectionFont = italic;
                    rtb.SelectionColor = Color.FromArgb(204, 204, 204);
                    rtb.SelectionStart = rtb.TextLength;
                    rtb.AppendText(translation.Replace("\n", " ") + "\n");
                }
            }
            else
            {
                rtb.SelectionFont = consoleFont;
                rtb.SelectionColor = Color.FromArgb(204, 204, 204);
                rtb.SelectionStart = rtb.TextLength;
                rtb.AppendText(msg + "\n");
            }
        }

        // keeps the chat at about MaxChatLines lines, trimming in batches
        private static void TrimChat(RichTextBox rtb)
        {
            int lines = rtb.GetLineFromCharIndex(rtb.TextLength) + 1;
            if (lines <= MaxChatLines + TrimChatBatch)
                return;

            int cut = rtb.GetFirstCharIndexFromLine(lines - MaxChatLines);
            if (cut > 0)
            {
                rtb.Select(0, cut);
                rtb.SelectedText = "";
            }
        }

        private static void ScrollToEnd(RichTextBox rtb)
        {
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.ScrollToCaret();
        }

        public static void AddChat(RichTextBox rtb, string tstr, string nick, string msg, string translation = null)
        {
            if (rtb.InvokeRequired)
            {
                if (rtb.IsDisposed || !rtb.IsHandleCreated)
                    return;

                // BeginInvoke keeps the order of messages and does not block the caller
                rtb.BeginInvoke(new UpdateRTBDelegate(AddChat), new object[] { rtb, tstr, nick, msg, translation });
            }
            else
            {
                AppendChatLine(rtb, tstr, nick, msg, translation);
                TrimChat(rtb);
                ScrollToEnd(rtb);
            }
        }

        // replaces the whole chat with the history in one go, redraw is off while doing so
        public static void SetChatHistory(RichTextBox rtb, List<(string time, string nick, string msg)> items, Func<string, string> translation_of = null)
        {
            if (rtb.InvokeRequired)
            {
                if (rtb.IsDisposed || !rtb.IsHandleCreated)
                    return;

                rtb.BeginInvoke(new Action<RichTextBox, List<(string time, string nick, string msg)>, Func<string, string>>(SetChatHistory), new object[] { rtb, items, translation_of });
                return;
            }

            SendMessage(rtb.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                rtb.Clear();
                foreach (var item in items)
                {
                    AppendChatLine(rtb, item.time, item.nick, item.msg, translation_of?.Invoke(item.msg));
                }
                TrimChat(rtb);
                ScrollToEnd(rtb);
            }
            finally
            {
                SendMessage(rtb.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                rtb.Invalidate();
            }
        }

        public static void ClearAll(System.Windows.Forms.ListBox LB, Object obj)
        {
            if (LB.InvokeRequired)
            {
                UpdateLBDelegate ulb = new UpdateLBDelegate(ClearAll);
                LB.Invoke(ulb, new object[] { LB, obj });
            }
            else
            {
                LB.Items.Clear();
            }
        }

        private const int MaxStoredMessages = 500;

        // all received messages (UI thread only), the chat shows the ones matching the filter
        private readonly List<(string time, string nick, string msg)> _messages = new List<(string time, string nick, string msg)>();

        private string CurrentFilter
        {
            get { return txtFilter.Text.Trim(); }
        }

        // free text, ignores case, matches the nick or the message text
        private static bool MatchesFilter((string time, string nick, string msg) m, string filter)
        {
            return filter.Length == 0
                || m.nick.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || m.msg.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateFilterLabel(string filter)
        {
            lblFilter.Text = filter.Length == 0
                ? "Filter"
                : "Filter: " + _messages.FindAll(m => MatchesFilter(m, filter)).Count + " of " + _messages.Count;
        }

        // redraws the chat from the stored messages, UI thread only
        private void RenderChat()
        {
            string filter = CurrentFilter;
            SetChatHistory(richChat, _messages.FindAll(m => MatchesFilter(m, filter)), TranslationOf);
            UpdateFilterLabel(filter);
        }

        // can be called from any thread
        private void AddMessage(string time, string nick, string msg)
        {
            if (InvokeRequired)
            {
                if (IsDisposed || !IsHandleCreated)
                    return;

                BeginInvoke(new Action<string, string, string>(AddMessage), time, nick, msg);
                return;
            }

            var entry = (time, nick, msg);
            _messages.Add(entry);
            if (_messages.Count > MaxStoredMessages)
            {
                _messages.RemoveRange(0, _messages.Count - MaxStoredMessages);
            }

            string filter = CurrentFilter;
            if (MatchesFilter(entry, filter))
            {
                AddChat(richChat, time, nick, msg, TranslationOf(msg));
            }
            UpdateFilterLabel(filter);

            QueueTranslations(new List<(string nick, string msg)> { (nick, msg) });
        }

        // ---- translation of the chat text (issue #132) ----

        // the translations by message text (UI thread only); "" = nothing to show (same language, service did not answer)
        private readonly Dictionary<string, string> _translations = new Dictionary<string, string>();
        private readonly HashSet<string> _translating = new HashSet<string>();

        // the translation to show instead of the text of a message, null when the translation is off or there is none
        private string TranslationOf(string msg)
        {
            return _settings.translate_enabled && _translations.TryGetValue(msg, out string translation) && translation.Length > 0
                ? translation
                : null;
        }

        // many answers come in a row (the whole chat): one redraw shortly after the last one
        private System.Windows.Forms.Timer _translation_redraw_timer;

        private void RedrawSoon()
        {
            if (_translation_redraw_timer == null)
            {
                _translation_redraw_timer = new System.Windows.Forms.Timer { Interval = 400 };
                _translation_redraw_timer.Tick += (s, e) =>
                {
                    _translation_redraw_timer.Stop();
                    RenderChat();
                };
            }

            _translation_redraw_timer.Stop();
            _translation_redraw_timer.Start();
        }

        // UI thread: asks the service for the messages that have no answer yet, in the background and in batches, and redraws
        // the chat when the answers are there
        private void QueueTranslations(List<(string nick, string msg)> items)
        {
            if (!_settings.translate_enabled)
                return;

            // what is sent: the message, or for a signal report only the free text a station added behind the fixed part
            // (the prefix stays as it is and is put in front of the translation)
            var texts = new List<(string msg, string prefix, string text)>();
            foreach (var item in items)
            {
                if (item.nick == "Chat" || _translations.ContainsKey(item.msg) || texts.Exists(t => t.msg == item.msg) || !_translating.Add(item.msg))
                    continue;

                if (ChatTranslator.TrySplitSigReport(item.msg, out string prefix, out string free_text))
                {
                    if (free_text.Length == 0)
                    {
                        _translating.Remove(item.msg);
                        _translations[item.msg] = "";   // nothing to translate
                        continue;
                    }

                    texts.Add((item.msg, prefix, free_text));
                }
                else
                {
                    texts.Add((item.msg, "", item.msg));
                }
            }
            if (texts.Count == 0)
                return;

            string language = _settings.translate_language;
            string key_file = _settings.translate_key_file;

            System.Threading.Tasks.Task.Run(async () =>
            {
                for (int start = 0; start < texts.Count; start += ChatTranslator.MaxTextsPerRequest)
                {
                    var batch = texts.GetRange(start, Math.Min(ChatTranslator.MaxTextsPerRequest, texts.Count - start));
                    string[] translations = await ChatTranslator.TranslateAsync(batch.ConvertAll(t => t.text), language, key_file).ConfigureAwait(false);

                    if (IsDisposed || !IsHandleCreated)
                        return;

                    BeginInvoke(new Action(() =>
                    {
                        if (_translations.Count > 2000)
                            _translations.Clear();

                        bool any = false;
                        for (int i = 0; i < batch.Count; i++)
                        {
                            _translating.Remove(batch[i].msg);
                            _translations[batch[i].msg] = translations[i] == null ? "" : batch[i].prefix + translations[i];
                            any |= translations[i] != null;
                        }

                        if (any && _settings.translate_enabled)
                            RedrawSoon();
                    }));
                }
            });
        }

        // all stored messages (at most MaxStoredMessages), when the translation is switched on and when the history arrives
        private void TranslateAllMessages()
        {
            QueueTranslations(_messages.ConvertAll(m => (m.nick, m.msg)));
        }

        private void checkTranslate_CheckedChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
                return;   // the start value from the settings, nothing to redraw yet

            _settings.translate_enabled = checkTranslate.Checked;
            RenderChat();
            TranslateAllMessages();
        }

        // replaces all stored messages, can be called from any thread
        private void SetHistory(List<(string time, string nick, string msg)> items)
        {
            if (InvokeRequired)
            {
                if (IsDisposed || !IsHandleCreated)
                    return;

                BeginInvoke(new Action<List<(string time, string nick, string msg)>>(SetHistory), items);
                return;
            }

            _messages.Clear();
            _messages.AddRange(items);
            if (_messages.Count > MaxStoredMessages)
            {
                _messages.RemoveRange(0, _messages.Count - MaxStoredMessages);
            }
            RenderChat();
            TranslateAllMessages();
        }

        // redraw shortly after the last keystroke, not on every letter
        private void txtFilter_TextChanged(object sender, EventArgs e)
        {
            tmrFilter.Stop();
            tmrFilter.Start();
        }

        private void tmrFilter_Tick(object sender, EventArgs e)
        {
            tmrFilter.Stop();
            RenderChat();
        }

        private void btnFilterClear_Click(object sender, EventArgs e)
        {
            txtFilter.Clear();
            tmrFilter.Stop();
            RenderChat();
            txtFilter.Focus();
        }

        private void initUsers(SocketIOResponse response)
        {
            ClearAll(lbUsers, "");

            var nicks = response.GetValue(0).GetProperty("nicks").EnumerateArray();

            foreach (System.Text.Json.JsonElement nick in nicks)
            {
                AddItem(lbUsers, nick.ToString());
            }
        }

        private void initHistory(SocketIOResponse response)
        {
            var history = response.GetValue(0).GetProperty("history").EnumerateArray();
            var items = new List<(string time, string nick, string msg)>();

            foreach (System.Text.Json.JsonElement hist_item in history)
            {
                string time = hist_item.GetProperty("time").ToString();
                DateTime timeobj = Convert.ToDateTime(time);

                items.Add((timeobj.ToString("HH:mm"), hist_item.GetProperty("name").ToString(), hist_item.GetProperty("message").ToString()));
            }

            Log.Information("Chat: history received, {Count} messages", items.Count);
            SetHistory(items);
        }

        private void onViewersCallback(SocketIOResponse response)
        {
            updateTitle(this, "QO-100 Wideband Chat - Viewers: " + response.GetValue(0).GetProperty("num").ToString());
        }

        private void onMessageCallback(SocketIOResponse response)
        {
            var newMessage = response.GetValue(0);
            string time = newMessage.GetProperty("time").ToString();
            DateTime timeobj = Convert.ToDateTime(time);
            //string newMsg = timeobj.ToString("HH:mm") + " <" + newMessage.GetProperty("name").ToString() + ">" + " " + newMessage.GetProperty("message").ToString();
            //AddItem(lbChat, newMsg);

            AddMessage(timeobj.ToString("HH:mm"), newMessage.GetProperty("name").ToString(), newMessage.GetProperty("message").ToString());
        }

        private void onNicksCallback(SocketIOResponse response)
        {
            initUsers(response);
        }

        private void onHistoryCallback(SocketIOResponse response)
        {
            initUsers(response);
            initHistory(response);
        }

        class nickInfo
        {
            [JsonPropertyName("nick")]
            public string nick { get; set; }
        }

        class chatMessage
        {
            [JsonPropertyName("message")]
            public string message { get; set; }
        }

        // A frame of 2 px in `color` around the control (instead of the 3D border of the control, which differs from
        // control to control), with 2 px of the window colour around the frame.
        private static void FrameControl(Control control, Color color)
        {
            Control parent = control.Parent;
            int index = parent.Controls.GetChildIndex(control);

            var outer = new Panel { BackColor = DarkTheme.Frame, Padding = new Padding(2), Dock = control.Dock };
            var inner = new Panel { BackColor = color, Padding = new Padding(2), Dock = DockStyle.Fill };
            if (control.Dock == DockStyle.Bottom || control.Dock == DockStyle.Top)
                outer.Height = control.Height + 8;

            if (control is TextBoxBase text_box)
                text_box.BorderStyle = BorderStyle.None;
            else if (control is ListBox list_box)
                list_box.BorderStyle = BorderStyle.None;

            parent.Controls.Add(outer);
            parent.Controls.SetChildIndex(outer, index);
            outer.Controls.Add(inner);
            inner.Controls.Add(control);
            control.Dock = DockStyle.Fill;
        }

        private void Client_OnConnected(object sender, EventArgs e)
        {
            Log.Information("Connected socketio (connects so far: {Count})", Interlocked.Increment(ref _connectCount));
            SetConnectedLabel(true);

            // also after a reconnect, the server does not remember the nick
            if (_settings.gui_autologin && !IsDisposed && IsHandleCreated)
            {
                BeginInvoke(new Action(setNick));
            }
        }

        // The own nick in the user list in the yellow of the nicks in the chat.
        private void lbUsers_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            string nick = lbUsers.Items[e.Index].ToString();
            bool own = _logged_in && string.Equals(nick.Trim(), _settings.nickname?.Trim(), StringComparison.OrdinalIgnoreCase);
            bool selected = (e.State & DrawItemState.Selected) != 0;

            using (var back = new SolidBrush(selected ? Color.FromArgb(90, 100, 108) : lbUsers.BackColor))
                e.Graphics.FillRectangle(back, e.Bounds);

            TextRenderer.DrawText(e.Graphics, nick, lbUsers.Font, e.Bounds, own ? Color.FromArgb(251, 222, 45) : lbUsers.ForeColor,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private bool _logged_in;

        // With a nick set the nick link in the footer is not needed any more; it is back when the connection is lost.
        private void ShowNickLink(bool visible)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            BeginInvoke(new Action(() =>
            {
                txtNick.Visible = visible;
                toolStripStatusLabel1.Visible = visible;
                lbUsers.Invalidate();
            }));
        }

        private void setNick()
        {
            if (client.Connected)
            {
                string nick = txtNick.Text.Trim();

                if (nick.Length > 0 && nick != "NONICK")
                {
                    client.EmitAsync("setnick", new nickInfo { nick = nick });
                    txtMessage.Enabled = true;
                    AddItem(lbChat, "*** Your nick is set to " + nick + " ***");
                    _settings.nickname = nick;
                    _logged_in = true;
                    ShowNickLink(false);
                }
            }
        }

        private void btnSetNick_Click(object sender, EventArgs e)
        {
            setNick();
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            sendMessage();
        }

        private void sendMessage()
        {
            if (client.Connected)
            {
                string msg = txtMessage.Text.Trim();

                if (msg.Length > 0)
                {
                    client.EmitAsync("message", new chatMessage { message = msg });
                }

                txtMessage.Text = "";
            }

        }

        private void txtMessage_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == 13)
            {
                sendMessage();
            }
        }

        private void wbchat_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_shutdown)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void lbUsers_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (txtMessage.Enabled && lbUsers.SelectedIndex >= 0)
            {
                string selectedName = lbUsers.SelectedItem.ToString();
                txtMessage.Text = txtMessage.Text + " @" + selectedName + " ";
            }
        }

        private void checkStayOnTop_CheckedChanged(object sender, EventArgs e)
        {
            
            if ( checkStayOnTop.Checked )
            {
                TopMost = true;
            }
            else
            {
                TopMost = false;
            }
           
        }

        private void copySelectedTextToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(richChat.SelectedText, TextDataFormat.UnicodeText);
        }

        private void txtNick_Click(object sender, EventArgs e)
        {
            setnickdialog nickDialog = new setnickdialog();

            nickDialog.txtNick.Text = txtNick.Text;

            _settings.nickname = txtNick.Text;

            if (nickDialog.ShowDialog() == DialogResult.OK)
            {
                txtNick.Text = nickDialog.txtNick.Text;
                setNick();

                if (txtNick.Text.Length > 0 && txtNick.Text != "NONICK")
                {
                    DateTime timeobj = DateTime.Now;
                    AddMessage(timeobj.ToString("HH:mm"), "Chat", "You are now known as '" + txtNick.Text + "'");
                }
            }
        }

        private void lbChat_Resize(object sender, EventArgs e)
        {
        }

        private void richChat_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to follow this link?", "Warning", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                opentuner.Utilities.CommonFunctions.OpenUrl(e.LinkText);
            }
        }

        private void lbUsers_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lbUsers.SelectedIndex > 0)
            {
                // from Chris - DH3CS
                string user = lbUsers.GetItemText(lbUsers.SelectedItem);

                try
                {
                    richChat.SelectionStart = 0;
                    richChat.SelectionLength = richChat.Text.Length - 1;
                    richChat.SelectionBackColor = Color.FromArgb(63, 70, 76);
                    // set the current caret position to the end, if nothing will be found
                    richChat.SelectionStart = richChat.Text.Length;

                    foreach (Match match in Regex.Matches(richChat.Text, user))
                    {
                        richChat.SelectionStart = match.Index;
                        richChat.SelectionLength = match.Length;
                        richChat.SelectionBackColor = Color.FromArgb(0, 255, 0);
                    }
                    // scroll it automatically
                    richChat.ScrollToCaret();
                }
                catch { }
            }
            else
            {
                richChat.SelectionStart = 0;
                richChat.SelectionLength = richChat.Text.Length - 1;
                richChat.SelectionBackColor = Color.FromArgb(63, 70, 76);
                richChat.SelectionStart = richChat.Text.Length;
                richChat.ScrollToCaret();
            }
        }

        private void lbUsers_MouseClick(object sender, MouseEventArgs e)
        {
        }

        private void lbUsers_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { lbUsers.SelectedIndex = -1; }
        }

        private void selectAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            txtMessage.Text = Clipboard.GetText();
        }

        private void getSignalReportData(int tuner)
        {
            var data = _source.GetSignalData(tuner);

            //string signalReport = "SigReport: " + lblServiceName.Text.ToString() + "/" + lblServiceProvider.Text.ToString() + " - " + lbldbMargin.Text.ToString() + " (" + lblMer.Text.ToString() + ") - " + lblSR.Text.ToString() + "" + " - " + (freq).ToString() + " ";
            string signalReport = _settings.sigreport_template.ToString();

            // SigReport: {SN}/{SP} - {DBM} - ({MER}) - {SR} - {VCODEC} - {FREQ} - {TSERR}

            signalReport = signalReport.Replace("{SN}", data["ServiceName"]);
            signalReport = signalReport.Replace("{SP}", data["ServiceProvider"]);
            signalReport = signalReport.Replace("{DBM}", data["dbMargin"]);
            signalReport = signalReport.Replace("{MER}", data["Mer"] + " dB");
            signalReport = signalReport.Replace("{SR}", data["SR"] + "");
            signalReport = signalReport.Replace("{VCODEC}", MediaStatus.CodecDisplayName(data["VideoCodec"] + ""));
            signalReport = signalReport.Replace("{FREQ}", data["Frequency"] + "");

            // TS errors of the last few seconds (MiniTiouner only, other sources don't provide them). Without any
            // errors the placeholder and the " - " in front of it are removed, so a clean report stays as it was.
            string ts_errors;
            if (!data.TryGetValue("TSErrors", out ts_errors))
                ts_errors = "";

            if (ts_errors.Length > 0)
                signalReport = signalReport.Replace("{TSERR}", "TS errors: " + ts_errors);
            else
                signalReport = Regex.Replace(signalReport, @"(\s*-)?\s*\{TSERR\}", "");

            txtMessage.Text = signalReport;

            Clipboard.SetText(signalReport);
        }

        private string _sig_report_text = "";

        // The report button that made the text in the input field is blue (active) until the text is sent or changed.
        private void MarkSigReportButton(FlexButton button)
        {
            _sig_report_text = button != null ? txtMessage.Text : "";

            foreach (var b in new[] { btnSigReportTuner1, btnSigReportTuner2, btnSigReportTuner3, btnSigReportTuner4 })
                b.Active = b == button;
        }

        private void SigReport(int tuner, FlexButton button)
        {
            getSignalReportData(tuner);
            MarkSigReportButton(txtMessage.Text.Length > 0 ? button : null);
        }

        private void btnSigReportTuner1_Click(object sender, EventArgs e)
        {
            SigReport(0, btnSigReportTuner1);
        }

        private void btnSigReportTuner2_Click(object sender, EventArgs e)
        {
            SigReport(1, btnSigReportTuner2);
        }

        private void btnSigReportTuner3_Click(object sender, EventArgs e)
        {
            SigReport(2, btnSigReportTuner3);
        }

        private void btnSigReportTuner4_Click(object sender, EventArgs e)
        {
            SigReport(3, btnSigReportTuner4);
        }
    }
}
