using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Abr.Sdk;
using Topomatic.Alg.Road;

namespace ModelDesk
{
    // Диалог копирования верха конструкции: выбор таблиц источника + дорог-приёмников +
    // режим привязки станций + офсет. На «Применить» -> ConstructionCopier.Copy + тост.
    internal sealed class CopyConstructionDialog : Form
    {
        private readonly RoadAlignment _source;
        private readonly IReadOnlyList<(string name, RoadAlignment road)> _targets;

        private CheckedListBox _tablesList;
        private CheckedListBox _roadsList;
        private RadioButton _rbAbsolute, _rbRelative, _rbOffset;
        private NumericUpDown _offset;
        private Label _preview;

        internal CopyConstructionDialog(RoadAlignment source, IReadOnlyList<(string name, RoadAlignment road)> targets)
        {
            _source = source;
            _targets = targets;

            Text = "Копировать конструкцию";
            Icon = AbrIcon.Create();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(560, 460);
            Font = new Font("Segoe UI", 9f);

            // Таблицы источника
            Controls.Add(new Label { Text = "Таблицы (источник):", AutoSize = true, Location = new Point(12, 12) });
            _tablesList = new CheckedListBox { Location = new Point(12, 34), Size = new Size(260, 150), CheckOnClick = true };
            foreach (var t in ConstructionTables.Read(_source))
                _tablesList.Items.Add(new TableItem(t.Id, t.Title), true);
            _tablesList.ItemCheck += (s, e) => BeginInvoke((Action)UpdatePreview);
            Controls.Add(_tablesList);

            // Дороги-приёмники
            Controls.Add(new Label { Text = "Дороги-приёмники:", AutoSize = true, Location = new Point(288, 12) });
            _roadsList = new CheckedListBox { Location = new Point(288, 34), Size = new Size(260, 150), CheckOnClick = true };
            foreach (var tg in _targets)
                _roadsList.Items.Add(new RoadItem(tg.name, tg.road), false);
            _roadsList.ItemCheck += (s, e) => BeginInvoke((Action)UpdatePreview);
            Controls.Add(_roadsList);

            // Режим привязки станций
            var modeBox = new GroupBox { Text = "Привязка станций", Location = new Point(12, 196), Size = new Size(536, 120) };
            _rbAbsolute = new RadioButton { Text = "Абсолютная (1:1)", AutoSize = true, Location = new Point(16, 26), Checked = true };
            _rbRelative = new RadioButton { Text = "Относительная (масштаб диапазона)", AutoSize = true, Location = new Point(16, 52) };
            _rbOffset = new RadioButton { Text = "Смещение начала + офсет", AutoSize = true, Location = new Point(16, 78) };
            var lblOff = new Label { Text = "Офсет, м:", AutoSize = true, Location = new Point(300, 80) };
            _offset = new NumericUpDown
            {
                Location = new Point(372, 76), Width = 90, DecimalPlaces = 2,
                Minimum = -100000m, Maximum = 100000m, Increment = 1m, Value = 0m
            };
            modeBox.Controls.AddRange(new Control[] { _rbAbsolute, _rbRelative, _rbOffset, lblOff, _offset });
            Controls.Add(modeBox);

            // Превью
            _preview = new Label { AutoSize = true, Location = new Point(12, 330), ForeColor = Color.FromArgb(90, 90, 90) };
            Controls.Add(_preview);

            // Кнопки
            var apply = UiTheme.MakeButton("Применить", BtnKind.Primary, 120, 32);
            apply.Location = new Point(300, 412);
            apply.Click += (s, e) => DoApply();
            var cancel = UiTheme.MakeButton("Отмена", BtnKind.Ghost, 110, 32);
            cancel.Location = new Point(428, 412);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { apply, cancel });
            CancelButton = cancel;

            UpdatePreview();
        }

        private MappingMode SelectedMode()
        {
            if (_rbRelative.Checked) return MappingMode.Relative;
            if (_rbOffset.Checked) return MappingMode.Offset;
            return MappingMode.Absolute;
        }

        private void UpdatePreview()
        {
            int tables = _tablesList.CheckedItems.Count;
            int roads = _roadsList.CheckedItems.Count;
            _preview.Text = "Выбрано таблиц: " + tables + "     дорог-приёмников: " + roads;
        }

        private void DoApply()
        {
            var ids = new HashSet<string>(_tablesList.CheckedItems.Cast<TableItem>().Select(t => t.Id));
            var roads = _roadsList.CheckedItems.Cast<RoadItem>().Select(r => r.Road).ToList();
            if (ids.Count == 0) { MessageBox.Show(this, "Не выбрано ни одной таблицы.", "Проводник"); return; }
            if (roads.Count == 0) { MessageBox.Show(this, "Не выбрано ни одной дороги-приёмника.", "Проводник"); return; }

            try
            {
                var res = ConstructionCopier.Copy(_source, roads, ids, SelectedMode(), (double)_offset.Value);
                string msg = "Скопировано: дорог " + res.Roads + ", таблиц " + res.Tables + ", строк " + res.Rows +
                             ".\nОткат — Ctrl+Z в Robur.";
                if (res.Errors.Count > 0) msg += "\n\nОшибки:\n" + string.Join("\n", res.Errors);
                MessageBox.Show(this, msg, "Проводник");
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось скопировать: " + ex.Message, "Проводник"); }
        }

        private sealed class TableItem
        {
            public readonly string Id;
            private readonly string _title;
            public TableItem(string id, string title) { Id = id; _title = title; }
            public override string ToString() => _title;
        }

        private sealed class RoadItem
        {
            private readonly string _name;
            public readonly RoadAlignment Road;
            public RoadItem(string name, RoadAlignment road) { _name = name; Road = road; }
            public override string ToString() => _name;
        }
    }
}
