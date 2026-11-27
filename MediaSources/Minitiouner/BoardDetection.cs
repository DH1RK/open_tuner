using System;
using System.Collections.Generic;
using System.Linq;

namespace opentuner.MediaSources.Minitiouner
{
    // One MiniTiouner board as it is found on USB: the FTDI channels that belong together.
    // The port numbers are FTDI device indexes (as used by hw_init), 99 = not present.
    public class MiniTiounerBoard
    {
        public const uint None = 99;

        public string Type;                                         // e.g. "Minitiouner Pro 2"
        public List<string> ChipSerials = new List<string>();       // serial numbers without the channel letter
        public uint I2c = None;
        public uint Ts = None;
        public uint Ts2 = None;
        public uint Aux = None;
        public string PicoPort = "";                                // USB port of a PicoTuner, "" = an FTDI board

        public bool IsPicoTuner => PicoPort.Length > 0;

        // A PicoTuner as one more board of a source (#272). It has no chip serial number, the "serial" is made of the
        // USB port ("USB 2-5.2" -> "pico_2-5_2") and names the settings file. The FTDI device indexes have no meaning.
        public static MiniTiounerBoard ForPicoTuner(string port)
        {
            var board = new MiniTiounerBoard { Type = "PicoTuner", PicoPort = port, I2c = 0, Ts = 0, Ts2 = 0 };
            board.ChipSerials.Add("pico_" + port.Replace("USB ", "").Replace('.', '_'));
            return board;
        }

        public bool Complete => I2c != None && Ts != None;
        public int TsCount => Ts2 != None ? 2 : 1;
        public string Name => Type + " (" + (IsPicoTuner ? PicoPort : string.Join(" + ", ChipSerials)) + ")";

        public bool MatchesSerial(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return false;
            string s = serial.Trim();
            return ChipSerials.Any(c => s.StartsWith(c, StringComparison.OrdinalIgnoreCase));
        }
    }

    // Groups the FTDI channels of the driver's device list into boards. Before, hw_detect took the last
    // matching channel for each role, so with two boards connected one silently replaced the other.
    public static class BoardDetection
    {
        private class Chip
        {
            public string Board;
            public string Serial;                                    // without the channel letter
            public Dictionary<string, uint> Roles = new Dictionary<string, uint>();
        }

        // The two channels (A/B) of one FT2232H share the serial number except for the last letter.
        private static List<Chip> GroupChips(List<FtdiDeviceInfo> devices)
        {
            var chips = new List<Chip>();
            foreach (var d in devices.Where(d => d.Role != "-" && d.Board != ""))
            {
                string serial = ChipSerial(d.Serial);
                var chip = chips.FirstOrDefault(c => c.Serial == serial && c.Board == d.Board);
                if (chip == null)
                {
                    chip = new Chip { Board = d.Board, Serial = serial };
                    chips.Add(chip);
                }
                chip.Roles[d.Role] = (uint)d.Index;
            }
            return chips;
        }

