using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Serilog;

namespace opentuner.ExtraFeatures.BATCWebchat
{
    // The characters Azure Translator has charged this month (issue #270). Azure tells them with every answer; the counter
    // of the Azure account itself needs a sign-in to Azure, which the translation key is not. Kept in a file in the user
    // directory ("<month> <characters>"), so every OpenTuner of the user (all versions, folders and instances) counts into
    // it; what other PCs or programs send with the key is not in it. The month is the one of Azure (from the 1st, 00:00 UTC).
    public static class TranslationUsage
    {
        public const long FreeLimit = 2000000;   // characters per month of the free tier F0

        // raised from any thread when this program added characters
        public static event Action Changed;

        private static string _file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".azure_translator_usage");

        private static readonly object Sync = new object();
        private static (string month, long characters) _last;   // as read or written last
        private static bool _problem_reported;

        private static string Month()
        {
            return DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        public static void Add(long characters)
        {
            if (characters <= 0)
                return;

            lock (Sync)
            {
                string month = Month();
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        // nobody else reads or writes between the reading and the writing
                        using (var stream = new FileStream(_file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                        {
                            long total = Read(stream, month) + characters;
                            byte[] line = Encoding.ASCII.GetBytes(month + " " + total.ToString(CultureInfo.InvariantCulture) + "\r\n");
                            stream.SetLength(0);
                            stream.Position = 0;
                            stream.Write(line, 0, line.Length);
                            _last = (month, total);
                        }
                        break;
                    }
                    catch (IOException) when (attempt < 40)
                    {
                        Thread.Sleep(25);   // another instance has it
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        if (!_problem_reported)
                        {
                            _problem_reported = true;
                            Log.Warning("Chat translation: the usage file {File} could not be written: {Message}", _file, ex.Message);
                        }
                        return;
                    }
                }
            }

            Changed?.Invoke();
        }

        // the characters of the current month, 0 when there is no file yet
        public static long ThisMonth()
        {
            lock (Sync)
            {
                string month = Month();
                try
                {
                    if (!File.Exists(_file))
                        return 0;

                    using (var stream = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        _last = (month, Read(stream, month));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // being written just now: the number known last
                }

                return _last.month == month ? _last.characters : 0;
            }
        }

        // the number in the file when it is the one of this month, else 0
        private static long Read(Stream stream, string month)
        {
            var buffer = new byte[(int)Math.Min(stream.Length, 100)];
            stream.ReadExactly(buffer, 0, buffer.Length);
            string[] parts = Encoding.ASCII.GetString(buffer).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && parts[0] == month && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long characters)
                ? characters
                : 0;
        }
    }
}
