using System;
using System.Collections.Generic;
using System.Linq;
using opentuner.Utilities;

namespace opentuner.MediaSources.Minitiouner
{
    // One board in the table: a MiniTiouner board or a PicoTuner, which OpenTuner instance uses it and its place in the
    // tuner order. For a MiniTiouner Pro also the aux chip that belongs to it (cannot be seen from USB when several Pros
    // are connected, the user pairs them).
    public class BoardTableRow
    {
        public string Master = "";      // chip serial without the A/B letter, "pico_<USB port>" for a PicoTuner
        public string Aux = "";         // "" = not paired: the Pro runs with one TS
        public int Instance = BoardTable.AutoInstance;  // OpenTuner instance (--instance) that uses the board, or AutoInstance / Off
        public int Order = 0;           // place in the table, 1 = first; 0 = not set
    }

    public class BoardTableData
    {
        public int Version = 1;
        public List<BoardTableRow> Rows = new List<BoardTableRow>();
    }

    // The table is one file (settings\minitiouner_boards.json) that all OpenTuner instances in a folder read; it is
    // only written from Hardware Info. A board that is not in the table can be used by every instance.
    public static class BoardTable
    {
        public const int AutoInstance = 0;  // BoardTableRow.Instance: "auto", the first instance that connects takes the board
        public const int Off = -1;          // no instance uses the board

        // set with the command line option --instance
        public static int Instance = 1;

        // true if --instance was given: the number is shown in the title bar
        public static bool InstanceGiven = false;

        // The file of the main settings (window, source, view): instance 1 keeps open_tuner_settings.json, every other
        // instance has its own open_tuner_settings_<n>.json, which starts as a copy of the one of instance 1.
        public static string MainSettingsName()
        {
            string own_path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings", "open_tuner_settings_" + Instance + ".json");
            bool existed = Instance <= 1 || System.IO.File.Exists(own_path);
            string name = SettingsName("open_tuner_settings");
            // called more than once (Program and MainForm): the first call makes the copy, a later one must not forget it
            if (!existed && System.IO.File.Exists(own_path))
                MainSettingsCopied = true;
            return name;
        }

        // true if the main settings file of this instance was made just now, as a copy of the one of instance 1 (the first start of the
        // instance): the window of instance 1 is not meant for it (#216)
        public static bool MainSettingsCopied = false;

        // Same for every settings file that belongs to the instance (chat, spectrum, MQTT, QuickTune, the sources).
        public static string SettingsName(string name)
        {
            if (Instance <= 1)
                return name;

            string own = name + "_" + Instance;
            try
            {
                string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings");
                string own_path = System.IO.Path.Combine(dir, own + ".json");
                string first_path = System.IO.Path.Combine(dir, name + ".json");
                if (!System.IO.File.Exists(own_path) && System.IO.File.Exists(first_path))
                    System.IO.File.Copy(first_path, own_path);
            }
            catch (Exception)
            {
                // the instance then starts with the defaults
            }
            return own;
        }

        // the settings files that exist once per instance (see SettingsName)
        private static readonly string[] InstanceSettingNames = { "open_tuner_settings", "minitiouner_settings", "longmynd_settings", "winterhill_settings",
            "qo100_webchat_settings", "spectrumSettings", "mqttclient_settings", "quicktune_settings" };

        // the settings and log files of an instance greater than 1
        public static List<string> FilesOfInstance(int instance)
        {
            var files = new List<string>();
            string settings = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings");
            string logs = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            foreach (var name in InstanceSettingNames)
            {
                string path = System.IO.Path.Combine(settings, name + "_" + instance + ".json");
                if (System.IO.File.Exists(path))
                    files.Add(path);
            }
            if (System.IO.Directory.Exists(logs))
                files.AddRange(System.IO.Directory.GetFiles(logs, "ot_log_*_i" + instance + ".txt"));
            return files;
        }

        // the instances (2..) that have files
        public static List<int> InstancesWithFiles()
        {
            var result = new List<int>();
            for (int i = 2; i <= 9; i++)
                if (FilesOfInstance(i).Count > 0)
                    result.Add(i);
            return result;
        }

        // Deletes the files of an instance. Its boards in the table go to instance 1 (the pairing stays) so they are not
        // blocked for everybody. Returns the number of files that could not be deleted.
        public static int DeleteInstance(int instance)
        {
            int failed = 0;
            foreach (var file in FilesOfInstance(instance))
            {
                try { System.IO.File.Delete(file); }
                catch (Exception) { failed++; }
            }

            var rows = Data.Rows;
            if (rows.Any(r => r.Instance == instance))
            {
                foreach (var row in rows.Where(r => r.Instance == instance))
                    row.Instance = 1;
                Save(rows);
            }
            return failed;
        }

        private static BoardTableData _data;

        private static SettingsManager<BoardTableData> Manager => new SettingsManager<BoardTableData>("minitiouner_boards");

        private static BoardTableData Data
        {
            get
            {
                if (_data == null)
                    _data = Manager.LoadSettings(new BoardTableData());
                return _data;
            }
        }

        public static BoardTableRow Find(string masterSerial)
        {
            if (string.IsNullOrWhiteSpace(masterSerial))
                return null;

            // a PicoTuner by its whole USB port ("pico_2-1" is not "pico_2-10"), a chip also with its channel letter
            return Data.Rows.FirstOrDefault(r => r.Master.Length > 0 && (r.Master.StartsWith("pico_", StringComparison.OrdinalIgnoreCase)
                ? string.Equals(masterSerial, r.Master, StringComparison.OrdinalIgnoreCase)
                : masterSerial.StartsWith(r.Master, StringComparison.OrdinalIgnoreCase)));
        }

        public static bool IsForThisInstance(string masterSerial)
        {
            var row = Find(masterSerial);
            return row == null || row.Instance == AutoInstance || row.Instance == Instance;
        }

        // reads the file again at the next access
        public static void Reload()
        {
            _data = null;
        }

        // replaces the whole table: it is set up once with all boards connected, nothing invisible stays in it
        public static void Save(List<BoardTableRow> rows)
        {
            _data = new BoardTableData { Rows = rows };
            Manager.SaveSettings(_data);
        }
    }
}