        // The boards for the table in Hardware Info: chip serial + board type of every board with one chip (V2, S, Express, ...)
        // and of every MiniTiouner Pro master chip, and the aux chip serials of the Pros (to pair them).
        public static void TableChips(List<FtdiDeviceInfo> devices, out List<KeyValuePair<string, string>> boards, out List<string> auxes)
        {
            var chips = GroupChips(devices);
            boards = chips.Where(c => c.Roles.ContainsKey("I2C") && c.Roles.ContainsKey("TS"))
                .OrderBy(c => c.Board == "Minitiouner Pro 2" ? 0 : 1).ThenBy(c => c.Serial, StringComparer.Ordinal)
                .Select(c => new KeyValuePair<string, string>(c.Serial, c.Board)).ToList();
            auxes = chips.Where(c => c.Board == "Minitiouner Pro 2" && c.Roles.ContainsKey("AUX") && c.Roles.ContainsKey("TS2"))
                .Select(c => c.Serial).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        // allInstances false (default): the boards the table (BoardTable, Hardware Info) gives to another OpenTuner
        // instance (--instance) are left out. true: all boards, for the table itself.
        public static List<MiniTiounerBoard> Detect(List<FtdiDeviceInfo> devices, List<string> warnings, bool allInstances = false)
        {
            var boards = new List<MiniTiounerBoard>();
            var chips = GroupChips(devices);

            // Boards with a single FT2232H: I2C on channel A, TS on channel B
            foreach (var chip in chips.Where(c => c.Board != "Minitiouner Pro 2" && c.Roles.ContainsKey("I2C") && c.Roles.ContainsKey("TS")))
            {
                var board = new MiniTiounerBoard { Type = chip.Board, I2c = chip.Roles["I2C"], Ts = chip.Roles["TS"] };
                board.ChipSerials.Add(chip.Serial);
                boards.Add(board);
            }

            // BATC V2 or Minitiouner-S with a second, separate TS chip ("NIM DB tuner B"). The daughter board is programmed
            // with the same template for both, so its description does not tell which board it belongs to.
            var dbChips = chips.Where(c => c.Board == "BATC V2 Minitiouner" && c.Roles.ContainsKey("TS2") && !c.Roles.ContainsKey("I2C")).ToList();
            var dbHosts = boards.Where(b => b.Type == "BATC V2 Minitiouner" || b.Type == "Minitiouner-S").ToList();
            if (dbChips.Count > 0)
            {
                if (dbHosts.Count == 1 && dbChips.Count == 1)
                {
                    dbHosts[0].Ts2 = dbChips[0].Roles["TS2"];
                    dbHosts[0].ChipSerials.Add(dbChips[0].Serial);
                }
                else
                {
                    warnings.Add("Second TS chip (NIM DB) found, but it cannot be assigned to a board (" + dbHosts.Count + " BATC V2 / Minitiouner-S board(s), " + dbChips.Count + " second TS chip(s)): ignored");
                }
            }

            // MiniTiouner Pro: master chip ("TS2 A/B": I2C + TS) and aux chip ("TS1 A/B": AUX + TS2), two
            // FT2232H with unrelated serial numbers. Only pairable when there is exactly one of each.
            var masters = chips.Where(c => c.Board == "Minitiouner Pro 2" && c.Roles.ContainsKey("I2C") && c.Roles.ContainsKey("TS")).ToList();
            var auxes = chips.Where(c => c.Board == "Minitiouner Pro 2" && c.Roles.ContainsKey("AUX") && c.Roles.ContainsKey("TS2")).ToList();

            foreach (var master in masters)
            {
                var board = new MiniTiounerBoard { Type = "Minitiouner Pro 2", I2c = master.Roles["I2C"], Ts = master.Roles["TS"] };
                board.ChipSerials.Add(master.Serial);

                // the aux chip: from the table in Hardware Info, otherwise the only one if there is only one Pro
                var row = BoardTable.Find(master.Serial);
                var aux = (row != null && row.Aux.Length > 0)
                    ? auxes.FirstOrDefault(c => c.Serial.StartsWith(row.Aux, StringComparison.OrdinalIgnoreCase))
                    : (masters.Count == 1 && auxes.Count == 1) ? auxes[0] : null;
                if (aux != null)
                {
                    board.Aux = aux.Roles["AUX"];
                    board.Ts2 = aux.Roles["TS2"];
                    board.ChipSerials.Add(aux.Serial);
                }
                boards.Add(board);
            }

            int unpaired = boards.Count(b => b.Type == "Minitiouner Pro 2" && b.Ts2 == MiniTiounerBoard.None);
            if (masters.Count > 1 && auxes.Count > 0 && unpaired > 0)
                warnings.Add(masters.Count + " MiniTiouner Pro master chips and " + auxes.Count + " aux chips found: cannot tell which aux chip belongs to which master, " + unpaired + " Pro(s) used with one TS (pair them in the board table of Hardware Info)");
            else if (masters.Count == 0 && auxes.Count > 0)
                warnings.Add("MiniTiouner Pro aux chip found without its master chip: ignored");

            foreach (var chip in chips)
            {
                bool used = boards.Any(b => b.ChipSerials.Contains(chip.Serial));
                if (!used && chip.Board != "Minitiouner Pro 2")
                    warnings.Add(chip.Board + " chip " + chip.Serial + " is incomplete (roles: " + string.Join(", ", chip.Roles.Keys) + "): ignored");
            }

            if (!allInstances)
                boards.RemoveAll(b => !BoardTable.IsForThisInstance(b.ChipSerials[0]));

            return boards;
        }

        // The board to open. preferredSerial (a chip serial of the board chosen by BoardSelection) wins; otherwise the board
        // with the most TS streams (Pro before V2), then the lowest serial number.
        public static MiniTiounerBoard Select(List<MiniTiounerBoard> boards, string preferredSerial)
        {
            var complete = boards.Where(b => b.Complete).ToList();
            if (complete.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(preferredSerial))
            {
                var preferred = complete.FirstOrDefault(b => b.MatchesSerial(preferredSerial));
                if (preferred != null)
                    return preferred;
            }

            return complete
                .OrderByDescending(b => b.TsCount)
                .ThenBy(b => b.Type == "Minitiouner Pro 2" ? 0 : 1)
                .ThenBy(b => b.ChipSerials.FirstOrDefault() ?? "", StringComparer.Ordinal)
                .First();
        }

        // Writes into FtdiDeviceInfo.Tuner which tuner reads each TS channel. used: the boards of a source in the order of
        // their tuner numbers (BoardSelection, or the connected ones). A PicoTuner takes tuner numbers but has no FTDI
        // devices. In a board the channel with role "TS" is tuner 1 (MinitiounerSource: tuner 1 = TSThread with ReadTS2 =
        // ftdiDevice_ts), the channel with role "TS2" tuner 2. The board's own names (TS1/TS2) are crossed with the roles.
        public static void LabelTuners(List<FtdiDeviceInfo> devices, List<MiniTiounerBoard> used)
        {
            foreach (var d in devices)
                d.Tuner = "-";

            int offset = 0;
            foreach (var board in used)
            {
                if (board.IsPicoTuner)
                {
                    offset += board.TsCount;
                    continue;
                }

                string label = ShortName(board.Type);
                var same = used.Where(o => ShortName(o.Type) == label).ToList();
                if (same.Count > 1)
                    label += same.IndexOf(board) + 1;

                Action<uint, int, string> set = (port, tuner, letter) =>
                {
                    var device = devices.FirstOrDefault(x => x.Index == port);
                    if (port != MiniTiounerBoard.None && device != null)
                        device.Tuner = tuner + " (" + label + " " + letter + ")";
                };
                set(board.Ts, offset + 1, "1");
                set(board.Ts2, offset + 2, "2");

                // the I2C channel controls the NIM, so both tuners of the board
                var control = devices.FirstOrDefault(x => x.Index == board.I2c);
                if (board.I2c != MiniTiounerBoard.None && control != null)
                    control.Tuner = "ctrl " + (board.TsCount > 1 ? (offset + 1) + "-" + (offset + 2) : (offset + 1).ToString()) + " (" + label + ")";

                offset += board.TsCount;
            }
        }

        public static string ShortName(string boardType)
        {
            switch (boardType)
            {
                case "Minitiouner Pro 2": return "Pro";
                case "BATC V2 Minitiouner": return "V2";
                case "Minitiouner Express": return "Express";
                case "Minitiouner-S": return "S";
                case "PicoTuner": return "Pico";
                default: return string.IsNullOrEmpty(boardType) ? "M" : boardType;
            }
        }

        // "DA5YTEB3B" -> "DA5YTEB3"
        private static string ChipSerial(string serial)
        {
            if (string.IsNullOrEmpty(serial))
                return "";
            char last = serial[serial.Length - 1];
            return (last == 'A' || last == 'B') ? serial.Substring(0, serial.Length - 1) : serial;
        }
    }
}
