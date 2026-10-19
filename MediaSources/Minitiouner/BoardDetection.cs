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

        public bool Complete => I2c != None && Ts != None;
        public int TsCount => Ts2 != None ? 2 : 1;
        public string Name => Type + " (" + string.Join(" + ", ChipSerials) + ")";

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

        public static List<MiniTiounerBoard> Detect(List<FtdiDeviceInfo> devices, List<string> warnings)
        {
            var boards = new List<MiniTiounerBoard>();

            // The two channels (A/B) of one FT2232H share the serial number except for the last letter.
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

            // Boards with a single FT2232H: I2C on channel A, TS on channel B
            foreach (var chip in chips.Where(c => c.Board != "Minitiouner Pro 2" && c.Roles.ContainsKey("I2C") && c.Roles.ContainsKey("TS")))
            {
                var board = new MiniTiounerBoard { Type = chip.Board, I2c = chip.Roles["I2C"], Ts = chip.Roles["TS"] };
                board.ChipSerials.Add(chip.Serial);
                boards.Add(board);
            }

            // BATC V2 with a second, separate TS chip ("NIM DB tuner B")
            var dbChips = chips.Where(c => c.Board == "BATC V2 Minitiouner" && c.Roles.ContainsKey("TS2") && !c.Roles.ContainsKey("I2C")).ToList();
            var v2Boards = boards.Where(b => b.Type == "BATC V2 Minitiouner").ToList();
            if (dbChips.Count > 0)
            {
                if (v2Boards.Count == 1 && dbChips.Count == 1)
                {
                    v2Boards[0].Ts2 = dbChips[0].Roles["TS2"];
                    v2Boards[0].ChipSerials.Add(dbChips[0].Serial);
                }
                else
                {
                    warnings.Add("Second TS chip of a BATC V2 found, but it cannot be assigned to a board (" + v2Boards.Count + " V2 board(s), " + dbChips.Count + " second TS chip(s)): ignored");
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

                if (masters.Count == 1 && auxes.Count == 1)
                {
                    board.Aux = auxes[0].Roles["AUX"];
                    board.Ts2 = auxes[0].Roles["TS2"];
                    board.ChipSerials.Add(auxes[0].Serial);
                }
                boards.Add(board);
            }

            if (masters.Count > 1 && auxes.Count > 0)
                warnings.Add(masters.Count + " MiniTiouner Pro master chips and " + auxes.Count + " aux chips found: cannot tell which aux chip belongs to which master, the Pros are used with one TS each");
            else if (masters.Count == 0 && auxes.Count > 0)
                warnings.Add("MiniTiouner Pro aux chip found without its master chip: ignored");

            foreach (var chip in chips)
            {
                bool used = boards.Any(b => b.ChipSerials.Contains(chip.Serial));
                if (!used && chip.Board != "Minitiouner Pro 2")
                    warnings.Add(chip.Board + " chip " + chip.Serial + " is incomplete (roles: " + string.Join(", ", chip.Roles.Keys) + "): ignored");
            }

            return boards;
        }

        // The board to use. preferredSerial (a chip serial of the wanted board, setting "PreferredBoardSerial")
        // wins; otherwise the board with the most TS streams (Pro before V2), then the lowest serial number.
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

        // The other complete boards (not "used"), the order in which the connect numbers them after the first board:
        // most TS streams first, a Pro before a V2, then by serial number.
        public static List<MiniTiounerBoard> Further(List<MiniTiounerBoard> boards, MiniTiounerBoard used)
        {
            return boards
                .Where(b => b.Complete && !b.ChipSerials.Any(s => used.ChipSerials.Contains(s)))
                .OrderByDescending(b => b.TsCount)
                .ThenBy(b => b.Type == "Minitiouner Pro 2" ? 0 : 1)
                .ThenBy(b => b.ChipSerials.FirstOrDefault() ?? "", StringComparer.Ordinal)
                .ToList();
        }

        // Writes into FtdiDeviceInfo.Tuner which tuner reads each TS channel, numbered like the connect does it
        // (MinitiounerMultiSource): the board chosen first has tuners 1..n, the next board continues, at most maxTuners.
        // In a board the channel with role "TS" is tuner 1 (MinitiounerSource: tuner 1 = TSThread with ReadTS2 =
        // ftdiDevice_ts), the channel with role "TS2" tuner 2. The board's own names (TS1/TS2) are crossed with the roles.
        // useAllBoards false = only the first board is used.
        public static void AssignTuners(List<FtdiDeviceInfo> devices, List<MiniTiounerBoard> boards, string preferredSerial, bool useAllBoards, int maxTuners)
        {
            foreach (var d in devices)
                d.Tuner = "-";

            var first = Select(boards, preferredSerial);
            if (first == null)
                return;

            var ordered = new List<MiniTiounerBoard> { first };
            if (useAllBoards)
                ordered.AddRange(Further(boards, first));

            var used = new List<MiniTiounerBoard>();
            int offset = 0;
            foreach (var board in ordered)
            {
                if (used.Count > 0 && offset + board.TsCount > maxTuners)
                    continue;
                used.Add(board);
                offset += board.TsCount;
            }

            offset = 0;
            foreach (var board in used)
            {
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
