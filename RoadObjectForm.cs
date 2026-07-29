using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Abr.Sdk;
using Topomatic.Alg.Road;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;

namespace ModelDesk
{
    // Немодальное окно объекта «Дорога» Проводника: вкладки «Сводка» (RoadSummary) и
    // «Конструкция» (инлайн-правка таблиц + пакетная правка + копирование + пресеты).
    internal sealed class RoadObjectForm : Form
    {
        internal enum Tab { Summary, Construction, Sheets }

        private readonly RoadAlignment _road;
        private readonly string _name;
        private readonly string _pathId;

        private TabControl _tabs;

        // Сводка
        private FlowLayoutPanel _summaryPanel;

        // Конструкция
        private ComboBox _tableCombo;
        private DataGridView _grid;
        private ComboBox _fieldCombo;
        private TextBox _fromInput, _toInput, _valInput;
        private IReadOnlyList<ConstructionTable> _tables;

        internal RoadObjectForm(RoadAlignment road, string name, string pathId)
        {
            _road = road;
            _name = string.IsNullOrEmpty(name) ? "—" : name;
            _pathId = pathId;
            Text = "Дорога: " + _name;
            Icon = AbrIcon.Create();
            ClientSize = new Size(940, 600);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);

            _tabs = new TabControl { Dock = DockStyle.Fill };

            var pageSummary = BuildSummaryTab();
            pageSummary.Name = "summary";
            _tabs.TabPages.Add(pageSummary);

            // Конструкция - сырая часть Проводника, из релиза скрыта (см. ModelDeskPlugin).
            if (ModelDeskPlugin.RoadConstructionEnabled)
            {
                var pageConstruction = BuildConstructionTab();
                pageConstruction.Name = "construction";
                _tabs.TabPages.Add(pageConstruction);
            }

            var pageSheets = new TabPage("Ведомости") { Name = "sheets", BackColor = Color.White };
            pageSheets.Controls.Add(new SheetsTabControl(_pathId));
            _tabs.TabPages.Add(pageSheets);

            Controls.Add(_tabs);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            var close = UiTheme.MakeButton("Закрыть", BtnKind.Ghost, 110, 30);
            close.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            close.Location = new Point(ClientSize.Width - 122, 9);
            close.Click += (s, e) => Close();
            bottom.Controls.Add(close);
            Controls.Add(bottom);

            ReloadSummary();
            if (ModelDeskPlugin.RoadConstructionEnabled) ReloadConstruction();
        }

