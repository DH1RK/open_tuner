using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace opentuner.ExtraFeatures.BATCWebchat
{
    // The answers of the translation service, kept on disk, so that a chat line is sent only once, also after a restart
    // (issue #270). One file per service and target language in settings\translation_cache, shared by all OpenTuner
    // instances in the folder. The key is the text as it is sent (with the protected parts): a text which is protected in
    // another way is asked again. An empty translation means "nothing to show" (already in the target language).
    public sealed class TranslationCache
    {
        public const int MaxEntries = 5000;     // with more the oldest ones are dropped ...
        public const int KeptEntries = 4000;    // ... down to this number

        // how long an instance waits for another one which is asking the service
        private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(60);

        private static readonly Dictionary<string, TranslationCache> Caches =
            new Dictionary<string, TranslationCache>(StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions FileFormat =
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        // the cache of a service and target language
        public static TranslationCache For(string service, string target_language)
        {
            string name = service + "_" + target_language;
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            lock (Caches)
            {
                if (!Caches.TryGetValue(name, out TranslationCache cache))
                {
                    cache = new TranslationCache(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "settings", "translation_cache", name + ".json"));
                    Caches.Add(name, cache);
                }
                return cache;
            }
        }

        private readonly string _file;
        private readonly object _sync = new object();
        private Dictionary<string, string> _entries = new Dictionary<string, string>(StringComparer.Ordinal);
        private List<string> _order = new List<string>();               // the texts, the oldest first
        private readonly List<string> _unsaved = new List<string>();    // not in the file yet (it could not be written)
        private (DateTime time, long length) _stamp;                   // of the file as it was read or written last
        private bool _write_failure_reported;

        private TranslationCache(string file)
        {
            _file = file;
        }

        // reads the file again when it changed (written by another instance)
        public void Refresh()
        {
            lock (_sync)
                ReadFileWhenChanged();
        }

        public bool TryGet(string text, out string translation)
        {
            lock (_sync)
                return _entries.TryGetValue(text, out translation);
        }

        // adds answers of the service (text sent, translation) and writes the file
        public void Add(IEnumerable<KeyValuePair<string, string>> answers)
        {
            lock (_sync)
            {
                ReadFileWhenChanged();

                foreach (var answer in answers)
                {
                    if (!_entries.ContainsKey(answer.Key))
                        _order.Add(answer.Key);
                    _entries[answer.Key] = answer.Value;
                    _unsaved.Add(answer.Key);
                }

                if (_order.Count > MaxEntries)
                {
                    int drop = _order.Count - KeptEntries;
                    for (int i = 0; i < drop; i++)
                        _entries.Remove(_order[i]);
                    _order.RemoveRange(0, drop);
                }

                WriteFile();
            }
        }

        // The lock of this cache for all instances: while one instance asks the service, the others wait and then find the
        // answers in the file. Null when there is nothing left to wait for (done) or the lock can not be had.
        public async Task<IDisposable> LockAsync(Func<bool> done)
        {
            var waited = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_file));
                    return new FileStream(_file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    // another instance has it
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }

                if (waited.Elapsed > LockWait)
                    return null;

                await Task.Delay(200).ConfigureAwait(false);
                Refresh();
                if (done())
                    return null;
            }
        }

        private void ReadFileWhenChanged()
        {
            try
            {
                var info = new FileInfo(_file);
                if (!info.Exists)
                    return;

                var stamp = (info.LastWriteTimeUtc, info.Length);
                if (stamp == _stamp)
                    return;

                List<string[]> pairs;
                try
                {
                    // FileShare.Delete: another instance may replace the file while it is read
                    using (var stream = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        pairs = JsonSerializer.Deserialize<List<string[]>>(stream);
                }
                catch (JsonException ex)
                {
                    // a faulty file: it is written new with the next answer
                    Log.Warning("Chat translation: the cache file {File} is faulty: {Message}", _file, ex.Message);
                    _stamp = stamp;
                    return;
                }

                var entries = new Dictionary<string, string>(StringComparer.Ordinal);
                var order = new List<string>();
                foreach (string[] pair in pairs ?? new List<string[]>())
                {
                    if (pair == null || pair.Length != 2 || pair[0] == null || pair[1] == null || entries.ContainsKey(pair[0]))
                        continue;
                    entries.Add(pair[0], pair[1]);
                    order.Add(pair[0]);
                }

                foreach (string text in _unsaved)
                {
                    if (!entries.ContainsKey(text) && _entries.TryGetValue(text, out string translation))
                    {
                        entries.Add(text, translation);
                        order.Add(text);
                    }
                }

                _entries = entries;
                _order = order;
                _stamp = stamp;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // read again the next time
                Log.Debug("Chat translation: the cache file {File} could not be read: {Message}", _file, ex.Message);
            }
        }

        // The whole file is written beside it and then put in its place, so no instance reads half a file.
        private void WriteFile()
        {
            var pairs = new List<string[]>(_order.Count);
            foreach (string text in _order)
                pairs.Add(new[] { text, _entries[text] });

            string temporary = _file + "." + Environment.ProcessId + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file));
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(pairs, FileFormat));

                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        File.Move(temporary, _file, true);
                        break;
                    }
                    catch (Exception ex) when (attempt < 5 && (ex is IOException || ex is UnauthorizedAccessException))
                    {
                        Thread.Sleep(50);   // another instance is reading it
                    }
                }

                var info = new FileInfo(_file);
                _stamp = (info.LastWriteTimeUtc, info.Length);
                _unsaved.Clear();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // the answers stay in memory and are written with the next ones
                if (!_write_failure_reported)
                {
                    _write_failure_reported = true;
                    Log.Warning("Chat translation: the cache file {File} could not be written: {Message}", _file, ex.Message);
                }

                try
                {
                    File.Delete(temporary);
                }
                catch (Exception)
                {
                    // nothing to do about it
                }
            }
        }
    }
}
