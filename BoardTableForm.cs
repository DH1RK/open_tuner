using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using opentuner.MediaSources.Minitiouner;

namespace opentuner
{
    // The board table, shown in Hardware Info (#272): every MiniTiouner board and PicoTuner on USB, which OpenTuner instance
    // (command line option --instance, 1 if not given) uses it, the order of the tuner numbers, and for a MiniTiouner Pro
    // which aux chip belongs to its master chip. Saved in settings\minitiouner_boards.json.
    // The table is set up once (new hardware, new version): it can only be changed while no OpenTuner is connected (see
    // LockReason), it shows the boards found when Hardware Info opens or at "Refresh", and saving replaces the whole file.
    public class BoardTablePanel : Panel
    {
        private const int TunerColumn = 0, BoardColumn = 1, IdColumn = 2, AuxColumn = 3, InstanceColumn = 4;

        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _message = new Label();
        private readonly DataGridViewComboBoxColumn _aux_column;

        private List<string> _auxes = new List<string>();           // aux chips of the Pros on USB
        private ICollection<string> _busy_ports = new List<string>();   // PicoTuners which another program holds
        private bool _filling;
        private readonly DataGridViewComboBoxColumn _instance_column;
        private readonly FlexButton _up, _down, _save;

        // why the table can only be shown at the moment, "" = it can be changed; asked when it is filled and again at Save
        public Func<string> LockReason = () => "";

        public event Action Saved;

        public BoardTablePanel()
        {
            var info = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(6, 4, 6, 0),
                Text = "MiniTiouner boards and PicoTuners (source Minitiouner Variant), one setup for all instances. Instance: which OpenTuner "
                     + "(--instance <n>, default 1) uses it, \"auto\" = the first that connects, \"off\" = none. \"Move up\" / \"Move down\" set the tuner order. "
                     + "MiniTiouner Pro: pair its aux chip. PicoTuner: known by its USB port, in another socket it counts as new."
            };

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;

            // white text on white otherwise
            _grid.EnableHeadersVisualStyles = false;
            _grid.BorderStyle = BorderStyle.None;
            _grid.BackgroundColor = DarkTheme.Frame;
            _grid.GridColor = DarkTheme.Surface;
            _grid.DefaultCellStyle.BackColor = DarkTheme.Input;
            _grid.DefaultCellStyle.ForeColor = Color.White;
            _grid.DefaultCellStyle.SelectionBackColor = DarkTheme.Surface;
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = DarkTheme.Frame;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = DarkTheme.Frame;
            _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;

            // no sorting: the order of the rows is the order of the tuners
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tuner", ReadOnly = true, FillWeight = 25, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Board", ReadOnly = true, FillWeight = 70, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Chip (I2C + TS) / USB port", ReadOnly = true, FillWeight = 55, SortMode = DataGridViewColumnSortMode.NotSortable });
            _aux_column = new DataGridViewComboBoxColumn { HeaderText = "AUX chip + TS2 (Pro only)", FillWeight = 60, FlatStyle = FlatStyle.Flat };
            _grid.Columns.Add(_aux_column);
            _instance_column = new DataGridViewComboBoxColumn { HeaderText = "Instance", FillWeight = 30, FlatStyle = FlatStyle.Flat };
            _instance_column.Items.Add("auto");
            for (int i = 1; i <= 6; i++)
                _instance_column.Items.Add(i.ToString());
            _instance_column.Items.Add("off");
            _grid.Columns.Add(_instance_column);

            // a choice in a drop-down counts at once, for the tuner numbers
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += (s, e) =>
            {
                if (!_filling && e.RowIndex >= 0 && (e.ColumnIndex == AuxColumn || e.ColumnIndex == InstanceColumn))
                    NumberTuners();
            };

            _message.Dock = DockStyle.Bottom;
            _message.Height = 36;   // up to two lines
            _message.Padding = new Padding(6, 3, 6, 0);

            _up = new FlexButton { Text = "Move up", Width = 90, Dock = DockStyle.Right };
            _up.Click += (s, e) => MoveRow(-1);
            _down = new FlexButton { Text = "Move down", Width = 100, Dock = DockStyle.Right };
            _down.Click += (s, e) => MoveRow(1);
            _save = new FlexButton { Text = "Save table", Width = 100, Dock = DockStyle.Right };
            _save.Click += (s, e) => Save();
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 30 };
            bottom.Controls.Add(_up);
            bottom.Controls.Add(_down);
            bottom.Controls.Add(_save);