        internal void SelectTab(Tab tab)
        {
            string name = tab == Tab.Summary      ? "summary"
                        : tab == Tab.Construction ? "construction"
                        :                           "sheets";

            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                if (string.Equals(_tabs.TabPages[i].Name, name, StringComparison.Ordinal))
                {
                    _tabs.SelectedIndex = i;
                    return;
                }
            }
        }

        // ── Вкладка «Сводка» ─────────────────────────────────────────────────
        private TabPage BuildSummaryTab()
        {
            var page = new TabPage("Сводка") { BackColor = Color.White };
            _summaryPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(16)
            };
            page.Controls.Add(_summaryPanel);
            return page;
        }

        private void ReloadSummary()
        {
            _summaryPanel.Controls.Clear();
            RoadSummaryData d = RoadSummary.Read(_road, null);
            d.Name = _name;   // имя у формы точнее (модель не передаётся в окно)

            AddHeader("Дорога");
            AddRow("Имя", d.Name);
            AddRow("Длина", d.Length);
            AddRow("Пикетаж", d.Stationing);
            AddRow("Полос (макс.)", d.Lanes);

            AddHeader("Конструкция");
            AddRow("Ширина полос", d.WidthRange);
            AddRow("Таблица слоёв", d.Layers);

            AddHeader("Поперечники / поверхности");
            AddRow("Поперечников", d.Sections);
            AddRow("Режущих поверхностей", d.Surfaces);
        }

        private void AddHeader(string text)
        {
            _summaryPanel.Controls.Add(new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 14, 0, 6),
                ForeColor = Color.FromArgb(8, 145, 178)
            });
        }

        private void AddRow(string key, string value)
        {
            _summaryPanel.Controls.Add(new Label
            {
                Text = key + ":  " + value,
                AutoSize = true,
                Margin = new Padding(4, 2, 0, 2)
            });
        }

        // ── Вкладка «Конструкция» ────────────────────────────────────────────
        private TabPage BuildConstructionTab()
        {
            var page = new TabPage("Конструкция") { BackColor = Color.White };

            // Верх: выбор таблицы
            var top = new Panel { Dock = DockStyle.Top, Height = 38 };
            top.Controls.Add(new Label { Text = "Таблица:", AutoSize = true, Location = new Point(12, 11) });
            _tableCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(78, 7),
                Width = 260
            };
            _tableCombo.SelectedIndexChanged += (s, e) => { FillGrid(); FillFieldCombo(); };
            top.Controls.Add(_tableCombo);

            // Тулбар пакетной правки
            var batch = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.FromArgb(245, 248, 250) };
            batch.Controls.Add(new Label { Text = "Поле:", AutoSize = true, Location = new Point(12, 11) });
            _fieldCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(56, 7), Width = 130 };
            batch.Controls.Add(_fieldCombo);
            batch.Controls.Add(new Label { Text = "от", AutoSize = true, Location = new Point(196, 11) });
            _fromInput = new TextBox { Location = new Point(220, 7), Width = 70 };
            batch.Controls.Add(_fromInput);
            batch.Controls.Add(new Label { Text = "до", AutoSize = true, Location = new Point(298, 11) });
            _toInput = new TextBox { Location = new Point(322, 7), Width = 70 };
            batch.Controls.Add(_toInput);
            batch.Controls.Add(new Label { Text = "значение", AutoSize = true, Location = new Point(400, 11) });
            _valInput = new TextBox { Location = new Point(462, 7), Width = 70 };
            batch.Controls.Add(_valInput);
            var setBtn = UiTheme.MakeButton("Задать", BtnKind.Secondary, 90, 26);
            setBtn.Location = new Point(542, 7);
            setBtn.Click += (s, e) => ApplyBatch();
            batch.Controls.Add(setBtn);

            // Грид
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None
            };

            // Низ: кнопки
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            var save = UiTheme.MakeButton("Сохранить изменения", BtnKind.Primary, 170, 30);
            save.Location = new Point(12, 9);
            save.Click += (s, e) => SaveInline();
            var copy = UiTheme.MakeButton("Копировать конструкцию…", BtnKind.Secondary, 200, 30);
            copy.Location = new Point(190, 9);
            copy.Click += (s, e) => OpenCopyDialog();
            var savePreset = UiTheme.MakeButton("Сохранить пресет", BtnKind.Ghost, 150, 30);
            savePreset.Location = new Point(398, 9);
            savePreset.Click += (s, e) => SavePreset();
            var applyPreset = UiTheme.MakeButton("Применить пресет", BtnKind.Ghost, 150, 30);
            applyPreset.Location = new Point(556, 9);
            applyPreset.Click += (s, e) => ApplyPreset();
            bottom.Controls.AddRange(new Control[] { save, copy, savePreset, applyPreset });

            page.Controls.Add(_grid);
            page.Controls.Add(bottom);
            page.Controls.Add(batch);
            page.Controls.Add(top);
            return page;
        }

        private ConstructionTable CurrentTable()
        {
            if (_tables == null || _tableCombo.SelectedIndex < 0 || _tableCombo.SelectedIndex >= _tables.Count)
                return null;
            return _tables[_tableCombo.SelectedIndex];
        }

        private void ReloadConstruction()
        {
            string prevId = CurrentTable()?.Id;
            _tables = ConstructionTables.Read(_road);
            _tableCombo.Items.Clear();
            foreach (var t in _tables)
                _tableCombo.Items.Add(t.Title + "  (" + t.Rows.Count + ")");
            if (_tableCombo.Items.Count > 0)
            {
                int idx = 0;
                if (prevId != null)
                    for (int i = 0; i < _tables.Count; i++)
                        if (_tables[i].Id == prevId) { idx = i; break; }
                _tableCombo.SelectedIndex = idx;  // триггерит FillGrid + FillFieldCombo
            }
            else { FillGrid(); FillFieldCombo(); }
        }

        private void FillFieldCombo()
        {
            _fieldCombo.Items.Clear();
            var t = CurrentTable();
            if (t == null) return;
            foreach (string f in t.FieldNames) _fieldCombo.Items.Add(f);
            if (_fieldCombo.Items.Count > 0) _fieldCombo.SelectedIndex = 0;
        }

        private void FillGrid()
        {
            _grid.Rows.Clear();
            _grid.Columns.Clear();
            var t = CurrentTable();
            if (t == null) return;
            foreach (string c in t.Columns) _grid.Columns.Add("c" + _grid.Columns.Count, c);
            foreach (var o in t.Rows)
                _grid.Rows.Add(t.Cells(o).Cast<object>().ToArray());
        }

        private void ApplyBatch()
        {
            var t = CurrentTable();
            if (t == null || _fieldCombo.SelectedIndex < 0) return;
            if (!TryParse(_valInput.Text, out double val))
            { MessageBox.Show(this, "Некорректное значение.", "Проводник"); return; }
            double? from = ParseNullable(_fromInput.Text);
            double? to = ParseNullable(_toInput.Text);
            try
            {
                ConstructionBatchEdit.SetField(_road, t.Id, _fieldCombo.SelectedIndex, from, to, val);
                ReloadConstruction();
                MessageBox.Show(this, "Поле обновлено. Откат — Ctrl+Z в Robur.", "Проводник");
            }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось: " + ex.Message, "Проводник"); }
        }

        private void SaveInline()
        {
            var t = CurrentTable();
            if (t == null) return;
            var urb = _road.Urb;
            if (urb == null) return;
            try
            {
                urb.BeginUpdate("Правка конструкции");
                try
                {
                    var rebuilt = new List<object>(_grid.Rows.Count);
                    foreach (DataGridViewRow r in _grid.Rows)
                    {
                        if (r.IsNewRow) continue;
                        int i = r.Index;
                        if (i >= t.Rows.Count) break;
                        object row = t.Rows[i];
                        row = t.WithStation(row, ParseD(r.Cells[0].Value));
                        for (int f = 0; f < t.FieldNames.Length; f++)
                            row = t.WithField(row, f, ParseD(r.Cells[f + 1].Value));
                        rebuilt.Add(row);
                    }
                    t.Rows.Clear();
                    foreach (var x in rebuilt) t.Rows.Add(x);
                    urb.EndUpdate();
                }
                catch { try { urb.EndUpdate(); } catch { } throw; }
                ReloadConstruction();
                MessageBox.Show(this, "Изменения сохранены. Откат — Ctrl+Z в Robur.", "Проводник");
            }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось сохранить: " + ex.Message, "Проводник"); }
        }

        private void OpenCopyDialog()
        {
            var targets = CollectTargetRoads();
            if (targets.Count == 0)
            { MessageBox.Show(this, "Нет других открытых дорог для копирования.", "Проводник"); return; }
            using (var dlg = new CopyConstructionDialog(_road, targets))
                dlg.ShowDialog(this);
        }

        private void SavePreset()
        {
            if (_tables == null || _tables.Count == 0) { MessageBox.Show(this, "Нет таблиц.", "Проводник"); return; }
            string name = PromptText(this, "Имя пресета:", "Сохранить пресет", SafeName(_name));
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var ids = new HashSet<string>(_tables.Select(t => t.Id));
                ConstructionPresets.Save(name.Trim(), _road, ids);
                MessageBox.Show(this, "Пресет сохранён: " + name.Trim(), "Проводник");
            }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось сохранить пресет: " + ex.Message, "Проводник"); }
        }

        private void ApplyPreset()
        {
            var names = ConstructionPresets.List();
            if (names.Count == 0) { MessageBox.Show(this, "Пресетов нет.", "Проводник"); return; }
            string chosen = PickFromList(this, "Выберите пресет:", "Применить пресет", names);
            if (chosen == null) return;
            try
            {
                var p = ConstructionPresets.Load(chosen);
                var res = ConstructionPresets.Apply(p, new[] { _road }, MappingMode.Absolute, 0);
                ReloadConstruction();
                MessageBox.Show(this, "Применено таблиц: " + res.Tables + ", строк: " + res.Rows +
                    ".\nОткат — Ctrl+Z в Robur.", "Проводник");
            }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось применить пресет: " + ex.Message, "Проводник"); }
        }

        // Открытые дорожные модели, кроме источника (_road).
        internal static List<(string name, RoadAlignment road)> CollectTargetRoadsFor(RoadAlignment source)
        {
            var list = new List<(string, RoadAlignment)>();
            try
            {
                PluginCoreOps.FilterOpenedModels((Predicate<IProjectModel>)delegate (IProjectModel pm)
                {
                    if (RoadModelAccess.TryGetRoadAlignment(pm, out RoadAlignment r) && !ReferenceEquals(r, source))
                    {
                        string n = PluginCoreOps.GetFileName(pm);
                        n = string.IsNullOrEmpty(n) ? "дорога" : System.IO.Path.GetFileNameWithoutExtension(n);
                        list.Add((n, r));
                    }
                    return false;
                });
            }
            catch { }
            return list;
        }

        private List<(string name, RoadAlignment road)> CollectTargetRoads() => CollectTargetRoadsFor(_road);

        // ── helpers ──────────────────────────────────────────────────────────
        private static double ParseD(object o)
        {
            TryParse(Convert.ToString(o), out double v);
            return v;
        }

        private static bool TryParse(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().Replace('.', ',');
            return double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.CurrentCulture, out v)
                || double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        private static double? ParseNullable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return TryParse(s, out double v) ? v : (double?)null;
        }

        private static string SafeName(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        // Мини-диалог ввода строки.
        private static string PromptText(IWin32Window owner, string prompt, string title, string initial)
        {
            using (var f = new Form
            {
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(360, 108),
                MinimizeBox = false, MaximizeBox = false, Font = new Font("Segoe UI", 9f)
            })
            {
                f.Controls.Add(new Label { Text = prompt, AutoSize = true, Location = new Point(12, 12) });
                var tb = new TextBox { Location = new Point(12, 36), Width = 336, Text = initial ?? "" };
                f.Controls.Add(tb);
                var ok = UiTheme.MakeButton("OK", BtnKind.Primary, 90, 28);
                ok.Location = new Point(168, 70); ok.DialogResult = DialogResult.OK;
                var cancel = UiTheme.MakeButton("Отмена", BtnKind.Ghost, 90, 28);
                cancel.Location = new Point(258, 70); cancel.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
            }
        }

        // Мини-диалог выбора из списка.
        private static string PickFromList(IWin32Window owner, string prompt, string title, IReadOnlyList<string> items)
        {
            using (var f = new Form
            {
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(360, 300),
                MinimizeBox = false, MaximizeBox = false, Font = new Font("Segoe UI", 9f)
            })
            {
                f.Controls.Add(new Label { Text = prompt, AutoSize = true, Location = new Point(12, 12) });
                var lb = new ListBox { Location = new Point(12, 36), Size = new Size(336, 210) };
                foreach (var it in items) lb.Items.Add(it);
                if (lb.Items.Count > 0) lb.SelectedIndex = 0;
                f.Controls.Add(lb);
                var ok = UiTheme.MakeButton("OK", BtnKind.Primary, 90, 28);
                ok.Location = new Point(168, 258); ok.DialogResult = DialogResult.OK;
                var cancel = UiTheme.MakeButton("Отмена", BtnKind.Ghost, 90, 28);
                cancel.Location = new Point(258, 258); cancel.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? lb.SelectedItem as string : null;
            }
        }
    }
}
