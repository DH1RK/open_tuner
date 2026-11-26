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
    // Translates chat messages into the language of the user with DeepL (issue #132) or Azure Translator of Microsoft
    // (issue #270). The API key is the own key of the user; it is read from a file (not kept in the settings), by default
    // ".deepl_key" or ".azure_translator_key" in the user directory. The text of the messages is sent to the chosen service.
    // An entry of the result is null when nothing is to be shown: already in the target language, too short, no key, the
    // service not reachable, quota used up.
    public static class ChatTranslator
    {
        // the translation services, as they are kept in the settings and shown in the drop-down list
        public const string DeepL = "DeepL";
        public const string Azure = "Azure";
        public static readonly string[] Services = { DeepL, Azure };

        // the service of a setting, DeepL for an empty or unknown one
        public static string ServiceName(string setting)
        {
            return string.Equals(setting, Azure, StringComparison.OrdinalIgnoreCase) ? Azure : DeepL;
        }

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

        // Words that are no words of a language but ham radio jargon ("de" = from, "ga" = go ahead, Q codes ...) and callsigns:
        // they are sent to DeepL inside <x> tags that DeepL leaves alone (ignore_tags), so "ga all" does not become a sentence.
        // (the greetings with "all" behind them are one piece: "ga all", "Gm All", "73 all")
        private static readonly Regex JargonWords = new Regex(
            @"\b(de|ga|gm|ge|gn|gl|gd|tnx|tks|73|88)(\s+(all|alle|everybody|everyone))?\b|\b(cq|qso|qrz|qsl|qth|qrm|qrn|qsb|qrp|qsy|rst|pse|agn|sk|om|fb|dish|bullseye)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // a callsign: one to three characters, a digit, one to four letters (DH1RK, 9A3ABC, G4ABC), at least one letter and one digit
        private static readonly Regex Callsign = new Regex(
            @"\b(?=[A-Z0-9]*\d)(?=[A-Z0-9]*[A-Z])[A-Z0-9]{1,3}\d[A-Z]{1,4}\b(/[A-Z0-9]{1,4})?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // the text for the service: XML characters escaped, jargon and callsigns in <x> tags (Azure gets its own tags, ForAzure)
        private static string Protect(string text, string target_language)
        {
            string escaped = System.Security.SecurityElement.Escape(text) ?? "";
            escaped = Callsign.Replace(escaped, "<x>$0</x>");
            escaped = JargonWords.Replace(escaped, m => "<x>" + m.Value + "</x>");

            if (target_language != null && target_language.StartsWith("DE", StringComparison.OrdinalIgnoreCase))
                // for a German reader "bin" is German ("ich bin"); in the mixed German / English lines DeepL would take it for the English "bin"
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
            string plain = Regex.Replace(text ?? "", @"</?(x|span)\b[^>]*>", "");
            return System.Net.WebUtility.HtmlDecode(plain);
        }

        // DeepL takes up to 50 texts in one request, Azure more
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

        // C:\Users\<name>\.deepl_key or C:\Users\<name>\.azure_translator_key
        public static string DefaultKeyFile(string service)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ServiceName(service) == Azure ? ".azure_translator_key" : ".deepl_key");
        }

        // the file of the setting, the default of the service when the setting is empty
        public static string KeyFilePath(string setting, string service)
        {
            return string.IsNullOrWhiteSpace(setting) ? DefaultKeyFile(service) : setting.Trim();
        }

        // the first line of the key file, "" when there is no file or it is empty
        public static string ReadKey(string path)
        {
            return ReadKeyFileLine(path, 0);
        }

        // a line of the key file (0 = the key, 1 = the region of an Azure key), "" when it is not there
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
        private static string ProblemText(string service, Exception ex)
        {
            string message = ex.Message ?? "";
            if (service == Azure)
            {
                if (message.Contains("code 403001"))
                    return "Azure: the free character limit of the key is used up (answer 403, code 403001)";
                if (message.Contains(" 401 "))
                    return "Azure: the key or its region is not accepted (answer 401)";
                if (message.Contains(" 403 "))
                    return "Azure: the request is not allowed, or the character limit is used up (answer 403)";
            }
            else
            {
                if (message.Contains(" 456 "))
                    return "DeepL: the character limit of the key is used up (answer 456)";
                if (message.Contains(" 403 "))
                    return "DeepL: the key is not accepted (answer 403)";
            }
            if (message.Contains(" 429 "))
                return service + ": too many requests (answer 429)";
            return service + ": " + message;
        }
        private static bool _no_key_reported = false;

        // translates the texts (at most MaxTextsPerRequest); the result has one entry per text, null = nothing to show
        public static async Task<string[]> TranslateAsync(IList<string> texts, string target_language, string service_setting, string key_file_setting)
        {
            var result = new string[texts.Count];
            string service = ServiceName(service_setting);

            // too short texts are not sent
            var send_index = new List<int>();
            for (int i = 0; i < texts.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(texts[i]) && texts[i].Trim().Length >= 3 && HasTranslatableText(Protect(texts[i], target_language)))
                    send_index.Add(i);
            }
            if (send_index.Count == 0)
                return result;

            string key_file = KeyFilePath(key_file_setting, service);
            string key = ReadKey(key_file);
            if (key.Length == 0)
            {
                SetProblem("no " + service + " key in " + key_file);
                if (!_no_key_reported)
                {
                    _no_key_reported = true;
                    Log.Warning("Chat translation: no {Service} key in {File}", service, key_file);
                }
                return result;
            }
            _no_key_reported = false;

            await OneAtATime.WaitAsync().ConfigureAwait(false);
            try
            {
                var send = new List<string>();
                foreach (int i in send_index)
                    send.Add(Protect(texts[i], target_language));

                string[] translated = service == Azure
                    ? await AzureAsync(send, target_language, key, ReadKeyFileLine(key_file, 1)).ConfigureAwait(false)
                    : await DeepLAsync(send, target_language, key).ConfigureAwait(false);

                int shown = 0;
                for (int n = 0; n < send_index.Count; n++)
                {
                    result[send_index[n]] = translated[n];
                    if (translated[n] != null)
                        shown++;
                }

                Log.Information("Chat translation ({Service}, to {Language}): {Count} message(s), {Chars} characters, {Shown} translated",
                    service, target_language, send.Count, TotalLength(send), shown);
                _failures = 0;
                SetProblem(null);
            }
            catch (Exception ex)
            {
                SetProblem(ProblemText(service, ex));
                // log the first failures only, a service that is down must not fill the log
                if (Interlocked.Increment(ref _failures) <= 3)
                    Log.Warning("Chat translation ({Service}) failed: {Message}", service, ex.Message);
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

        // the target language of the drop-down list (a DeepL code) as Azure names it
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
                                ? null
                                : Unprotect(item.GetProperty("translations")[0].GetProperty("text").GetString());
                            n++;
                        }

                        return result;
                    }
                }
            }
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
            form.Add(new KeyValuePair<string, string>("tag_handling", "xml"));
            form.Add(new KeyValuePair<string, string>("ignore_tags", "x"));

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
                            result[n] = SameLanguage(detected, target_language) ? null : Unprotect(item.GetProperty("text").GetString());
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
