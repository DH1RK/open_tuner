using FlyleafLib;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using opentuner.Utilities;

namespace opentuner
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        public static LoggingLevelSwitch levelSwitch;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow([In] IntPtr hWnd, [In] int nCmdShow);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool SetDllDirectory(string lpPathName);

        [STAThread]

        static void Main(string[] args)
        {
            int i = 0;
            int debugLevel = 3; // Warning
            levelSwitch = new LoggingLevelSwitch();

            while (i < args.Length)
            {
                switch (args[i])
                {
                    case "--debuglevel":
                        int new_debug_level = -1;

                        if (int.TryParse(args[i + 1], out new_debug_level))
                        {
                            if (new_debug_level < 6 && new_debug_level >= 0)
                            {
                                debugLevel = new_debug_level;
                            }
                            i += 1;
                        }
                        break;

                    case "--hideconsolewindow":
                        // minimize console window
                        IntPtr handle = GetConsoleWindow();
                        if (handle != IntPtr.Zero)
                        {
                            ShowWindow(handle, 0);
                        }
                        break;

                    default:
                        break;
                }
                // grab next param
                i += 1;
            }

            switch (debugLevel)
            {
                case 0: // Verbose
                    levelSwitch.MinimumLevel = LogEventLevel.Verbose;
                    break;

                case 1: // Debug
                    levelSwitch.MinimumLevel = LogEventLevel.Debug;
                    break;

                case 2: // Information
                    levelSwitch.MinimumLevel = LogEventLevel.Information;
                    break;

                case 3: // Warning
                    levelSwitch.MinimumLevel = LogEventLevel.Warning;
                    break;

                case 4: // Error
                    levelSwitch.MinimumLevel = LogEventLevel.Error;
                    break;

                case 5: // Fatal
                    levelSwitch.MinimumLevel = LogEventLevel.Fatal;
                    break;

                default:
                    levelSwitch.MinimumLevel = LogEventLevel.Warning;
                    break;
            }

            // Loaded once, this early, so show_console_window can take effect before Serilog's
            // Console sink is built below (AllocConsole() after that point wouldn't retroactively
            // redirect a sink that already captured the old, console-less stdout handle) - reused
            // further down for ffmpeg_path instead of loading settings a second time.
            MainSettings early_settings = new SettingsManager<MainSettings>("open_tuner_settings").LoadSettings(new MainSettings());

            if (early_settings.show_console_window)
            {
                AllocConsole();
            }

            // Must run before any P/Invoke call reaches libmpv-2.dll (MPVMediaPlayer is only
            // instantiated on demand, but SetDllDirectory has to be in place before that first
            // call, so it's simplest to just always set it here, this early). The default in
            // MainSettings.cs points at the folder this migration was built/tested against - if
            // that doesn't exist here, fall back to the default DLL search order instead of
            // pointing SetDllDirectory at a dead folder, and nudge towards SETUP.md.
            if (!string.IsNullOrWhiteSpace(early_settings.libmpv_path))
            {
                if (Directory.Exists(early_settings.libmpv_path))
                {
                    SetDllDirectory(early_settings.libmpv_path);
                }
                else
                {
                    MessageBox.Show(
                        "The configured libmpv Path (\"" + early_settings.libmpv_path + "\") does not exist.\n\n" +
                        "Falling back to the default DLL search order (libmpv-2.dll next to opentuner.exe).\n" +
                        "See SETUP.md for what to install and where, then set the correct path under Settings > Playback Paths.",
                        "OpenTuner - libmpv Path not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .WriteTo.Console()
                .WriteTo.File("logs\\ot_log_" + DateTime.Now.ToString("yyyy-dd-M--HH-mm-ss") + ".txt")
                .CreateLogger();

            // Always log the starting information
            // swith logging level to Information
            LogEventLevel lastMinimumLevel = levelSwitch.MinimumLevel;
            levelSwitch.MinimumLevel = LogEventLevel.Information;

            Log.Information("Starting OpenTuner");

            // swith logging level back
            levelSwitch.MinimumLevel = lastMinimumLevel;

            string logDirectory = AppDomain.CurrentDomain.BaseDirectory + "logs\\";

            if (Directory.Exists(logDirectory))
            {
                var logFiles = Directory.GetFiles(logDirectory, "*.txt").Select(f => new FileInfo(f)).OrderByDescending(f => f.CreationTime);
                int fileCount = logFiles.Count();
                if (fileCount > 10)
                {
                    i = 0;
                    foreach (var file in logFiles)
                    {
                        if (i > 9)
                        {
                            try
                            {
                                File.Delete(file.FullName);
                                Log.Debug("Log file deleted: " + file.Name);
                            }
                            catch
                            {
                                Log.Warning("Log file for deletion not found: " + file.Name);
                            }
                        }
                        i++;
                    }
                }
            }

            try
            {
                // ffmpeg_path is user-configurable (Settings > Playback Paths > ffmpeg Path)
                // since the shared-library ffmpeg build has to match the FFmpeg.AutoGen NuGet
                // package version. The default in MainSettings.cs points at the folder this
                // migration was built/tested against - if that doesn't exist here, fall back to
                // the bundled "ffmpeg\" folder and nudge towards SETUP.md. (early_settings was
                // already loaded above.)
                string ffmpeg_path;
                if (!string.IsNullOrWhiteSpace(early_settings.ffmpeg_path) && Directory.Exists(early_settings.ffmpeg_path))
                {
                    ffmpeg_path = early_settings.ffmpeg_path;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(early_settings.ffmpeg_path))
                    {
                        MessageBox.Show(
                            "The configured ffmpeg Path (\"" + early_settings.ffmpeg_path + "\") does not exist.\n\n" +
                            "Falling back to the bundled \"ffmpeg\\\" folder next to opentuner.exe.\n" +
                            "See SETUP.md for what to install and where, then set the correct path under Settings > Playback Paths.",
                            "OpenTuner - ffmpeg Path not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    ffmpeg_path = @"ffmpeg\";
                }

                Engine.Start(new EngineConfig()
                {
                    FFmpegPath = ffmpeg_path,
                    // FFmpegDevices removed in FlyleafLib 3.11.5's EngineConfig - avdevice/avfilter
                    // loading is no longer a manual opt-out here (we never used dshow/gdigrab).
                    //LogLevel = LogLevel.Debug,
                                              //LogOutput = ":console",
                                              //LogOutput = @"C:\temp2\ffmpeg.log",

                    /*
                    UIRefresh = true,    // Required for Activity, BufferedDuration, Stats in combination with Config.Player.Stats = true
                    UIRefreshInterval = 250,      // How often (in ms) to notify the UI
                    UICurTimePerSecond = false,     // Whether to notify UI for CurTime only when it's second changed or by UIRefreshInterval
                    */
                });

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args));
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Program.Main: Uncaught Exception");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