            // the last one added is laid out first: the message at the very bottom, the buttons above it
            Controls.Add(_grid);
            Controls.Add(info);
            Controls.Add(bottom);
            Controls.Add(_message);
        }

        // boards: all boards on USB in their order, auxes: aux chip serials of the Pros, busy_ports: PicoTuners in use by another
        // program, device_in_use: some device is held by another program (a board among them would be missing here)
        public void Fill(List<MiniTiounerBoard> boards, List<string> auxes, ICollection<string> busy_ports, bool device_in_use)
        {
            string locked = LockReason();
            bool editable = locked.Length == 0;
            _filling = true;
            _auxes = auxes;
            _busy_ports = busy_ports;

            _grid.Rows.Clear();
            _aux_column.ReadOnly = !editable;
            _instance_column.ReadOnly = !editable;
            _grid.DefaultCellStyle.ForeColor = editable ? Color.White : Color.Silver;
            _grid.DefaultCellStyle.SelectionForeColor = _grid.DefaultCellStyle.ForeColor;
            _up.Enabled = _down.Enabled = _save.Enabled = editable;
            _aux_column.Items.Clear();
            _aux_column.Items.Add("");
            foreach (var a in auxes)
                _aux_column.Items.Add(a);

            foreach (var board in boards)
            {
                string key = board.ChipSerials[0];
                var row = BoardTable.Find(key);

                // a paired aux chip which is not on USB at the moment stays, so that saving does not lose it
                string aux = board.Type == "Minitiouner Pro 2" ? row?.Aux ?? "" : "";
                if (aux.Length > 0 && !_aux_column.Items.Contains(aux))
                    _aux_column.Items.Add(aux);

                int instance = row?.Instance ?? BoardTable.AutoInstance;
                string instance_text = instance == BoardTable.Off ? "off" : instance >= 1 && instance <= 6 ? instance.ToString() : "auto";

                int index = _grid.Rows.Add("", board.Type, board.IsPicoTuner ? board.PicoPort : key, aux, instance_text);
                _grid.Rows[index].Tag = board;
                if (board.Type != "Minitiouner Pro 2")
                    _grid.Rows[index].Cells[AuxColumn].ReadOnly = true;
            }

            _filling = false;
            NumberTuners();
            _message.Text = !editable ? "Display only: " + locked
                : "Connect all boards before saving, the table is replaced. It takes effect at the next connect."
                  + (device_in_use ? " A device is in use by another program: if it is one of your boards, close that program and Refresh." : "");
        }

        // The tuner numbers this instance gets with the table as it is shown (the rule of BoardSelection.Choose), "-" for a
        // board it does not use
        private void NumberTuners()
        {
            int pros = _grid.Rows.Cast<DataGridViewRow>().Count(r => ((MiniTiounerBoard)r.Tag).Type == "Minitiouner Pro 2");
            int offset = 0;

            foreach (DataGridViewRow r in _grid.Rows)
            {
                var board = (MiniTiounerBoard)r.Tag;
                int tuners = board.TsCount;
                if (board.Type == "Minitiouner Pro 2")
                {
                    // two with its aux chip: the paired one, or the only one next to the only Pro
                    string aux = Convert.ToString(r.Cells[AuxColumn].Value) ?? "";
                    tuners = (aux.Length > 0 ? _auxes.Contains(aux) : pros == 1 && _auxes.Count == 1) ? 2 : 1;
                }

                string instance = Convert.ToString(r.Cells[InstanceColumn].Value);
                bool used = (instance == "auto" || instance == BoardTable.Instance.ToString())
                    && !_busy_ports.Contains(board.PicoPort)
                    && offset + tuners <= MinitiounerMultiSource.MaxTuners;

                r.Cells[TunerColumn].Value = !used ? "-" : tuners == 1 ? (offset + 1).ToString() : (offset + 1) + "-" + (offset + tuners);
                if (used)
                    offset += tuners;
            }
        }

        private void MoveRow(int direction)
        {
            var cell = _grid.CurrentCell;
            if (cell == null)
                return;

            int from = cell.RowIndex, to = from + direction, column = cell.ColumnIndex;
            if (to < 0 || to >= _grid.Rows.Count)
                return;

            _grid.EndEdit();
            _filling = true;
            var row = _grid.Rows[from];
            _grid.Rows.RemoveAt(from);
            _grid.Rows.Insert(to, row);
            _grid.CurrentCell = row.Cells[column];
            _filling = false;

            NumberTuners();
            _message.Text = "Order changed, \"Save table\" keeps it.";
        }

        private void Save()
        {
            // another OpenTuner may have been started since the table was filled
            string locked = LockReason();
            if (locked.Length > 0)
            {
                _message.Text = "Not saved: " + locked;
                return;
            }

            _grid.EndEdit();
            var rows = new List<BoardTableRow>();
            foreach (DataGridViewRow r in _grid.Rows)
            {
                string instance = Convert.ToString(r.Cells[InstanceColumn].Value);
                rows.Add(new BoardTableRow
                {
                    Master = ((MiniTiounerBoard)r.Tag).ChipSerials[0],
                    Aux = Convert.ToString(r.Cells[AuxColumn].Value) ?? "",
                    Instance = instance == "off" ? BoardTable.Off : int.TryParse(instance, out int n) ? n : BoardTable.AutoInstance,
                    Order = r.Index + 1
                });
            }

            var twice = rows.Where(r => r.Aux.Length > 0).GroupBy(r => r.Aux).FirstOrDefault(g => g.Count() > 1);
            if (twice != null)
            {
                _message.Text = "The aux chip " + twice.Key + " is used for more than one Pro.";
                return;
            }

            BoardTable.Save(rows);
            Saved?.Invoke();   // fills the table again
            _message.Text = "Saved. It takes effect at the next connect of each OpenTuner instance.";
        }
    }
}
