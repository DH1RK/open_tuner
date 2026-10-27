using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using opentuner.MediaSources.Minitiouner;

namespace opentuner
{
    // The table of the MiniTiouner Pros, shown in Hardware Info: which aux chip belongs to which master chip and which
    // OpenTuner instance (command line option --instance, 1 if not given) uses the Pro. Saved in settings\minitiouner_boards.json.
    public class BoardTablePanel : Panel
    {
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _message = new Label();
        private DataGridViewComboBoxColumn _aux_column;

        public event Action Saved;

        public BoardTablePanel()
        {
            var info = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(6, 6, 6, 0),
                Text = "Boards: give each board to an OpenTuner instance (started with --instance <n>, default 1); for a MiniTiouner Pro also pair its aux chip."
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

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "HW", ReadOnly = true, FillWeight = 20 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Board", ReadOnly = true, FillWeight = 70 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Chip (I2C + TS)", ReadOnly = true, FillWeight = 55 });
            _aux_column = new DataGridViewComboBoxColumn { HeaderText = "AUX chip + TS2 (Pro only)", FillWeight = 60, FlatStyle = FlatStyle.Flat };
            _grid.Columns.Add(_aux_column);
            var instance_column = new DataGridViewComboBoxColumn { HeaderText = "Instance", FillWeight = 30, FlatStyle = FlatStyle.Flat };
            for (int i = 1; i <= 6; i++)
                instance_column.Items.Add(i.ToString());
            _grid.Columns.Add(instance_column);

            _message.Dock = DockStyle.Bottom;
            _message.Height = 22;
            _message.Padding = new Padding(6, 3, 0, 0);

            var save = new FlexButton { Text = "Save table", Width = 100, Dock = DockStyle.Right };
            save.Click += (s, e) => Save();
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 30 };
            bottom.Controls.Add(_message);
            bottom.Controls.Add(save);

            Controls.Add(_grid);
            Controls.Add(info);
            Controls.Add(bottom);
        }

        // boards: chip serial + board type found on USB, auxes: aux chip serials of the Pros
        public void Fill(List<KeyValuePair<string, string>> boards, List<string> auxes)
        {
            _aux_column.Items.Clear();
            _aux_column.Items.Add("");
            foreach (var a in auxes)
                _aux_column.Items.Add(a);

            _grid.Rows.Clear();
            for (int i = 0; i < boards.Count; i++)
            {
                var row = BoardTable.Find(boards[i].Key);
                _grid.Rows.Add((i + 1).ToString(), boards[i].Value, boards[i].Key, row != null && auxes.Contains(row.Aux) ? row.Aux : "", Math.Max(1, row?.Instance ?? 1).ToString());
                if (boards[i].Value != "Minitiouner Pro 2")
                    _grid.Rows[i].Cells[3].ReadOnly = true;
            }
            _message.Text = "This is OpenTuner instance " + BoardTable.Instance + ". A saved table takes effect at the next connect.";
        }

        private void Save()
        {
            _grid.EndEdit();
            var rows = new List<BoardTableRow>();
            foreach (DataGridViewRow r in _grid.Rows)
            {
                rows.Add(new BoardTableRow
                {
                    Master = Convert.ToString(r.Cells[2].Value) ?? "",
                    Aux = Convert.ToString(r.Cells[3].Value) ?? "",
                    Instance = int.TryParse(Convert.ToString(r.Cells[4].Value), out int n) ? n : 1
                });
            }

            var twice = rows.Where(r => r.Aux.Length > 0).GroupBy(r => r.Aux).FirstOrDefault(g => g.Count() > 1);
            if (twice != null)
            {
                _message.Text = "The aux chip " + twice.Key + " is used for more than one Pro.";
                return;
            }

            BoardTable.Save(rows);
            _message.Text = "Saved. It takes effect at the next connect of each OpenTuner instance.";
            Saved?.Invoke();
        }
    }
}
