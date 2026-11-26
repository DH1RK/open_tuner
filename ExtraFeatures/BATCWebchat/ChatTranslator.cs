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
    // Translates chat messages into the language of the user with Azure Translator of Microsoft (issue #270; the chat
    // translation of issue #132 used DeepL before). The API key is the own key of the user; it is read from a file (not kept
    // in the settings), by default ".azure_translator_key" in the user directory. The text of the messages is sent to
    // Microsoft. The answers are kept on disk (TranslationCache), a text is sent only once.
    // An entry of the result is null when nothing is to be shown: already in the target language, too short, no key, the
    // service not reachable, quota used up.
    public static class ChatTranslator
    {
        // the target languages of the drop-down list: name, the code kept in the settings (AzureLanguage: as Azure names it)
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

        // Words that are no words of a language but ham radio jargon ("ga" = go ahead, Q codes ...) and callsigns:
        // they are put inside <x> tags (ForAzure) that the service leaves alone, so "ga all" does not become a sentence.
        // (the greetings with "all" behind them are one piece: "ga all", "Gm All", "73 all")
        private static readonly Regex JargonWords = new Regex(
            @"\b(ga|gm|ge|gn|gl|gd|tnx|tks|73|88)(\s+(all|alle|everybody|everyone))?\b|\b(cq|qso|qrz|qsl|qth|qrm|qrn|qsb|qrp|qsy|rst|pse|agn|sk|om|fb|dish|bullseye)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "de" (= from) only in front of a callsign (already in its tags, also with a prefix "GM/"): it is a French, Spanish
        // and Dutch word too
        private static readonly Regex FromCallsign = new Regex(
            @"\bde(?=\s+([A-Z0-9]{1,4}/)?<x>)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // a callsign: one to three characters, a digit, one to four letters (DH1RK, 9A3ABC, G4ABC), at least one letter and one digit,
        // with a short part behind a "/" (/P, /MM) only when that is a whole word (not the "Plut" of "CT1EJC/PlutoDVB2")
        private static readonly Regex Callsign = new Regex(
            @"\b(?=[A-Z0-9]*\d)(?=[A-Z0-9]*[A-Z])[A-Z0-9]{1,3}\d[A-Z]{1,4}\b(/[A-Z0-9]{1,4}\b)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // the protected text (also the key of the cache): XML characters escaped, jargon and callsigns in <x> tags
        private static string Protect(string text, string target_language)
        {
            string escaped = System.Security.SecurityElement.Escape(text) ?? "";
            escaped = Callsign.Replace(escaped, "<x>$0</x>");
            escaped = FromCallsign.Replace(escaped, "<x>$0</x>");   // before the jargon gets its tags
            escaped = JargonWords.Replace(escaped, m => "<x>" + m.Value + "</x>");

            if (target_language != null && target_language.StartsWith("DE", StringComparison.OrdinalIgnoreCase))
                // for a German reader "bin" is German ("ich bin"); in mixed German / English lines it must not be taken for the English "bin"
                escaped = Regex.Replace(escaped, @"(?<!<x>)\bbin\b(?!</x>)", m => "<x>" + m.Value + "</x>", RegexOptions.IgnoreCase);

            return escaped;
        }

        // true when something is left to translate after the protected parts are taken out
        private static bool HasTranslatableText(string protected_text)
        {
            string rest = Regex.Replace(protected_text, "<x>.*?</x>", "");
            return Regex.IsMatch(rest, @"[^\W\d_]{2,}");
        }

        // Azure leaves the text of an HTML element of the class "notranslate" alone
        private static string ForAzure(string protected_text)
        {
            return protected_text.Replace("<x>", "<span class=\"notranslate\">").Replace("</x>", "</span>");
        }

        // the answer of the service without the protecting tags and with the characters unescaped (the text of the message
        // was escaped, so every tag left is one of ours)
        private static string Unprotect(string text)
        {
            // Azure may drop the blank next to a protected word ("auf die dishgesetzt"): two or more letters which stick to
            // it get one again (not a single one, "Jims"). In the text sent no letter touches a protected word.
            string spaced = Regex.Replace(text ?? "", @"</(x|span)>(?=\p{L}{2})", "$0 ");
            spaced = Regex.Replace(spaced, @"(?<=\p{L}{2})(?=<(x|span)\b)", " ");

            string plain = Regex.Replace(spaced, @"</?(x|span)\b[^>]*>", "");
            return System.Net.WebUtility.HtmlDecode(plain);
        }

        // the chat history is sent in portions of this size (the service would take more)
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

        // C:\Users\<name>\.azure_translator_key
        public static string DefaultKeyFile()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure_translator_key");
        }

        // the file of the setting, the default when the setting is empty
        public static string KeyFilePath(string setting)
        {
            return string.IsNullOrWhiteSpace(setting) ? DefaultKeyFile() : setting.Trim();
        }

        // the first line of the key file, "" when there is no file or it is empty
        public static string ReadKey(string path)
        {
            return ReadKeyFileLine(path, 0);
        }

        // a line of the key file (0 = the key, 1 = its region), "" when it is not there
        private static string ReadKeyFileLine(string path, int index)
        {
            try
            {
                if (!File.Exists(path))
                    return "";

                using (var reader = new StreamReader(path))
                {
                    for (int i = 0; i < index; i++)
                        reader.ReadLine();
                    return (reader.ReadLine() ?? "").Trim();
                }
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

        // What is wrong with the translation at the moment (no key, the service not reachable, the character limit used up),
        // null when the last request was fine. The chat window shows it at its "Translate" check box (#227). Raised from any
        // thread.
        public static string Problem { get; private set; }
        public static event Action ProblemChanged;

        private static void SetProblem(string text)
        {
            if (Problem == text)
                return;
            Problem = text;
            ProblemChanged?.Invoke();
        }

        // after a change of the service, language or key file: an old problem does not say anything about the new ones
        public static void ResetProblem()
        {
            _failures = 0;
            _no_key_reported = false;
            SetProblem(null);
        }

        // the reason in words, for the tooltip
        private static string ProblemText(Exception ex)
        {
            string message = ex.Message ?? "";
            if (message.Contains("code 403001"))
                return "Azure: the free character limit of the key is used up (answer 403, code 403001)";
            if (message.Contains(" 401 "))
                return "Azure: the key or its region is not accepted (answer 401)";
            if (message.Contains(" 403 "))
                return "Azure: the request is not allowed, or the character limit is used up (answer 403)";
            if (message.Contains(" 429 "))
                return "Azure: too many requests (answer 429)";
            return "Azure: " + message;
        }
        private static bool _no_key_reported = false;

        // translates the texts (at most MaxTextsPerRequest); the result has one entry per text, null = nothing to show
        public static async Task<string[]> TranslateAsync(IList<string> texts, string target_language, string key_file_setting)
        {
            var result = new string[texts.Count];

            // too short texts are not sent; "open" = the texts without an answer yet, as they are sent
            var open = new List<(int index, string text)>();
            for (int i = 0; i < texts.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(texts[i]) || texts[i].Trim().Length < 3)
                    continue;

                string protected_text = Protect(texts[i], target_language);
                if (HasTranslatableText(protected_text))
                    open.Add((i, protected_text));
            }
            if (open.Count == 0)
                return result;

            // what the service answered before comes from the cache (#270)
            int wanted = open.Count;
            TranslationCache cache = TranslationCache.For(target_language);
            cache.Refresh();

            bool AllCached()
            {
                open.RemoveAll(entry =>
                {
                    if (!cache.TryGet(entry.text, out string translation))
                        return false;
                    result[entry.index] = translation.Length == 0 ? null : translation;
                    return true;
                });
                return open.Count == 0;
            }

            void LogAllCached()
            {
                Log.Information("Chat translation (to {Language}): {Cached} message(s) from the cache, nothing sent",
                    target_language, wanted);
            }

            if (AllCached())
            {
                LogAllCached();
                return result;
            }

            string key_file = KeyFilePath(key_file_setting);
            string key = ReadKey(key_file);
            if (key.Length == 0)
            {
                SetProblem("no Azure Translator key in " + key_file + " (how to get one: SETUP.md)");
                if (!_no_key_reported)
                {
                    _no_key_reported = true;
                    Log.Warning("Chat translation: no Azure Translator key in {File}", key_file);
                }
                return result;
            }
            _no_key_reported = false;

            await OneAtATime.WaitAsync().ConfigureAwait(false);
            try
            {
                // only one instance at a time asks the service, the others then find its answers in the cache
                using (await cache.LockAsync(AllCached).ConfigureAwait(false))
                {
                    cache.Refresh();
                    if (AllCached())
                    {
                        LogAllCached();
                        return result;
                    }

                    // the same text twice is sent once
                    var send = new List<string>();
                    foreach (var entry in open)
                    {
                        if (!send.Contains(entry.text))
                            send.Add(entry.text);
                    }

                    string[] translated = await AzureAsync(send, target_language, key, ReadKeyFileLine(key_file, 1)).ConfigureAwait(false);

                    var answers = new List<KeyValuePair<string, string>>();
                    int shown = 0;
                    for (int n = 0; n < send.Count; n++)
                    {
                        if (translated[n] == null)
                            continue;   // the service gave no answer for this one
                        answers.Add(new KeyValuePair<string, string>(send[n], translated[n]));
                        if (translated[n].Length > 0)
                            shown++;
                    }
                    cache.Add(answers);

                    int cached = wanted - open.Count;
                    AllCached();

                    Log.Information("Chat translation (to {Language}): {Count} message(s) sent, {Chars} characters, {Shown} translated, {Cached} from the cache",
                        target_language, send.Count, TotalLength(send), shown, cached);
                    _failures = 0;
                    SetProblem(null);
                }
            }
            catch (Exception ex)
            {
                SetProblem(ProblemText(ex));
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

        // the target language of the drop-down list (the code of the settings) as Azure names it
        private static string AzureLanguage(string target_language)
        {
            switch ((target_language ?? "").ToUpperInvariant())
            {
                case "EN-GB": return "en";
                case "PT-PT": return "pt-pt";
                case "ZH": return "zh-Hans";
                default: return (target_language ?? "").ToLowerInvariant();
            }
        }

        // Azure Translator, text translation 3.0. A key of a regional resource needs its region, a key of a "global" one does not.
        // The result has one entry per text, "" = already in the target language, null = no answer.
        private static async Task<string[]> AzureAsync(List<string> texts, string target_language, string key, string region)
        {
            string url = "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&textType=html&to="
                + Uri.EscapeDataString(AzureLanguage(target_language));

            var body = new List<Dictionary<string, string>>();
            foreach (string text in texts)
                body.Add(new Dictionary<string, string> { { "Text", ForAzure(text) } });

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", key);
                if (region.Length > 0 && !string.Equals(region, "global", StringComparison.OrdinalIgnoreCase))
                    request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", region);
                request.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

                using (var response = await Http.SendAsync(request).ConfigureAwait(false))
                {
                    string answer = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("Azure answered " + (int)response.StatusCode + " (" + response.ReasonPhrase + ")" + AzureErrorCode(answer));

                    TranslationUsage.Add(ChargedCharacters(response, texts));

                    using (var json = JsonDocument.Parse(answer))
                    {
                        var result = new string[texts.Count];
                        int n = 0;
                        foreach (JsonElement item in json.RootElement.EnumerateArray())
                        {
                            if (n >= result.Length)
                                break;

                            string detected = item.TryGetProperty("detectedLanguage", out var d) && d.TryGetProperty("language", out var l) ? l.GetString() : "";
                            result[n] = SameLanguage(detected, target_language)
                                ? ""
                                : Unprotect(item.GetProperty("translations")[0].GetProperty("text").GetString());
                            n++;
                        }

                        return result;
                    }
                }
            }
        }

        // the characters Azure charges for this answer, as it says itself (the protecting tags count too), else the length of
        // what was sent
        private static long ChargedCharacters(HttpResponseMessage response, List<string> texts)
        {
            if (response.Headers.TryGetValues("X-Metered-Usage", out IEnumerable<string> values))
            {
                foreach (string value in values)
                {
                    if (long.TryParse(value, out long charged))
                        return charged;
                }
            }

            long length = 0;
            foreach (string text in texts)
                length += ForAzure(text).Length;
            return length;
        }

        // ", code 403001" from the error answer of Azure {"error":{"code":403001,...}}, "" when there is none
        private static string AzureErrorCode(string answer)
        {
            try
            {
                using (var json = JsonDocument.Parse(answer))
                {
                    return ", code " + json.RootElement.GetProperty("error").GetProperty("code").ToString();
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static bool SameLanguage(string detected, string target)
        {
            return !string.IsNullOrEmpty(detected)
                && string.Equals(detected.Split('-')[0], target.Split('-')[0], StringComparison.OrdinalIgnoreCase);
        }
    }
}
