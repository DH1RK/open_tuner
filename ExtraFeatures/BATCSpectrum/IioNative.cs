using Serilog;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace opentuner
{
    // Loads the native libiio 1.0 (libiio1.dll and what it needs) for the ADI Pluto spectrum source (#215).
    //
    // The build copies the files into libiio\ next to opentuner.exe (opentuner.csproj, EnsureLibiioBundled). They are loaded by their
    // full path before the first call of the binding: a module that is loaded satisfies the imports of the next one by its name, so the
    // dependencies (libusb, libxml2, ...) are found without changing the search path of the whole program. The same file name
    // libusb-1.0.dll that LibUsbDotNet (the PicoTuner) uses is shared on purpose: it is the same library.
    static class IioNative
    {
        // dependencies first, libiio1.dll last
        static readonly string[] Files =
        {
            "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll",
            "libzstd.dll", "libxml2.dll", "libserialport.dll", "libusb-1.0.dll",
            "libiio1.dll"
        };

        static readonly object load_lock = new object();
        static bool loaded;
        static string error;

        public static string Folder => Path.Combine(AppContext.BaseDirectory, "libiio");

        // true when libiio1.dll is loaded; otherwise error says why
        public static bool Load(out string why)
        {
            lock (load_lock)
            {
                if (loaded)
                {
                    why = null;
                    return true;
                }
                if (error != null)
                {
                    why = error;
                    return false;
                }

                try
                {
                    string folder = Folder;
                    if (Directory.Exists(folder))
                    {
                        foreach (string name in Files)
                        {
                            string path = Path.Combine(folder, name);
                            if (!File.Exists(path))
                            {
                                if (name == "libiio1.dll")
                                    throw new FileNotFoundException("libiio1.dll is not in " + folder);
                                continue;       // the runtime files may be missing when the system has them
                            }
                            NativeLibrary.Load(path);
                        }
                    }
                    else if (!NativeLibrary.TryLoad("libiio1.dll", out _))
                    {
                        throw new FileNotFoundException("libiio1.dll is not in " + folder + " and not on the search path");
                    }
                    loaded = true;
                    Log.Information("ADI spectrum: libiio loaded from " + (Directory.Exists(folder) ? folder : "the search path"));
                    why = null;
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Log.Warning("ADI spectrum: libiio could not be loaded: " + ex.Message);
                    why = error;
                    return false;
                }
            }
        }
    }
}
