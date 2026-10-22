using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace opentuner.ExtraFeatures.BATCWebchat
{
    // Translates chat messages into the language of the user with DeepL (issue #132). The DeepL API key is the own key of
    // the user; it is read from a file (not kept in the settings), by default ".deepl_key" in the user directory. The text
    // of the messages is sent to DeepL. An entry of the result is null when nothing is to be shown: already in the target
    // language, too short, no key, DeepL not reachable, quota used up.
    public static class ChatTranslator
    {
        // the target languages of the drop-down list: name, DeepL code
        public static readonly KeyValuePair<string, string>[] Languages = new[]
        {
            new KeyValuePair<string, string>("Deutsch", "DE"),
            new KeyValuePair<string, string>("English", "EN-GB"),
            new KeyValuePair<string, string>("Français", "FR"),
            new KeyValuePair<string, string>("Español", "ES"),
            new KeyValuePair<string, string>("Italiano", "IT"),
            new KeyValuePair<string, string>("Nederlands", "NL"),
            new KeyValuePair<string, string>("Português", "PT-PT"),
            new KeyValuePair<string, string>("Polski", "PL"),
            new KeyValuePair<string, string>("Čeština", "CS"),
            new KeyValuePair<string, string>("Svenska", "SV"),
            new KeyValuePair<string, string>("Dansk", "DA"),
            new KeyValuePair<string, string>("Russian", "RU"),
            new KeyValuePair<string, string>("Türkçe", "TR"),
            new KeyValuePair<string, string>("Japanese", "JA"),
            new KeyValuePair<string, string>("Chinese", "ZH"),
        };

        // DeepL takes up to 50 texts in one request
        public const int MaxTextsPerRequest = 25;

        // The fixed part of a signal report ("SigReport: {SN}/{SP} - {DBM} - ({MER}) - {SR} - {VCODEC} - {FREQ} - {TSERR}"): from the
        // start up to the frequency (the first number with a decimal point of three or more digits before it, e.g. 10494.752) and
        // the optional TS errors ("- TS errors: CC 12 TEI 1 SYNC 3 BUF 2 ENC"). What a station adds behind it is free text.
        private static readonly Regex SigReportHead = new Regex(
            @"^\s*SigReport:.*?\b\d{3,5}\.\d{1,4}\b(\s*-\s*TS errors:(\s+(CC \d+|TEI \d+|SYNC \d+|BUF \d+|ENC))*)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly char[] SeparatorChars = { ' ', '\t', '-', '–', '—', ':' };

        // For a signal report: prefix = the fixed part with the separators behind it (stays as it is), free_text = what a station
        // added after it (may be empty). False when the message is not a signal report.
        public static bool TrySplitSigReport(string msg, out string prefix, out string free_text)
        {
            prefix = "";
            free_text = "";

            Match head = SigReportHead.Match(msg);
            if (!head.Success)
                return false;

            string rest = msg.Substring(head.Length).TrimStart(SeparatorChars);
            prefix = msg.Substring(0, msg.Length - rest.Length);
            free_text = rest.TrimEnd();
            return true;
        }

        // C:\Users\<name>\.deepl_key
        public static string DefaultKeyFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".deepl_key"); }
        }

        // the file of the setting, the default when the setting is empty
        public static string KeyFilePath(string setting)
        {
            return string.IsNullOrWhiteSpace(setting) ? DefaultKeyFile : setting.Trim();
        }

        // the first line of the key file, "" when there is no file or it is empty
        public static string ReadKey(string key_file_setting)
        {
            try
            {
                string path = KeyFilePath(key_file_setting);
                if (!File.Exists(path))
                    return "";

                using (var reader = new StreamReader(path))
                    return (reader.ReadLine() ?? "").Trim();
            }
            catch (Exception ex)
            {
                Log.Warning("Chat translation: the key file could not be read: {Message}", ex.Message);
                return "";
            }
        }

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        // one request at a time, the chat does not hammer the service
        private static readonly SemaphoreSlim OneAtATime = new SemaphoreSlim(1, 1);

        private static int _failures = 0;
        private static bool _no_key_reported = false;

        // translates the texts (at most MaxTextsPerRequest); the result has one entry per text, null = nothing to show
        public static async Task<string[]> TranslateAsync(IList<string> texts, string target_language, string key_file_setting)
        {
            var result = new string[texts.Count];

            // too short texts are not sent
            var send_index = new List<int>();
            for (int i = 0; i < texts.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(texts[i]) && texts[i].Trim().Length >= 3)
                    send_index.Add(i);
            }
            if (send_index.Count == 0)
                return result;

            string key = ReadKey(key_file_setting);
            if (key.Length == 0)
            {
                if (!_no_key_reported)
                {
                    _no_key_reported = true;
                    Log.Warning("Chat translation: no DeepL key in {File}", KeyFilePath(key_file_setting));
                }
                return result;
            }
            _no_key_reported = false;

            await OneAtATime.WaitAsync().ConfigureAwait(false);
            try
            {
                var send = new List<string>();
                foreach (int i in send_index)
                    send.Add(texts[i]);

                string[] translated = await DeepLAsync(send, target_language, key).ConfigureAwait(false);

                int shown = 0;
                for (int n = 0; n < send_index.Count; n++)
                {
                    result[send_index[n]] = translated[n];
                    if (translated[n] != null)
                        shown++;
                }

                Log.Information("Chat translation (to {Language}): {Count} message(s), {Chars} characters, {Shown} translated",
                    target_language, send.Count, TotalLength(send), shown);
                _failures = 0;
            }
            catch (Exception ex)
            {
                // log the first failures only, a service that is down must not fill the log
                if (Interlocked.Increment(ref _failures) <= 3)
                    Log.Warning("Chat translation failed: {Message}", ex.Message);
            }
            finally
            {
                OneAtATime.Release();
            }

            return result;
        }

        private static int TotalLength(List<string> texts)
        {
            int total = 0;
            foreach (string text in texts)
                total += text.Length;
            return total;
        }

        private static async Task<string[]> DeepLAsync(List<string> texts, string target_language, string key)
        {
            // keys of the free plan end with ":fx" and use their own address
            string url = key.EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
                ? "https://api-free.deepl.com/v2/translate"
                : "https://api.deepl.com/v2/translate";

            var form = new List<KeyValuePair<string, string>>();
            foreach (string text in texts)
                form.Add(new KeyValuePair<string, string>("text", text));
            form.Add(new KeyValuePair<string, string>("target_lang", target_language));

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + key);
                request.Content = new FormUrlEncodedContent(form);

                using (var response = await Http.SendAsync(request).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("DeepL answered " + (int)response.StatusCode + " (" + response.ReasonPhrase + ")");

                    using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)))
                    {
                        var result = new string[texts.Count];
                        int n = 0;
                        foreach (JsonElement item in json.RootElement.GetProperty("translations").EnumerateArray())
                        {
                            if (n >= result.Length)
                                break;

                            string detected = item.TryGetProperty("detected_source_language", out var d) ? d.GetString() : "";
                            result[n] = SameLanguage(detected, target_language) ? null : item.GetProperty("text").GetString();
                            n++;
                        }

                        return result;
                    }
                }
            }
        }

        private static bool SameLanguage(string detected, string target)
        {
            return !string.IsNullOrEmpty(detected)
                && string.Equals(detected.Split('-')[0], target.Split('-')[0], StringComparison.OrdinalIgnoreCase);
        }
    }
}
