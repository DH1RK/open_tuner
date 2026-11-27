using System;
using System.Collections.Generic;
using System.Linq;

namespace opentuner.MediaSources.Minitiouner
{
    // Which boards a source uses and in which order (#272), chosen with the board table in Hardware Info (BoardTable).
    // MiniTiouner boards (FTDI) and PicoTuners are treated alike.
    public static class BoardSelection
    {
        // the order of the boards = the order of their tuner numbers: as sorted in the board table; boards without an
        // entry behind them, MiniTiouner boards first (most TS streams first, a Pro before a V2, by serial number), then
        // the PicoTuners by USB port
        public static List<MiniTiounerBoard> Ordered(IEnumerable<MiniTiounerBoard> boards)
        {
            return boards
                .OrderBy(b => TablePosition(b))
                .ThenBy(b => b.IsPicoTuner ? 1 : 0)
                .ThenByDescending(b => b.TsCount)
                .ThenBy(b => b.Type == "Minitiouner Pro 2" ? 0 : 1)
                .ThenBy(b => b.ChipSerials.FirstOrDefault() ?? "", StringComparer.Ordinal)
                .ToList();
        }

        private static int TablePosition(MiniTiounerBoard board)
        {
            var row = BoardTable.Find(board.ChipSerials.FirstOrDefault());
            return row != null && row.Order > 0 ? row.Order : int.MaxValue;
        }

        // The boards of this instance out of the given ones, in order, with at most MaxTuners tuners (a board which does
        // not fit any more is left out, a smaller one behind it can still fit).
        public static List<MiniTiounerBoard> Choose(IEnumerable<MiniTiounerBoard> boards)
        {
            var chosen = new List<MiniTiounerBoard>();
            int tuners = 0;

            foreach (var board in Ordered(boards.Where(b => b.Complete && BoardTable.IsForThisInstance(b.ChipSerials[0]))))
            {
                if (tuners + board.TsCount > MinitiounerMultiSource.MaxTuners)
                    continue;
                chosen.Add(board);
                tuners += board.TsCount;
            }

            return chosen;
        }

        // The boards a connect would use now: those on USB which no other program holds, as chosen above.
        public static List<MiniTiounerBoard> Find()
        {
            BoardTable.Reload();   // another instance may have saved the table

            var devices = HardwareInfo.EnumerateFtdiDevices(out _);
            var boards = BoardDetection.Detect(devices, new List<string>());
            boards.AddRange(PicoTunerInterface.ListDevices(null, true, out _)
                .Where(d => d.State == "free")
                .Select(d => MiniTiounerBoard.ForPicoTuner(d.Port)));

            return Choose(boards);
        }

        // FTDI devices are there, but none is recognised as a board by its description (an FTDI module with other names):
        // the connect then tries the first two devices, as it always did.
        public static bool OnlyUnrecognisedFtdi()
        {
            var devices = HardwareInfo.EnumerateFtdiDevices(out string error);
            return error == null && devices.Count >= 2 && devices.All(d => d.Role == "-" && !d.InUseByOtherProgram);
        }
    }
}
