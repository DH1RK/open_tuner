namespace opentuner.ExtraFeatures.BATCWebchat
{
    public class WebChatSettings
    {
        // chat settings        
        public string sigreport_template = "SigReport: {SN}/{SP} - {DBM} - ({MER}) - {SR} - {VCODEC} - {FREQ} - {TSERR}";
        public string nickname = "NONICK";
        public int chat_font_size = 10;
        public int gui_chat_width = 800;
        public int gui_chat_height = 600;
        public int gui_chat_seperator = 220;
        public bool gui_autostart = false;
        public bool gui_autologin = false;
        public int gui_chat_x = 0;
        public int gui_chat_y = 0;
        public int gui_chat_windowstate = 0;

        // translation of the chat text (issue #132): on/off, the language to translate into (DeepL code, see ChatTranslator.Languages)
        // and the file with the DeepL API key (own key of the user, not part of these settings; empty = ChatTranslator.DefaultKeyFile)
        public bool translate_enabled = false;
        public string translate_language = "DE";
        public string translate_key_file = "";

        // keyword alert (issue #259): a short system sound when one of the keywords (at most KeywordCount) is in the text of a new message (not in the nick of the sender)
        public const int KeywordCount = 5;
        public bool alert_enabled = false;
        public string[] alert_keywords = new string[0];
    }
}
