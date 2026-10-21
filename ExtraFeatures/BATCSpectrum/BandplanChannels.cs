using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Serilog;

namespace opentuner.ExtraFeatures.BATCSpectrum
{
    // The channel names of the bandplan ("A1", "B3", ...): the same names the spectrum shows as "CH:", also for the
    // info line of a stream, where the spectrum may not even be running.
    public static class BandplanChannels
    {
        // a tuned frequency belongs to a channel when it is within this distance of the channel centre (kHz)
        private const double MaxDistanceKHz = 125;

        private static readonly object _lock = new object();
        private static bool _loaded = false;
        private static List<XElement> _channels = new List<XElement>();
        private static string[] _labels = new string[0];

        // "A1", "B3", ...: the row of a bandplan channel and its number in that row, counted by ascending frequency
        // (bandplan.xml has no channel numbers); same index as the list
        public static string[] BuildLabels(IList<XElement> channels)
        {
            string[] labels = new string[channels.Count];

            foreach (var row in channels.Select((channel, index) => new { channel, index }).GroupBy(c => c.channel.Element("block").Value))
            {
                int number = 1;
                foreach (var item in row.OrderBy(c => Convert.ToDouble(c.channel.Element("x-freq").Value, CultureInfo.InvariantCulture)))
                {
                    labels[item.index] = row.Key + number.ToString();
                    number++;
                }
            }

            return labels;
        }

        // The channel for a tuned frequency (kHz, downlink) and symbol rate (kS), "" if there is none. Several rows share
        // centre frequencies (A 10493.25 with 1000 kS, B 10493.25 with 333 kS): the symbol rate decides then.
        public static string Find(long frequency_kHz, int symbol_rate_kS)
        {
            Load();

            lock (_lock)
            {
                string best = "";
                double best_distance = double.MaxValue;
                double best_sr_distance = double.MaxValue;

                for (int i = 0; i < _channels.Count; i++)
                {
                    double centre_kHz = Convert.ToDouble(_channels[i].Element("x-freq").Value, CultureInfo.InvariantCulture) * 1000.0;
                    double distance = Math.Abs(centre_kHz - frequency_kHz);
                    if (distance > MaxDistanceKHz)
                        continue;

                    double sr_distance = Math.Abs(Convert.ToDouble(_channels[i].Element("sr").Value, CultureInfo.InvariantCulture) - symbol_rate_kS);

                    // the nearer centre wins; at the same centre the nearer symbol rate
                    if (distance < best_distance - 0.5 || (Math.Abs(distance - best_distance) <= 0.5 && sr_distance < best_sr_distance))
                    {
                        best = _labels[i];
                        best_distance = distance;
                        best_sr_distance = sr_distance;
                    }
                }

                return best;
            }
        }

        private static void Load()
        {
            lock (_lock)
            {
                if (_loaded)
                    return;

                _loaded = true;

                try
                {
                    var bandplan = XElement.Load(Path.GetDirectoryName(Application.ExecutablePath) + @"\extra\bandplan.xml");
                    _channels = bandplan.Elements("channel").ToList();
                    _labels = BuildLabels(_channels);
                }
                catch (Exception ex)
                {
                    Log.Warning("Bandplan channels: bandplan.xml not loaded: " + ex.Message);
                }
            }
        }
    }
}
