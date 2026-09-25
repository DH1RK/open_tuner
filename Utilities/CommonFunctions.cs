using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace opentuner.Utilities
{
    public static class CommonFunctions
    {
        // .NET (Core) needs UseShellExecute = true to open a URL in the default browser
        public static void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Unable to open URL {Url}", url);
            }
        }

        public static string GenerateTimestampFilename()
        {
            return DateTime.Now.ToString("yyyy-dd-M--HH-mm-ss");
        }

        public static List<string> determineIP()
        {
            List<string> detected_ips = new List<string>();

            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    detected_ips.Add(ip.ToString());
                }
            }

            return detected_ips;
        }
        

    }
}
