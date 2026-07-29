using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Abr.Sdk;
using Topomatic.Alg;                          // Alignment
using Topomatic.Alg.Runtime.ServiceClasses;   // ActiveAlignmentReciver<T>
using Topomatic.ApplicationPlatform;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;
using Topomatic.Controls.Dialogs;
using Topomatic.FoundationClasses;

namespace ModelDesk
{
    internal class ModelManagerDialog : Form
    {
        // ── Type filter definitions (one entry per Robur model type) ─────────
        private static readonly (string Label, string Type, Color Color)[] TypeFilters =
        {
            ("ЦММ",       "dtm",          Color.FromArgb(60,  150,  60)),
            ("Трасса",    "survey",       Color.FromArgb(0,   140, 200)),
            ("Дорога",    "road",         Color.FromArgb(0,    90, 200)),
            ("Геология",  "global_glg",   Color.FromArgb(130,  80,  40)),
            ("Метро",     "metro",        Color.FromArgb(150,   0, 150)),
            ("Обустр.",   "arr",          Color.FromArgb(0,    80, 160)),
            ("ЖД",        "rail",         Color.FromArgb(180,  50,   0)),
            ("Путев.",    "railways",     Color.FromArgb(200,  70,   0)),
            ("Инж.сеть", "pipe",          Color.FromArgb(0,   150, 150)),
            ("Труба",     "culvert",      Color.FromArgb(80,  120,   0)),
            ("Площадка", "site",          Color.FromArgb(150, 120,   0)),
            ("Картогр.", "cartograms",    Color.FromArgb(100, 100,   0)),
            ("Земляные", "soilworks",     Color.FromArgb(130,  90,   0)),
            ("DWG",      "application/dwg",   Color.FromArgb(80,   80,  80)),
            ("DWP",      "application/prf-dwl", Color.FromArgb(50,   90, 180)),
            ("Облако",   "cloud",             Color.FromArgb(0,   160, 200)),
            ("Доки",     "application/edocx", Color.FromArgb(100,   0, 150)),
            ("Файл",     "octet-stream",  Color.FromArgb(90,   90,  90)),
            ("Папка",    "folder",        Color.FromArgb(200, 160,   0)),
        };

        private readonly List<ModelEntry> _all = new List<ModelEntry>();
        private readonly HashSet<string> _openIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _activeTypes = new HashSet<string>(StringComparer.Ordinal);
        private readonly ListViewColumnSorter _sorter = new ListViewColumnSorter();
        private readonly Button[] _typeBtns;
        private Button _btnAll;

        private ListView _list;
        private TextBox _filter;
        private Label _statusLabel;
        private Label _infoTitle;
        private RichTextBox _infoText;
        private Button _btnOpen, _btnClose, _btnIsolate, _btnRename;
        private Button _btnOpenCard, _btnSheets;
        private Label  _activeLabel;
        private Button _btnExpandSel;
        private Button _btnCollapseSel;

        private readonly HashSet<string> _activeStatuses = new HashSet<string>(StringComparer.Ordinal);
        private Button _statusBtn;
        private TextBox _noteBox;
        private ModelEntry _currentNoteEntry;

        private int _lastCheckedIndex = -1;
        private int _dragFromIndex   = -1;
        private bool _dragCheckValue;

        private static readonly List<string> _sessionLog = new List<string>();
        private static readonly Dictionary<string, Image> _typeImages = new Dictionary<string, Image>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Image> _treeIcons  = new Dictionary<string, Image>(StringComparer.Ordinal);
        private RichTextBox _logBox;
        private readonly ToolTip _groupTip = new ToolTip { InitialDelay = 300 };

        private ModelDeskSettings _settings = new ModelDeskSettings();
        private string _settingsPath;
        private string _activeGroup; // null = Все
        private string _activePathId; // PathId активной модели проекта (◉)
        private Font _boldFont;       // кэш для подсветки активной строки
        private FlowLayoutPanel _groupPanel;
        private List<(ModelEntry Entry, string OldName)> _lastUndoItems;

        public ModelManagerDialog()
        {
            Text            = "Диспетчер моделей";
            ClientSize      = new Size(920, 520);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize     = new Size(760, 400);
            StartPosition   = FormStartPosition.CenterScreen;
            MinimizeBox     = false;
            MaximizeBox     = false;
            Font            = new Font("Segoe UI", 9f);
            Icon            = AbrIcon.Create();

            _typeBtns = new Button[TypeFilters.Length];
            BuildUi();
            Load += (s, e) => LoadModels();
        }

        // ── UI construction ───────────────────────────────────────────────────

        private void BuildUi()
        {
            // Type filter bar — compact icon-only buttons, single row. Hosted inside pTop so the
            // tree-command icon cluster can sit on the right of the same row.
            EnsureTypeImages();
            var pType = new FlowLayoutPanel
            {
                Location      = new Point(0, 0),
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                AutoScroll    = false,
                Padding       = new Padding(4, 3, 2, 3)
            };
            _btnAll = new Button
            {
                Text      = "Все",
                Width     = 42,
                Height    = 26,
                Margin    = new Padding(0, 0, 6, 0),
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 8.5f),
                Tag       = (string)null
            };
            SetFilterBtnActive(_btnAll, true);
            _btnAll.Click += (s, e) => OnTypeFilterClick(_btnAll);
            pType.Controls.Add(_btnAll);

            var filterTip = new ToolTip { ShowAlways = true, InitialDelay = 200 };
            for (int i = 0; i < TypeFilters.Length; i++)
            {
                var (label, type, _) = TypeFilters[i];
                Image ico;
                _typeImages.TryGetValue(GetTypeIconKey(type), out ico);
                if (ico == null) _typeImages.TryGetValue("other", out ico);
                var btn = new Button
                {
                    Image      = ico,
                    ImageAlign = ContentAlignment.MiddleCenter,
                    Text       = "",
                    Width      = 26,
                    Height     = 26,
                    Margin     = new Padding(1, 0, 1, 0),
                    FlatStyle  = FlatStyle.Flat,
                    Tag        = type,
                    Cursor     = Cursors.Hand
                };
                SetFilterBtnActive(btn, false);
                filterTip.SetToolTip(btn, label);
                btn.Click += (s, e) => OnTypeFilterClick(btn);
                pType.Controls.Add(btn);
                _typeBtns[i] = btn;
            }

            // Project-tree command cluster — sits on the right of the top toolbar.
            // [structure icon (static)] | [expand] [collapse] [expand all] [collapse all]
            EnsureTreeIcons();
            var treeTip = new ToolTip { ShowAlways = true, InitialDelay = 300 };
            Func<string, string, EventHandler, Button> treeBtn = (key, tip, onClick) =>
            {
                Image im; _treeIcons.TryGetValue(key, out im);
                var b = new Button
                {
                    Image = im, ImageAlign = ContentAlignment.MiddleCenter, Text = "",
                    Width = 26, Height = 26, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 210);
                if (onClick != null) b.Click += onClick;
                treeTip.SetToolTip(b, tip);
                return b;
            };

            var pTreeIcons = new Panel { Height = 30 };
            var icoStruct = new Label
            {
                Image = _treeIcons.TryGetValue("structure", out var sIm) ? sIm : null,
                Width = 26, Height = 26, ImageAlign = ContentAlignment.MiddleCenter
            };
            treeTip.SetToolTip(icoStruct, "Структура проекта");

            _btnExpandSel   = treeBtn("expand",       "Развернуть выбранное", (s, e) => ExpandSelectedInTree());
            _btnCollapseSel = treeBtn("collapse",     "Свернуть выбранное",   (s, e) => CollapseSelectedInTree());
            _btnExpandSel.Enabled = false;
            _btnCollapseSel.Enabled = false;
            var btnExpandAll   = treeBtn("expand_all",   "Развернуть всё", (s, e) => ExpandTree());
            var btnCollapseAll = treeBtn("collapse_all", "Свернуть всё",   (s, e) => CollapseTree());

            int tx = 0;
            icoStruct.Location = new Point(tx, 2); tx += 28;
            var sepV = new Panel { Width = 1, Height = 20, BackColor = Color.FromArgb(180, 195, 210), Location = new Point(tx + 2, 5) };
            tx += 7;
            _btnExpandSel.Location   = new Point(tx, 2); tx += 27;
            _btnCollapseSel.Location = new Point(tx, 2); tx += 27;
            btnExpandAll.Location    = new Point(tx, 2); tx += 27;
            btnCollapseAll.Location  = new Point(tx, 2); tx += 27;
            pTreeIcons.Width = tx;
            pTreeIcons.Controls.AddRange(new Control[]
                { icoStruct, sepV, _btnExpandSel, _btnCollapseSel, btnExpandAll, btnCollapseAll });

            var pTop = new Panel { Dock = DockStyle.Top, Height = 34 };
            pTop.Controls.Add(pType);
            pTop.Controls.Add(pTreeIcons);
            EventHandler relayoutTop = (s, e) =>
            {
                pTreeIcons.Top  = 2;
                pTreeIcons.Left = Math.Max(pType.Right + 8, pTop.ClientSize.Width - pTreeIcons.Width - 6);
            };
            pTop.Resize += relayoutTop;
            relayoutTop(null, EventArgs.Empty);

            // Search bar with select-all / deselect buttons
            var pSearch = new Panel { Dock = DockStyle.Top, Height = 32 };
            var lblSearch = new Label { Text = "Поиск:", AutoSize = true, Location = new Point(8, 9) };

            var btnDeselect  = UiTheme.MakeButton("Снять выбор", BtnKind.Ghost, 90, 22);
            btnDeselect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var btnSelectAll = UiTheme.MakeButton("Выбрать все", BtnKind.Ghost, 90, 22);
            btnSelectAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSelectAll.Click += (s, e2) => SetAllChecked(true);
            btnDeselect.Click  += (s, e2) => SetAllChecked(false);

            _statusBtn = UiTheme.MakeButton("Статус ▾", BtnKind.Ghost, 84, 22);
            _statusBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _statusBtn.Click += OnStatusFilterClick;


            _filter = new TextBox { Location = new Point(56, 6), Height = 22 };
            _filter.TextChanged += (s, e) => ApplyFilter();

            pSearch.Controls.Add(lblSearch);
            pSearch.Controls.Add(_filter);
            pSearch.Controls.Add(_statusBtn);
            pSearch.Controls.Add(btnSelectAll);
            pSearch.Controls.Add(btnDeselect);

            Action layoutSearch = () =>
            {
                btnDeselect.Left       = pSearch.Width - btnDeselect.Width - 8;
                btnDeselect.Top        = 5;
                btnSelectAll.Left      = btnDeselect.Left - btnSelectAll.Width - 4;
                btnSelectAll.Top       = 5;
                _statusBtn.Left        = btnSelectAll.Left - _statusBtn.Width - 4;
                _statusBtn.Top         = 5;
                _filter.Width          = _statusBtn.Left - 56 - 8;
            };
            layoutSearch();
            pSearch.Resize += (s, e) => layoutSearch();

            // Button bar
            var pBtn = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            _btnOpen    = ActionBtn("Открыть",          80,  8,   OnOpen);
            _btnClose   = ActionBtn("Закрыть",          80,  96,  OnClose);
            _btnIsolate = ActionBtn("Изолировать",      95,  184, OnIsolate);
            _btnRename  = ActionBtn("Переименовать...", 130, 292, OnRename);

            var btnOrder = UiTheme.MakeButton("Порядок отрисовки", BtnKind.Ghost, 140, 30);
            btnOrder.Location = new Point(430, 7);
            btnOrder.Click += (s, e) => TryExecute("plan_order", new object[0]);

            // "Ещё ▾" dropdown for less-common actions
            var btnMore = UiTheme.MakeButton("Ещё ▾", BtnKind.Ghost, 80, 30);
            btnMore.Location = new Point(578, 7);

            btnMore.Click += (s, e) =>
            {
                var m = new ContextMenuStrip();
                m.Items.Add("Сохранить состояние",    null, (s2, e2) => SaveState());
                m.Items.Add("Восстановить состояние", null, (s2, e2) => RestoreState());
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Экспорт списка...",      null, (s2, e2) => OnExport());
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Статистика проекта...",  null, (s2, e2) => ShowStatistics());
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Авто-группировка по папкам...", null, (s2, e2) => OnAutoGroupByFolder());
                m.Items.Add("Переместить в папку...",         null, (s2, e2) => OnMoveToFolder(null, EventArgs.Empty));
                m.Items.Add("Создать папку в проекте...",    null, (s2, e2) => OnCreateProjectFolder());
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Разблокировать выбранные", null, (s2, e2) => UnlockSelected());
                m.Items.Add("Заблокировать выбранные",  null, (s2, e2) => LockSelected());
                if (_lastUndoItems != null && _lastUndoItems.Count > 0)
                {
                    m.Items.Add(new ToolStripSeparator());
                    m.Items.Add($"Отменить переименование ({_lastUndoItems.Count})...", null, (s2, e2) => OnUndoRename());
                }
                m.Show(btnMore, new Point(0, btnMore.Height));
            };

            var btnRefresh = UiTheme.MakeButton("Обновить", BtnKind.Ghost, 80, 30);
            btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnRefresh.Location = new Point(920 - 80 - 8 - 84, 7);
            btnRefresh.Click += (s, e) => LoadModels();

            var btnDone = UiTheme.MakeButton("Закрыть окно", BtnKind.Primary, 100, 30);
            btnDone.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnDone.Location = new Point(920 - 100 - 8, 7);
            btnDone.Click += (s, e) => Close();

            pBtn.Controls.AddRange(new Control[] { _btnOpen, _btnClose, _btnIsolate, _btnRename, btnOrder, btnMore, btnRefresh, btnDone });
            pBtn.Resize += (s, e) =>
            {
                btnDone.Left    = pBtn.Width - 100 - 8;
                btnRefresh.Left = btnDone.Left - 84;
            };

            // Status bar
            var pStatus = new Panel { Dock = DockStyle.Bottom, Height = 22, BackColor = SystemColors.Control };
            _statusLabel = new Label
            {
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(6, 0, 0, 0),
                ForeColor = Color.FromArgb(80, 80, 80),
                Text      = ""
            };
            pStatus.Controls.Add(_statusLabel);

            var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(210, 210, 210) };

            // Main area: list left + info panel right (no SplitContainer)
            var panelMain = new Panel { Dock = DockStyle.Fill };

            // ListView
            _list = new ListView
            {
                Dock          = DockStyle.Fill,
                View          = View.Details,
                FullRowSelect = true,
                MultiSelect   = false,
                CheckBoxes    = true,
                GridLines     = true,
                HideSelection = false,
                ListViewItemSorter = _sorter,
                SmallImageList     = BuildTypeIcons()
            };
            _list.Columns.Add("Модель",    200);
            _list.Columns.Add("Тип",       80);
            _list.Columns.Add("Статус",    80);
            _list.Columns.Add("Группа",    90);
            _list.Columns.Add("Состояние", 95);
            _list.Columns.Add("Изменён",  100);
            _list.ColumnClick          += OnColumnClick;
            _list.SelectedIndexChanged += (s, e) => { UpdateButtons(); UpdateInfoPanel(SelectedEntry()); };
            _list.ItemCheck            += (s, e) => BeginInvoke(new Action(() => { UpdateStatus(); UpdateButtons(); }));
            _list.MouseClick           += OnListMouseClick;
            _list.DoubleClick          += OnDoubleClick;
            _list.MouseDown            += OnListMouseDown;
            _list.MouseMove            += OnListMouseMove;
            _list.MouseUp              += (s, e) => _dragFromIndex = -1;
            _list.KeyDown              += OnListKeyDown;

            var ctxMenu = new ContextMenuStrip();
            ctxMenu.Items.Add("Открыть",            null, OnOpen);
            ctxMenu.Items.Add("Закрыть",            null, OnClose);
            ctxMenu.Items.Add(new ToolStripSeparator());
            ctxMenu.Items.Add("Изолировать",        null, OnIsolate);
            ctxMenu.Items.Add(new ToolStripSeparator());
            ctxMenu.Items.Add("Переименовать...",       null, OnRename);
            ctxMenu.Items.Add("Переместить в папку...", null, OnMoveToFolder);
            ctxMenu.Items.Add("Создать папку в проекте...", null, (s2, e2) => OnCreateProjectFolder());
            ctxMenu.Items.Add(new ToolStripSeparator());

            var miUnlock = new ToolStripMenuItem("Разблокировать (для редактирования)", null, (s2, e2) => UnlockSelected());
            var miLock   = new ToolStripMenuItem("Заблокировать (только чтение)",        null, (s2, e2) => LockSelected());
            ctxMenu.Items.Add(miUnlock);
            ctxMenu.Items.Add(miLock);
            ctxMenu.Items.Add(new ToolStripSeparator());

            var miGroup = new ToolStripMenuItem("Группа");
            ctxMenu.Items.Add(miGroup);
            ctxMenu.Opening += (s, e2) => BuildGroupSubmenu(miGroup);

            var miStatus = new ToolStripMenuItem("Статус");
            miStatus.DropDownItems.Add("Рабочая",     null, (s2,e2) => AssignStatus(ActiveEntries(), "working"));
            miStatus.DropDownItems.Add("Проверяется", null, (s2,e2) => AssignStatus(ActiveEntries(), "review"));
            miStatus.DropDownItems.Add("Архив",       null, (s2,e2) => AssignStatus(ActiveEntries(), "archive"));
            miStatus.DropDownItems.Add("Черновик",    null, (s2,e2) => AssignStatus(ActiveEntries(), "draft"));
            miStatus.DropDownItems.Add(new ToolStripSeparator());
            miStatus.DropDownItems.Add("Сбросить",      null, (s2,e2) => AssignStatus(ActiveEntries(), null));
            ctxMenu.Items.Add(miStatus);

            ctxMenu.Items.Add(new ToolStripSeparator());
            ctxMenu.Items.Add("Масштаб на модель",  null, (s, e2) => OnZoomToModel());
            ctxMenu.Items.Add(new ToolStripSeparator());
            ctxMenu.Items.Add("Копировать путь",    null, (s, e2) =>
            {
                var en = SelectedEntry();
                if (en == null) return;
                string p = GetDiskPath(en) ?? en.PathId;
                if (!string.IsNullOrEmpty(p)) Clipboard.SetText(p);
            });
            ctxMenu.Items.Add("Открыть папку",      null, (s, e2) =>
            {
                var en = SelectedEntry();
                if (en == null) return;
                string p = GetDiskPath(en);
                string dir = p != null ? (Directory.Exists(p) ? p : Path.GetDirectoryName(p)) : null;
                if (dir != null && Directory.Exists(dir))
                    Process.Start("explorer.exe", dir);
            });
            ctxMenu.Items.Add(new ToolStripSeparator());
            var miSelectFolder = new ToolStripMenuItem("Выбрать все из папки «...»");
            miSelectFolder.Click += (s, e2) =>
            {
                var en = SelectedEntry();
                if (en == null) return;
                string folder = GetParentFolder(en);
                if (string.IsNullOrEmpty(folder)) return;
                _list.BeginUpdate();
                foreach (ListViewItem item in _list.Items)
                    if (item.Tag is ModelEntry me)
                        item.Checked = string.Equals(GetParentFolder(me), folder, StringComparison.Ordinal);
                _list.EndUpdate();
                SyncCheckedState();
                UpdateStatus();
            };
            ctxMenu.Items.Add(miSelectFolder);
            ctxMenu.Opening += (s, e) =>
            {
                e.Cancel = SelectedEntry() == null && _list.CheckedItems.Count == 0;
                var en = SelectedEntry();
                string folder = en != null ? GetParentFolder(en) : null;
                miSelectFolder.Visible = !string.IsNullOrEmpty(folder);
                if (folder != null) miSelectFolder.Text = $"Выбрать все из папки «{folder}»";

                var act = ActiveEntries();
                miUnlock.Enabled = act.Any(x => x.IsLocked);
                miLock.Enabled   = act.Any(x => !x.IsLocked && IsServerModel(x));
            };
            _list.ContextMenuStrip = ctxMenu;

            var panelRight = new Panel { Dock = DockStyle.Right, Width = 200 };
            BuildInfoPanel(panelRight);
            var splitLine = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Color.FromArgb(210, 210, 210) };

            // Order matters: Fill first, then Right panels (processed first = rightmost)
            panelMain.Controls.Add(_list);
            panelMain.Controls.Add(splitLine);
            panelMain.Controls.Add(panelRight);

            // Group filter bar (built dynamically by RebuildGroupPanel)
            _groupPanel = new FlowLayoutPanel
            {
                Dock          = DockStyle.Top,
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = true,
                Padding       = new Padding(4, 2, 2, 2)
            };

            // (Project-tree commands now live as icon buttons in the top toolbar — see pTop above.)

            // Полоса под структурой проекта: работа с выбранной моделью.
            var pModelBar = new Panel { Dock = DockStyle.Bottom, Height = 36 };

            _btnOpenCard = UiTheme.MakeButton("Открыть модель", BtnKind.Ghost, 140, 28);
            _btnOpenCard.Location = new Point(8, 4);
            _btnOpenCard.Enabled  = false;
            _btnOpenCard.Click   += (s, e) => TryOpenCardForSelected(false);

            _btnSheets = UiTheme.MakeButton("Ведомость...", BtnKind.Ghost, 120, 28);
            _btnSheets.Location = new Point(156, 4);
            _btnSheets.Enabled  = false;
            _btnSheets.Click   += (s, e) => TryOpenCardForSelected(true);

            _activeLabel = new Label
            {
                Location  = new Point(290, 9),
                AutoSize  = true,
                ForeColor = Color.FromArgb(110, 110, 110),
                Text      = "Активной модели нет"
            };

            pModelBar.Controls.AddRange(new Control[] { _btnOpenCard, _btnSheets, _activeLabel });

            var sepModelBar = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(225, 225, 225) };

            // Add to form — order matters for Dock layout (later added = higher z-order = docks first)
            Controls.Add(panelMain);
            Controls.Add(pSearch);
            Controls.Add(_groupPanel);
            Controls.Add(pTop);
            Controls.Add(sepModelBar);
            Controls.Add(pModelBar);
            Controls.Add(pBtn);
            Controls.Add(sep);
            Controls.Add(pStatus);
        }

        private void BuildInfoPanel(Panel panel)
        {
            panel.Padding = new Padding(8, 6, 8, 6);
            panel.BackColor = SystemColors.Control;

            _infoTitle = new Label
            {
                Text         = "",
                Font         = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Dock         = DockStyle.Top,
                Height       = 22,
                AutoEllipsis = true
            };
            var infoSep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(200, 200, 200) };
            _infoText = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                ReadOnly    = true,
                BorderStyle = BorderStyle.None,
                BackColor   = SystemColors.Control,
                Font        = new Font("Segoe UI", 9f),
                ScrollBars  = RichTextBoxScrollBars.Vertical,
                TabStop     = false
            };

            // Notes section
            var noteSep   = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(200, 200, 200) };
            var noteLabel = new Label
            {
                Text      = "Заметка:",
                Dock      = DockStyle.Bottom,
                Height    = 18,
                Font      = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(80, 80, 80)
            };
            _noteBox = new TextBox
            {
                Dock        = DockStyle.Bottom,
                Height      = 46,
                Multiline   = true,
                ScrollBars  = ScrollBars.Vertical,
                Font        = new Font("Segoe UI", 9f),
                BackColor   = Color.FromArgb(255, 255, 240)
            };
            _noteBox.Leave += (s, e) => SaveCurrentNote();

            // Log section (bottom of info panel)
            var logSep   = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(200, 200, 200) };
            var logLabel = new Label
            {
                Text      = "Журнал:",
                Dock      = DockStyle.Bottom,
                Height    = 18,
                Font      = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(80, 80, 80)
            };
            _logBox = new RichTextBox
            {
                Dock        = DockStyle.Bottom,
                Height      = 90,
                ReadOnly    = true,
                BorderStyle = BorderStyle.None,
                BackColor   = Color.FromArgb(245, 245, 245),
                Font        = new Font("Segoe UI", 8f),
                ScrollBars  = RichTextBoxScrollBars.Vertical,
                TabStop     = false
            };
            RefreshLog();

            // Dock.Bottom: first added = highest position; last added = closest to panel bottom
            panel.Controls.Add(noteSep);    // just below infoText fill
            panel.Controls.Add(noteLabel);  // below noteSep
            panel.Controls.Add(_noteBox);   // below noteLabel
            panel.Controls.Add(logSep);
            panel.Controls.Add(logLabel);
            panel.Controls.Add(_logBox);    // at panel bottom
            panel.Controls.Add(_infoText);  // Fill: takes remaining space above notes
            panel.Controls.Add(infoSep);    // Top: below title
            panel.Controls.Add(_infoTitle); // Top: very top
        }

        // ── Filter buttons ────────────────────────────────────────────────────

        private void OnTypeFilterClick(Button btn)
        {
            string type = btn.Tag as string;

            if (type == null) // "Все"
            {
                _activeTypes.Clear();
                SetFilterBtnActive(_btnAll, true);
                foreach (var tb in _typeBtns) SetFilterBtnActive(tb, false);
            }
            else
            {
                bool wasActive = _activeTypes.Contains(type);
                if (wasActive) { _activeTypes.Remove(type); SetFilterBtnActive(btn, false); }
                else           { _activeTypes.Add(type);    SetFilterBtnActive(btn, true);  }
                SetFilterBtnActive(_btnAll, _activeTypes.Count == 0);
            }

            ApplyFilter();
        }

        private void SetFilterBtnActive(Button btn, bool active)
        {
            string type = btn.Tag as string;
            int idx = type != null ? Array.FindIndex(TypeFilters, tf => tf.Type == type) : -1;
            Color c = idx >= 0 ? TypeFilters[idx].Color : Color.FromArgb(30, 80, 200);

            btn.FlatStyle = FlatStyle.Flat;
            if (active)
            {
                btn.BackColor = Color.FromArgb(
                    Math.Min(c.R + 155, 255),
                    Math.Min(c.G + 155, 255),
                    Math.Min(c.B + 155, 255));
                btn.FlatAppearance.BorderColor = c;
                btn.FlatAppearance.BorderSize  = 1;
            }
            else
            {
                btn.BackColor = SystemColors.Control;
                btn.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
                btn.FlatAppearance.BorderSize  = 1;
            }
        }

        // ── Data ──────────────────────────────────────────────────────────────

        private void LoadModels()
        {
            _all.Clear();
            _openIds.Clear();

            // Resolve settings file path from first available model's project
            if (_settingsPath == null)
            {
                // Will be set after FilterModels when we have a model to get project URI from
            }

            PluginCoreOps.FilterOpenedModels(pm =>
            {
                var pid = PluginCoreOps.FindModelPathId(pm);
                if (!string.IsNullOrEmpty(pid)) _openIds.Add(pid);
                return false;
            });

            PluginCoreOps.FilterModels(pm =>
            {
                var pid = PluginCoreOps.FindModelPathId(pm);
                if (string.IsNullOrEmpty(pid)) return false;
                var fileName = PluginCoreOps.GetFileName(pm) ?? string.Empty;
                var entry = new ModelEntry
                {
                    Model       = pm,
                    PathId      = pid,
                    DisplayName = Path.GetFileNameWithoutExtension(fileName),
                    ModelType   = pm.ModelType ?? string.Empty,
                    IsOpen      = _openIds.Contains(pid)
                };
                entry.IsLocked = IsModelLocked(entry);
                _all.Add(entry);

                // Grab settings path from first model
                if (_settingsPath == null)
                    _settingsPath = ResolveSettingsPath(pm);

                return false;
            });

            // Load settings once path is known
            if (_settingsPath != null)
                _settings = ModelDeskSettings.Load(_settingsPath);
            else
                _settings = new ModelDeskSettings();

            // Apply groups, statuses, notes to entries
            foreach (var e in _all)
            {
                _settings.ModelGroups.TryGetValue(e.PathId, out e.Group);
                _settings.ModelStatuses.TryGetValue(e.PathId, out e.UserStatus);
                _settings.ModelNotes.TryGetValue(e.PathId, out e.Note);
            }

            // Активная модель проекта (для подсветки ◉)
            _activePathId = GetActiveModelPathId();
            foreach (var e in _all)
                e.IsActive = _activePathId != null &&
                             string.Equals(e.PathId, _activePathId, StringComparison.Ordinal);

            foreach (var e in _all)
            {
                e.CachedDiskPath = GetDiskPath(e);
                if (e.CachedDiskPath != null)
                    try { e.CachedFileDate = File.GetLastWriteTime(e.CachedDiskPath).ToString("dd.MM.yy  HH:mm"); }
                    catch { e.CachedFileDate = ""; }
                else
                    e.CachedFileDate = "";
            }

            RebuildGroupPanel();
            ApplyFilter();
        }

        // Активная модель = ProjectModel текущего активного Alignment-ресивера.
        // Покрывает alignment-модели (дорога/трасса/ЖД/…); для не-alignment (ЦММ и т.п.)
        // вернёт null -> строка просто не подсвечивается (мягкая деградация).
        private string GetActiveModelPathId()
        {
            try
            {
                using (var recv = ActiveAlignmentReciver<Alignment>.CreateReciver(true))
                {
                    IProjectModel pm = recv?.ProjectModel;
                    if (pm != null) return PluginCoreOps.FindModelPathId(pm);
                }
            }
            catch { }
            return null;
        }

        private string ResolveSettingsPath(IProjectModel pm)
        {
            try
            {
                string uri = pm.Project?.TargetProjectUri?.ToString() ?? "";
                if (uri.StartsWith("local:///", StringComparison.OrdinalIgnoreCase))
                    uri = uri.Substring("local:///".Length).Replace('/', Path.DirectorySeparatorChar);
                else if (uri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    uri = uri.Substring(8).Replace('/', Path.DirectorySeparatorChar);
                string dir  = Path.GetDirectoryName(uri);
                string name = Path.GetFileNameWithoutExtension(uri);
                return (dir != null && name != null) ? Path.Combine(dir, name + "_modeldesk.json") : null;
            }
            catch { return null; }
        }

        private void AutoSaveSettings()
        {
            if (_settingsPath == null) return;
            // Sync open IDs from current state
            _settings.OpenIds.Clear();
            _settings.OpenIds.AddRange(_all.Where(x => x.IsOpen).Select(x => x.PathId));
            _settings.Save(_settingsPath);
        }

        private void ApplyFilter()
        {
            var q = _filter.Text.Trim().ToLowerInvariant();
            _list.BeginUpdate();
            _list.Items.Clear();

            foreach (var e in _all)
            {
                // Type filter
                if (_activeTypes.Count > 0 &&
                    !_activeTypes.Contains((e.ModelType ?? "").ToLowerInvariant()))
                    continue;

                // Group filter
                if (_activeGroup != null &&
                    !string.Equals(e.Group, _activeGroup, StringComparison.Ordinal))
                    continue;

                // Status filter
                if (_activeStatuses.Count > 0 &&
                    !_activeStatuses.Contains(e.UserStatus ?? ""))
                    continue;

                // Text filter
                if (q.Length > 0 &&
                    !e.DisplayName.ToLowerInvariant().Contains(q) &&
                    !e.ModelType.ToLowerInvariant().Contains(q) &&
                    !(e.Group ?? "").ToLowerInvariant().Contains(q) &&
                    !e.PathId.ToLowerInvariant().Contains(q))
                    continue;

                var displayText = e.IsLocked ? "🔒 " + e.DisplayName : e.DisplayName;
                var item = new ListViewItem(displayText, GetTypeIconKey(e.ModelType))
                {
                    Tag     = e,
                    Checked = e.IsChecked,
                    UseItemStyleForSubItems = false
                };

                // Subtle type-based row tint (5% type color + 95% white)
                var tc = GetTypeColor(e.ModelType);
                item.BackColor = Color.FromArgb(
                    (int)(255 * 0.95 + tc.R * 0.05),
                    (int)(255 * 0.95 + tc.G * 0.05),
                    (int)(255 * 0.95 + tc.B * 0.05));

                item.SubItems.Add(e.ModelType);
                item.SubItems[1].ForeColor = tc;

                string statusText;
                Color  statusColor;
                if (e.IsActive)      { statusText = "◉ Активна"; statusColor = Color.FromArgb(8, 145, 178); } // cyan
                else if (e.IsOpen)   { statusText = "● Открыта"; statusColor = Color.FromArgb(0, 130, 0); }
                else                 { statusText = "○ Закрыта"; statusColor = Color.FromArgb(140, 140, 140); }
                item.SubItems.Add(statusText);
                item.SubItems[2].ForeColor = statusColor;

                // "Группа" column
                item.SubItems.Add(e.Group ?? "");
                item.SubItems[3].ForeColor = Color.FromArgb(80, 80, 80);

                // "Состояние" column
                var (sLabel, sColor) = GetUserStatusInfo(e.UserStatus);
                item.SubItems.Add(sLabel);
                item.SubItems[4].ForeColor = sColor;

                item.SubItems.Add(e.CachedFileDate ?? "");

                item.ForeColor = e.IsLocked
                    ? Color.FromArgb(185, 70, 70)
                    : e.IsOpen ? SystemColors.ControlText : Color.FromArgb(160, 160, 160);

                // Активная модель: подсветка строки (bold + бледно-циановый фон)
                if (e.IsActive)
                {
                    if (_boldFont == null) _boldFont = new Font(_list.Font, FontStyle.Bold);
                    item.BackColor = Color.FromArgb(224, 247, 250); // pale cyan
                    item.Font = _boldFont;
                    item.SubItems[2].Font = _boldFont;
                    if (!e.IsLocked) item.ForeColor = SystemColors.ControlText;
                }

                _list.Items.Add(item);
            }

            _list.EndUpdate();
            UpdateButtons();
            UpdateStatus();
        }

        private static Color GetTypeColor(string modelType)
        {
            foreach (var tf in TypeFilters)
                if (string.Equals(modelType, tf.Type, StringComparison.OrdinalIgnoreCase))
                    return tf.Color;
            return Color.FromArgb(100, 100, 100);
        }

        private static string GetTypeIconKey(string modelType)
        {
            switch ((modelType ?? "").ToLowerInvariant())
            {
                case "dtm":          return "dtm";
                case "survey":       return "alg";
                case "road":         return "road";
                case "global_glg":   return "geo";
                case "metro":        return "metro";
                case "arr":          return "road_equip";
                case "rail":         return "rwy";
                case "railways":     return "rwy_dev";
                case "pipe":         return "net";
                case "culvert":      return "culvert";
                case "site":         return "site";
                case "cartograms":   return "cartogram";
                case "soilworks":    return "earthwork";
                case "application/dwg":   return "dwg";
                case "application/prf-dwl": return "dwp";
                case "cloud":             return "cloud";
                case "application/edocx": return "docs";
                case "octet-stream": return "ext_file";
                case "folder":       return "folder";
                default:          return "other";
            }
        }

        private static void EnsureTypeImages()
        {
            if (_typeImages.Count > 0) return;
            var defs = new (string Key, string File, Color Fallback)[]
            {
                ("dtm",        "ic_dtm_16dp_1x.png",        Color.FromArgb(60,  150,  60)),
                ("alg",        "ic_alignment_16dp_1x.png",  Color.FromArgb(0,   140, 200)),
                ("road",       "ic_road_16dp_1x.png",       Color.FromArgb(0,    90, 200)),
                ("geo",        "ic_glg_16dp_1x.png",        Color.FromArgb(130,  80,  40)),
                ("metro",      "ic_metro_16dp_1x.png",      Color.FromArgb(150,   0, 150)),
                ("road_equip", "ic_arr_16dp_1x.png",          Color.FromArgb(0,    80, 160)),
                ("rwy",        "ic_rail_16dp_1x.png",        Color.FromArgb(180,  50,   0)),
                ("rwy_dev",    "ic_railways_16dp_1x.png",    Color.FromArgb(200,  70,   0)),
                ("net",        "ic_pipes_16dp_1x.png",      Color.FromArgb(0,   150, 150)),
                ("culvert",    "ic_culvert_16dp_1x.png",    Color.FromArgb(80,  120,   0)),
                ("site",       "ic_site_16dp_1x.png",       Color.FromArgb(150, 120,   0)),
                ("cartogram",  "ic_cartogramms_16dp_1x.png",Color.FromArgb(100, 100,   0)),
                ("earthwork",  "ic_soilworks_16dp_1x.png",  Color.FromArgb(130,  90,   0)),
                ("dwg",        "ic_dwg_16dp_1x.png",        Color.FromArgb(80,   80,  80)),
                ("dwp",        "ic_drawings_16dp_1x.png",   Color.FromArgb(50,   90, 180)),
                ("underlay",   "ic_attachment_16dp_1x.png", Color.FromArgb(110, 110, 110)),
                ("cloud",      "ic_add_hidden_area_16dp_1x.png", Color.FromArgb(0,   160, 200)),
                ("docs",       "ic_drawings_16dp_1x.png",       Color.FromArgb(100,   0, 150)),
                ("ext_file",   "ic_reference_16dp_1x.png",      Color.FromArgb(90,   90,  90)),
                ("folder",     "ic_folder_16dp_1x.png",     Color.FromArgb(200, 160,   0)),
                ("other",      null,                         Color.FromArgb(140, 140, 140)),
            };
            string iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons");
            foreach (var (key, file, fallback) in defs)
            {
                Image img = null;
                if (file != null)
                {
                    string path = Path.Combine(iconsDir, file);
                    if (File.Exists(path)) try { img = Image.FromFile(path); } catch { }
                }
                if (img == null)
                {
                    var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        using (var brush = new SolidBrush(fallback))
                            g.FillEllipse(brush, 2, 3, 11, 11);
                    }
                    img = bmp;
                }
                _typeImages[key] = img;
            }
        }

        private static ImageList BuildTypeIcons()
        {
            EnsureTypeImages();
            var il = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            foreach (var kv in _typeImages)
                il.Images.Add(kv.Key, kv.Value);
            return il;
        }

        // Icons for the project-tree command bar. Each loads icons/<file>.png if present, otherwise a
        // code-drawn placeholder glyph. Drop a real 16×16 PNG of the same name into the icons folder to
        // override — no code change needed.
        private static void EnsureTreeIcons()
        {
            if (_treeIcons.Count > 0) return;
            var defs = new (string Key, string File, string Glyph)[]
            {
                ("structure",    "ic_tree_16dp_1x.png",         "tree"),
                ("expand",       "ic_expand_16dp_1x.png",       "chevron_down"),
                ("collapse",     "ic_collapse_16dp_1x.png",     "chevron_right"),
                ("expand_all",   "ic_expand_all_16dp_1x.png",   "double_down"),
                ("collapse_all", "ic_collapse_all_16dp_1x.png", "double_right"),
            };
            string iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons");
            foreach (var (key, file, glyph) in defs)
            {
                Image img = null;
                string path = Path.Combine(iconsDir, file);
                if (File.Exists(path)) try { img = Image.FromFile(path); } catch { }
                if (img == null) img = DrawTreeGlyph(glyph);
                _treeIcons[key] = img;
            }
        }

        private static Image DrawTreeGlyph(string glyph)
        {
            var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var col = Color.FromArgb(74, 111, 138);
                using (var pen = new Pen(col, 1.8f)
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap   = System.Drawing.Drawing2D.LineCap.Round,
                    LineJoin = System.Drawing.Drawing2D.LineJoin.Round
                })
                using (var br = new SolidBrush(col))
                {
                    switch (glyph)
                    {
                        case "chevron_down":
                            g.DrawLines(pen, new[] { new Point(4, 6), new Point(8, 10), new Point(12, 6) });
                            break;
                        case "chevron_right":
                            g.DrawLines(pen, new[] { new Point(6, 4), new Point(10, 8), new Point(6, 12) });
                            break;
                        case "double_down":
                            g.DrawLines(pen, new[] { new Point(4, 3), new Point(8, 7), new Point(12, 3) });
                            g.DrawLines(pen, new[] { new Point(4, 8), new Point(8, 12), new Point(12, 8) });
                            break;
                        case "double_right":
                            g.DrawLines(pen, new[] { new Point(4, 4), new Point(8, 8), new Point(4, 12) });
                            g.DrawLines(pen, new[] { new Point(9, 4), new Point(13, 8), new Point(9, 12) });
                            break;
                        case "tree":
                            g.DrawLine(pen, 4, 2, 4, 12);   // trunk
                            g.DrawLine(pen, 4, 5, 8, 5);    // branch 1
                            g.DrawLine(pen, 4, 11, 8, 11);  // branch 2
                            br.Color = col;
                            g.FillRectangle(br, 1, 1, 3, 3);    // root node
                            g.FillRectangle(br, 9, 3, 4, 4);    // child node 1
                            g.FillRectangle(br, 9, 9, 4, 4);    // child node 2
                            break;
                    }
                }
            }
            return bmp;
        }

        // ── Log ───────────────────────────────────────────────────────────────

        private void AddLog(string entry)
        {
            string line = $"[{DateTime.Now:HH:mm}] {entry}";
            _sessionLog.Insert(0, line);
            if (_sessionLog.Count > 100) _sessionLog.RemoveAt(100);
            RefreshLog();
        }

        private void RefreshLog()
        {
            if (_logBox == null) return;
            _logBox.Text = string.Join(Environment.NewLine, _sessionLog);
        }

        // ── Sorting ───────────────────────────────────────────────────────────

        private void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == _sorter.SortColumn)
                _sorter.Order = _sorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            else
            {
                _sorter.SortColumn = e.Column;
                _sorter.Order = SortOrder.Ascending;
            }
            _list.Sort();

            for (int i = 0; i < _list.Columns.Count; i++)
            {
                string t = _list.Columns[i].Text.TrimEnd(' ', '▲', '▼');
                if (i == e.Column)
                    t += _sorter.Order == SortOrder.Ascending ? " ▲" : " ▼";
                _list.Columns[i].Text = t;
            }
        }

        // ── Info panel ────────────────────────────────────────────────────────

        private void SaveCurrentNote()
        {
            if (_currentNoteEntry == null || _noteBox == null) return;
            string note = _noteBox.Text.Trim();
            string old  = _currentNoteEntry.Note ?? "";
            if (note == old) return;
            _currentNoteEntry.Note = string.IsNullOrEmpty(note) ? null : note;
            if (string.IsNullOrEmpty(note))
                _settings.ModelNotes.Remove(_currentNoteEntry.PathId);
            else
                _settings.ModelNotes[_currentNoteEntry.PathId] = note;
            AutoSaveSettings();
        }

        private void UpdateInfoPanel(ModelEntry e)
        {
            SaveCurrentNote();
            _currentNoteEntry = e;

            if (e == null)
            {
                _infoTitle.Text = "";
                _infoText.Text  = "";
                if (_noteBox != null) _noteBox.Text = "";
                return;
            }

            _infoTitle.Text = e.DisplayName;

            var sb = new StringBuilder();
            sb.AppendLine("Тип:      " + (string.IsNullOrEmpty(e.ModelType) ? "папка" : e.ModelType));
            sb.AppendLine("Статус:   " + (e.IsOpen ? "Открыта" : "Закрыта"));
            sb.AppendLine("Путь:     " + e.PathId);

            var filePath = TryGetFilePath(e);
            if (filePath != null && File.Exists(filePath))
            {
                var fi = new FileInfo(filePath);
                sb.AppendLine("Дата:     " + fi.LastWriteTime.ToString("dd.MM.yyyy  HH:mm"));
                sb.AppendLine("Размер:   " + FormatSize(fi.Length));
            }

            _infoText.Text = sb.ToString();
            if (_noteBox != null) _noteBox.Text = e.Note ?? "";
        }

        private string TryGetFilePath(ModelEntry e)
        {
            try
            {
                string projUri = e.Model?.Project?.TargetProjectUri?.ToString() ?? "";
                if (projUri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    projUri = projUri.Substring(8).Replace('/', '\\');
                string dir = Path.GetDirectoryName(projUri);
                if (dir == null) return null;
                return Path.Combine(dir, e.PathId.Replace('/', '\\'));
            }
            catch { return null; }
        }

        private static string FormatSize(long b)
        {
            if (b > 1024 * 1024) return $"{b / (1024.0 * 1024):F1} МБ";
            if (b > 1024)        return $"{b / 1024.0:F1} КБ";
            return $"{b} Б";
        }

        // ── Status ────────────────────────────────────────────────────────────

        private void UpdateStatus()
        {
            int open     = _all.Count(x => x.IsOpen);
            int checked_ = _list.CheckedItems.Count;
            _statusLabel.Text = $"Всего: {_all.Count}  |  Открыто: {open}  |  Отмечено: {checked_}";
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ModelEntry SelectedEntry()
        {
            return _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ModelEntry : null;
        }

        private List<ModelEntry> CheckedEntries()
        {
            var result = new List<ModelEntry>();
            foreach (ListViewItem item in _list.CheckedItems)
                if (item.Tag is ModelEntry e) result.Add(e);
            return result;
        }

        private List<ModelEntry> ActiveEntries()
        {
            var ch = CheckedEntries();
            if (ch.Count > 0) return ch;
            var sel = SelectedEntry();
            return sel != null ? new List<ModelEntry> { sel } : new List<ModelEntry>();
        }

        private void UpdateButtons()
        {
            var e = SelectedEntry();
            bool any = e != null || _list.CheckedItems.Count > 0;
            _btnOpen.Enabled    = any;
            _btnClose.Enabled   = any;
            _btnIsolate.Enabled = any;
            _btnRename.Enabled  = any;
            bool hasModel = e != null && e.Model != null;
            if (_btnOpenCard != null) _btnOpenCard.Enabled = hasModel;
            if (_btnSheets   != null) _btnSheets.Enabled   = hasModel;
            UpdateActiveLabel();
            bool anyActive = ActiveEntries().Count > 0;
            if (_btnExpandSel != null)   _btnExpandSel.Enabled   = anyActive;
            if (_btnCollapseSel != null) _btnCollapseSel.Enabled = anyActive;
        }

        // ── Блокировка/разблокировка (серверные модели: per-user право "editor") ──
        // Не-локальная модель без права "editor" открывается только для чтения (🔒). Выдача права
        // (AddPermission) делает её редактируемой; снятие — снова read-only. Локальные модели всегда
        // редактируемы (PluginCoreOps.HasPermission для local-схемы возвращает true) → не помечаются.
        private static bool IsModelLocked(ModelEntry e)
        {
            var pm = e?.Model;
            if (pm == null || e.ModelType == "folder") return false;
            try { return !PluginCoreOps.HasPermission(pm, PluginCoreOps.EDITOR_PERMISSION); }
            catch { return false; }
        }

        private static bool IsServerModel(ModelEntry e)
        {
            var scheme = e?.Model?.Uri?.Scheme;
            return !string.IsNullOrEmpty(scheme)
                && !scheme.Equals("local", StringComparison.OrdinalIgnoreCase)
                && !scheme.Equals("file",  StringComparison.OrdinalIgnoreCase);
        }

        private void UnlockSelected()
        {
            var targets = ActiveEntries().Where(x => x.IsLocked && x.Model != null).ToList();
            if (targets.Count == 0) { MessageDlg.Show("Среди выбранных нет заблокированных моделей."); return; }
            if (MessageBox.Show(this,
                    $"Разблокировать редактирование выбранных моделей: {targets.Count}?\n" +
                    "Модель станет доступна для изменения (переименование, перемещение, правка).",
                    "Разблокировать", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            int ok = 0; var errors = new List<string>();
            foreach (var e in targets)
            {
                try { PluginCoreOps.AddPermission(e.Model, PluginCoreOps.EDITOR_PERMISSION); ok++; }
                catch (Exception ex) { errors.Add($"{e.DisplayName}: {ex.Message}"); }
            }
            LoadModels();
            _statusLabel.Text = $"Разблокировано: {ok} из {targets.Count}";
            if (errors.Count > 0)
                MessageDlg.Show($"Не удалось разблокировать ({errors.Count}):\n" + string.Join("\n", errors));
        }

        private void LockSelected()
        {
            var targets = ActiveEntries().Where(x => !x.IsLocked && x.Model != null && IsServerModel(x)).ToList();
            if (targets.Count == 0) { MessageDlg.Show("Среди выбранных нет разблокированных серверных моделей."); return; }
            int ok = 0; var errors = new List<string>();
            foreach (var e in targets)
            {
                try { PluginCoreOps.RemovePermission(e.Model, PluginCoreOps.EDITOR_PERMISSION); ok++; }
                catch (Exception ex) { errors.Add($"{e.DisplayName}: {ex.Message}"); }
            }
            LoadModels();
            _statusLabel.Text = $"Заблокировано: {ok} из {targets.Count}";
            if (errors.Count > 0)
                MessageDlg.Show($"Не удалось заблокировать ({errors.Count}):\n" + string.Join("\n", errors));
        }

        private void CollapseTree()
        {
            Control treeCtrl = null;
            foreach (Form form in Application.OpenForms)
            {
                if (form == this) continue;
                var found = FindControlByName(form, "treeview");
                if (found != null) { treeCtrl = found; break; }
            }
            if (treeCtrl == null) return;
            var bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            // No TreeBatch: BeginUpdate suppresses AfterCollapse → IsExpanded stays stale (True)
            // after collapse, causing subsequent CollapseNode calls to toggle-open instead of skip.
            var rootsColl = GetRootNodeCollection(treeCtrl, bf, out Type nodeCollType);
            if (rootsColl != null)
            {
                EnsureCollectionMethods(nodeCollType);
                EnsureStateGetterLearned(rootsColl, nodeCollType, bf);
                CollapseNodeCollection(rootsColl, nodeCollType, bf);
            }
            treeCtrl.Invalidate(true);
            treeCtrl.Update();
        }

        private void ExpandTree()
        {
            Control treeCtrl = null;
            foreach (Form form in Application.OpenForms)
            {
                if (form == this) continue;
                var found = FindControlByName(form, "treeview");
                if (found != null) { treeCtrl = found; break; }
            }
            if (treeCtrl == null) return;
            var bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            // No TreeBatch: IsExpanded must update after each call. ExpandNode is idempotent (no-op on
            // already-open nodes), so no CollapseAll-first is needed — expanding a closed node also
            // learns the state getter via its children edge.
            var rootsColl = GetRootNodeCollection(treeCtrl, bf, out Type nodeCollType);
            if (rootsColl == null) return;
            EnsureCollectionMethods(nodeCollType);
            EnsureStateGetterLearned(rootsColl, nodeCollType, bf);
            ExpandNodeCollection(rootsColl, nodeCollType, bf);
            treeCtrl.Invalidate(true);
            treeCtrl.Update();
        }

        private void ExpandSelectedInTree() { ApplySelectedToTree(expand: true); }

        private void CollapseSelectedInTree() { ApplySelectedToTree(expand: false); }

        // Expand or collapse ONLY the nodes of the models selected/checked in the manager.
        // expand=true : each matched node opens one level + its ancestors open so it is visible.
        // expand=false: each matched node's whole subtree collapses. Non-selected models untouched.
        // No RefreshTree here — that would reset other models' state. The tree is traversed as-is.
        // Node method caches (_nodeCollapseMethod etc.) persist across calls — detected once per session.
        private void ApplySelectedToTree(bool expand)
        {
            Control treeCtrl = null;
            foreach (Form form in Application.OpenForms)
            {
                if (form == this) continue;
                var found = FindControlByName(form, "treeview");
                if (found != null) { treeCtrl = found; break; }
            }
            if (treeCtrl == null) return;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in ActiveEntries())
                if (!string.IsNullOrEmpty(entry.DisplayName)) names.Add(entry.DisplayName);
            if (names.Count == 0) return;

            var bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            if (expand)
            {
                // BeginUpdate suppresses BeforeExpand in TreeViewEx — lazy children don't load.
                // Run without batch so each ExpandNode triggers Robur's lazy-load.
                var rootsColl = GetRootNodeCollection(treeCtrl, bf, out Type nodeCollType);
                if (rootsColl != null)
                {
                    EnsureCollectionMethods(nodeCollType);
                    EnsureStateGetterLearned(rootsColl, nodeCollType, bf);
                    ApplyToMatchingNodes(rootsColl, nodeCollType, bf, names, expand: true);
                }
                treeCtrl.Invalidate(true);
                treeCtrl.Update();
            }
            else
            {
                // Learn the state getter BEFORE the batch — its probe expands a node to read the
                // children edge, and BeginUpdate would suppress that lazy-load.
                var rootsColl0 = GetRootNodeCollection(treeCtrl, bf, out Type nct0);
                if (rootsColl0 != null)
                {
                    EnsureCollectionMethods(nct0);
                    EnsureStateGetterLearned(rootsColl0, nct0, bf);
                }
                TreeBatch(treeCtrl, () =>
                {
                    var rootsColl = GetRootNodeCollection(treeCtrl, bf, out Type nodeCollType);
                    if (rootsColl != null)
                        ApplyToMatchingNodes(rootsColl, nodeCollType, bf, names, expand: false);
                });
            }
        }

        private void SyncCheckedState()
        {
            foreach (ListViewItem item in _list.Items)
                if (item.Tag is ModelEntry e)
                    e.IsChecked = item.Checked;
        }

        private bool TryExecute(string cmd, object[] args)
        {
            try { ApplicationHost.Current.Plugins.Execute(cmd, args); return true; }
            catch { return false; }
        }

        private Button ActionBtn(string text, int width, int x, EventHandler click)
        {
            var b = UiTheme.MakeButton(text, BtnKind.Ghost, width, 30);
            b.Location = new Point(x, 7);
            b.Enabled = false;
            b.Click += click;
            return b;
        }

        // ── Actions ───────────────────────────────────────────────────────────

        private void OnOpen(object sender, EventArgs e)
        {
            foreach (var entry in ActiveEntries())
            {
                if (entry.IsOpen) continue;
                if (!TryExecute("open", new object[] { entry.PathId })) continue;
                entry.IsOpen = true;
                _openIds.Add(entry.PathId);
            }
            RefreshRows();
        }

        private void OnClose(object sender, EventArgs e)
        {
            foreach (var entry in ActiveEntries())
            {
                if (!entry.IsOpen) continue;
                if (!TryExecute("close", new object[] { entry.PathId })) continue;
                entry.IsOpen = false;
                _openIds.Remove(entry.PathId);
            }
            RefreshRows();
        }

        private void OnIsolate(object sender, EventArgs e)
        {
            var targets = new HashSet<ModelEntry>(ActiveEntries());
            if (targets.Count == 0) return;

            foreach (var entry in _all)
            {
                if (targets.Contains(entry))
                {
                    if (!entry.IsOpen && TryExecute("open", new object[] { entry.PathId }))
                        entry.IsOpen = true;
                }
                else
                {
                    if (entry.IsOpen && TryExecute("close", new object[] { entry.PathId }))
                        entry.IsOpen = false;
                }
            }

            _openIds.Clear();
            foreach (var entry in _all.Where(x => x.IsOpen))
                _openIds.Add(entry.PathId);

            RefreshRows();
        }

        private void OnRename(object sender, EventArgs e)
        {
            SyncCheckedState();
            var entries = ActiveEntries();
            if (entries.Count == 0) return;

            using (var dlg = new BulkRenameDialog(entries))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    foreach (var logEntry in dlg.RenameLog)
                        AddLog(logEntry);
                    if (dlg.UndoItems.Count > 0)
                        _lastUndoItems = new List<(ModelEntry, string)>(dlg.UndoItems);
                    LoadModels();
                    if (dlg.RenameLog.Count > 0)
                        _statusLabel.Text = $"Переименовано: {dlg.RenameLog.Count}  |  Не забудьте сохранить проект (Ctrl+S)";
                }
            }
        }

        private void OnUndoRename()
        {
            if (_lastUndoItems == null || _lastUndoItems.Count == 0) return;
            var errors = new List<string>();
            int undone = 0;
            foreach (var item in _lastUndoItems)
            {
                string currentName = item.Entry.DisplayName;
                if (BulkRenameDialog.TryRenameEntry(item.Entry, item.OldName, out string error))
                {
                    AddLog($"Undo: {currentName} → {item.OldName}");
                    undone++;
                }
                else
                    errors.Add(error);
            }
            _lastUndoItems = null;
            LoadModels();
            if (errors.Count > 0)
                MessageDlg.Show($"Ошибки при откате ({errors.Count}):\n" + string.Join("\n", errors));
            _statusLabel.Text = $"Откат: {undone} восстановлено";
        }

        private void SetAllChecked(bool value)
        {
            _list.BeginUpdate();
            foreach (ListViewItem item in _list.Items)
                item.Checked = value;
            _list.EndUpdate();
            SyncCheckedState();
            UpdateStatus();
        }

        private void OnListMouseClick(object sender, MouseEventArgs e)
        {
            var info = _list.HitTest(e.X, e.Y);
            if (info.Item == null) return;

            int index = info.Item.Index;

            if ((ModifierKeys & Keys.Shift) == Keys.Shift && _lastCheckedIndex >= 0 && _lastCheckedIndex != index)
            {
                // Range select like Windows Explorer: anchor stays at _lastCheckedIndex
                bool targetState = info.Item.Checked; // state after native checkbox toggle
                int from = Math.Min(_lastCheckedIndex, index);
                int to   = Math.Max(_lastCheckedIndex, index);

                _list.BeginUpdate();
                for (int i = from; i <= to; i++)
                {
                    _list.Items[i].Selected = true;
                    if (i != index) // clicked item already toggled by native handler
                        _list.Items[i].Checked = targetState;
                }
                _list.EndUpdate();
                SyncCheckedState();
                UpdateStatus();
                // Do NOT update _lastCheckedIndex — anchor stays fixed (Explorer behavior)
            }
            else
            {
                _lastCheckedIndex = index; // new anchor on plain click
            }
        }

        private void OnDoubleClick(object sender, EventArgs e)
        {
            var entry = SelectedEntry();
            if (entry == null) return;
            TryExecute("activate", new object[] { entry.PathId });
            if (!entry.IsOpen) { entry.IsOpen = true; _openIds.Add(entry.PathId); }

            // активной стала эта модель -> перенести подсветку ◉
            _activePathId = entry.PathId;
            foreach (var en in _all)
                en.IsActive = string.Equals(en.PathId, _activePathId, StringComparison.Ordinal);

            RefreshRows();
            AddLog($"Активировано: {entry.DisplayName}");
        }

        // Карточка модели — отдельная кнопка (двойной клик только активирует модель).
        // Модель делается активной: каталог ведомостей строится по активной модели.
        internal bool TryOpenCardForSelected(bool onSheets)
        {
            var entry = SelectedEntry();
            if (entry == null || entry.Model == null) return false;

            TryExecute("activate", new object[] { entry.PathId });
            if (!entry.IsOpen) { entry.IsOpen = true; _openIds.Add(entry.PathId); }
            _activePathId = entry.PathId;
            foreach (var en in _all)
                en.IsActive = string.Equals(en.PathId, _activePathId, StringComparison.Ordinal);
            RefreshRows();

            AddLog($"Карточка модели: {entry.DisplayName}");
            ModelDeskPlugin.OpenModelCard(entry.Model, onSheets);
            return true;
        }

        private void UpdateActiveLabel()
        {
            if (_activeLabel == null) return;

            string name = null;
            foreach (var en in _all)
                if (en.IsActive) { name = en.DisplayName; break; }

            _activeLabel.Text = string.IsNullOrEmpty(name)
                ? "Активной модели нет"
                : "Активна: " + name;
        }

        private void OnListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var info = _list.HitTest(e.X, e.Y);
            if (info.Item == null) return;
            _dragFromIndex  = info.Item.Index;
            _dragCheckValue = !info.Item.Checked; // intended state after toggle
        }

        private void OnListMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragFromIndex < 0 || e.Button != MouseButtons.Left) return;
            var info = _list.HitTest(e.X, e.Y);
            if (info.Item == null || info.Item.Index == _dragFromIndex) return;
            if (info.Item.Checked != _dragCheckValue)
            {
                if (info.Item.Tag is ModelEntry me) me.IsChecked = _dragCheckValue;
                info.Item.Checked = _dragCheckValue;
                UpdateStatus();
            }
        }

        private void RefreshRows()
        {
            _list.BeginUpdate();
            foreach (ListViewItem item in _list.Items)
            {
                if (!(item.Tag is ModelEntry e)) continue;
                if (e.IsActive)    { item.SubItems[2].Text = "◉ Активна"; item.SubItems[2].ForeColor = Color.FromArgb(8, 145, 178); }
                else if (e.IsOpen) { item.SubItems[2].Text = "● Открыта"; item.SubItems[2].ForeColor = Color.FromArgb(0, 130, 0); }
                else               { item.SubItems[2].Text = "○ Закрыта"; item.SubItems[2].ForeColor = Color.FromArgb(140, 140, 140); }
                item.SubItems[3].Text      = e.Group ?? "";
                var (sLabel, sColor)       = GetUserStatusInfo(e.UserStatus);
                item.SubItems[4].Text      = sLabel;
                item.SubItems[4].ForeColor = sColor;
                item.ForeColor             = e.IsLocked ? Color.FromArgb(185, 70, 70)
                                             : e.IsOpen ? SystemColors.ControlText : Color.FromArgb(160, 160, 160);

                if (e.IsActive)
                {
                    if (_boldFont == null) _boldFont = new Font(_list.Font, FontStyle.Bold);
                    item.BackColor = Color.FromArgb(224, 247, 250);
                    item.Font = _boldFont;
                    item.SubItems[2].Font = _boldFont;
                }
                else
                {
                    item.Font = _list.Font;
                    item.SubItems[2].Font = _list.Font;
                    var tc = GetTypeColor(e.ModelType);
                    item.BackColor = Color.FromArgb(
                        (int)(255 * 0.95 + tc.R * 0.05),
                        (int)(255 * 0.95 + tc.G * 0.05),
                        (int)(255 * 0.95 + tc.B * 0.05));
                }
            }
            _list.EndUpdate();
            UpdateStatus();
        }

        // ── Keyboard shortcuts ────────────────────────────────────────────────

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:  OnOpen(null, EventArgs.Empty);   e.Handled = true; break;
                case Keys.Delete: OnClose(null, EventArgs.Empty);  e.Handled = true; break;
                case Keys.F2:     OnRename(null, EventArgs.Empty); e.Handled = true; break;
                case Keys.F5:     LoadModels();                    e.Handled = true; break;
                case Keys.A when e.Control: SetAllChecked(true);   e.Handled = true; break;
            }
        }

        // ── Disk path helper ─────────────────────────────────────────────────

        private static string GetDiskPath(ModelEntry e)
        {
            try
            {
                string abs = e.Model?.Uri?.AsAbsoluteUri ?? "";
                string path = MoveToFolderDialog.UriToDiskPath(abs);
                if (path == null) return null;
                if (File.Exists(path) || Directory.Exists(path)) return path;
                string dir  = Path.GetDirectoryName(path);
                string name = Path.GetFileName(path);
                if (dir == null || !Directory.Exists(dir)) return null;
                var hits = Directory.GetFiles(dir, name + ".*");
                return hits.Length == 1 ? hits[0] : null;
            }
            catch { return null; }
        }

        // ── State save / restore ─────────────────────────────────────────────

        private void SaveState()
        {
            if (_settingsPath == null) { MessageDlg.Show("Не удалось определить путь проекта."); return; }
            AutoSaveSettings();
            AddLog($"Состояние сохранено: {_settings.OpenIds.Count} открыто");
            _statusLabel.Text = $"Состояние сохранено → {Path.GetFileName(_settingsPath)}";
        }

        private void RestoreState()
        {
            if (_settingsPath == null || !File.Exists(_settingsPath))
            {
                MessageDlg.Show("Файл состояния не найден. Сохраните состояние сначала.");
                return;
            }
            var snap   = ModelDeskSettings.Load(_settingsPath);
            var saved  = new HashSet<string>(snap.OpenIds, StringComparer.Ordinal);
            int opened = 0, closed = 0;
            foreach (var entry in _all)
            {
                bool should = saved.Contains(entry.PathId);
                if (should && !entry.IsOpen)
                {
                    if (TryExecute("open", new object[] { entry.PathId }))
                    { entry.IsOpen = true; _openIds.Add(entry.PathId); opened++; }
                }
                else if (!should && entry.IsOpen)
                {
                    if (TryExecute("close", new object[] { entry.PathId }))
                    { entry.IsOpen = false; _openIds.Remove(entry.PathId); closed++; }
                }
            }
            RefreshRows();
            AddLog($"Состояние восстановлено: +{opened} открыто, -{closed} закрыто");
            _statusLabel.Text = $"Восстановлено: открыто +{opened}, закрыто -{closed}";
        }

        // ── Statistics ───────────────────────────────────────────────────────

        private void ShowStatistics()
        {
            var byType    = new Dictionary<string, (int count, long size)>(StringComparer.OrdinalIgnoreCase);
            long total    = 0;
            var sizePairs = new List<(ModelEntry e, long size)>();

            foreach (var e in _all)
            {
                string type = string.IsNullOrEmpty(e.ModelType) ? "папка" : e.ModelType;
                long   sz   = 0;
                string dp   = GetDiskPath(e);
                if (dp != null && File.Exists(dp)) sz = new FileInfo(dp).Length;
                total += sz;
                if (!byType.ContainsKey(type)) byType[type] = (0, 0);
                var cur = byType[type];
                byType[type] = (cur.count + 1, cur.size + sz);
                sizePairs.Add((e, sz));
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Всего моделей: {_all.Count}");
            sb.AppendLine();
            sb.AppendLine(JCol("Тип", 14) + JCol("Кол.", 6, true) + JCol("Размер", 12, true));
            sb.AppendLine(new string('─', 34));
            foreach (var kv in byType.OrderByDescending(x => x.Value.size))
                sb.AppendLine(JCol(kv.Key, 14) + JCol(kv.Value.count.ToString(), 6, true) + JCol(FormatSize(kv.Value.size), 12, true));
            sb.AppendLine(new string('─', 34));
            sb.AppendLine(JCol("Итого:", 14) + JCol(_all.Count.ToString(), 6, true) + JCol(FormatSize(total), 12, true));
            sb.AppendLine();
            sb.AppendLine("Топ-5 самых тяжёлых:");
            foreach (var (e, sz) in sizePairs.OrderByDescending(x => x.size).Take(5))
                sb.AppendLine($"  {e.DisplayName}  ({e.ModelType})  {FormatSize(sz)}");

            var dlg = new Form
            {
                Text            = "Статистика — ModelDesk",
                Size            = new Size(380, 420),
                MinimumSize     = new Size(320, 300),
                StartPosition   = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimizeBox     = false,
                MaximizeBox     = false,
                Icon            = AbrIcon.Create()
            };
            var rtb = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                ReadOnly    = true,
                Font        = new Font("Courier New", 9f),
                Text        = sb.ToString(),
                BackColor   = SystemColors.Window,
                BorderStyle = BorderStyle.None
            };
            dlg.Controls.Add(rtb);
            dlg.ShowDialog(this);
        }

        private static string JCol(string s, int w, bool right = false)
        {
            if (s.Length >= w) s = s.Substring(0, w);
            return right ? s.PadLeft(w) : s.PadRight(w);
        }

        // ── Export ────────────────────────────────────────────────────────────

        private void OnExport()
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title    = "Экспорт списка моделей";
                sfd.Filter   = "Текстовый файл (*.txt)|*.txt|CSV (*.csv)|*.csv";
                sfd.FileName = "models";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;

                bool csv = Path.GetExtension(sfd.FileName)
                               .Equals(".csv", StringComparison.OrdinalIgnoreCase);

                var lines = new List<string>();
                if (csv)
                {
                    lines.Add("Имя;Тип;Статус;Размер;Путь");
                    foreach (var e in _all)
                    {
                        string status = e.IsOpen ? "Открыта" : "Закрыта";
                        string size     = "";
                        string diskPath = GetDiskPath(e);
                        if (diskPath != null && File.Exists(diskPath))
                            size = FormatSize(new FileInfo(diskPath).Length);
                        string uri = e.Model?.Uri?.AsAbsoluteUri ?? e.PathId;
                        lines.Add(string.Join(";", new[]
                        {
                            Q(e.DisplayName), Q(e.ModelType), Q(status), Q(size), Q(uri)
                        }));
                    }
                }
                else
                {
                    lines.Add(JCol("Имя", 35) + JCol("Тип", 14) + JCol("Статус", 12) + JCol("Размер", 10, true) + "  Путь");
                    lines.Add(new string('-', 100));
                    foreach (var e in _all)
                    {
                        string status = e.IsOpen ? "Открыта" : "Закрыта";
                        string size     = "";
                        string diskPath = GetDiskPath(e);
                        if (diskPath != null && File.Exists(diskPath))
                            size = FormatSize(new FileInfo(diskPath).Length);
                        string uri = e.Model?.Uri?.AsAbsoluteUri ?? e.PathId;
                        lines.Add(JCol(e.DisplayName, 35) + JCol(e.ModelType, 14) + JCol(status, 12) + JCol(size, 10, true) + "  " + uri);
                    }
                }

                File.WriteAllLines(sfd.FileName, lines, Encoding.UTF8);
                AddLog($"Экспорт: {_all.Count} моделей → {Path.GetFileName(sfd.FileName)}");
                _statusLabel.Text = $"Экспортировано: {sfd.FileName}";
            }
        }

        private static string Q(string s) =>
            "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        // ── Folder helpers ────────────────────────────────────────────────────

        private static string GetParentFolder(ModelEntry e)
        {
            var parts = (e.PathId ?? "").Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[parts.Length - 2] : null;
        }

        private void OnAutoGroupByFolder()
        {
            int assigned = 0;
            foreach (var e in _all)
            {
                string folder = GetParentFolder(e);
                if (string.IsNullOrEmpty(folder)) continue;
                if (!_settings.Groups.Contains(folder))
                    _settings.Groups.Add(folder);
                e.Group = folder;
                _settings.ModelGroups[e.PathId] = folder;
                assigned++;
            }
            AutoSaveSettings();
            RebuildGroupPanel();
            ApplyFilter();
            AddLog($"Авто-группировка по папкам: {assigned} моделей");
            _statusLabel.Text = $"Авто-группировка: {assigned} назначено";
        }

        // ── Zoom to model ─────────────────────────────────────────────────────

        private void OnZoomToModel()
        {
            var entry = SelectedEntry();
            if (entry == null) return;

            if (!entry.IsOpen)
            {
                if (!TryExecute("open", new object[] { entry.PathId })) return;
                entry.IsOpen = true;
                _openIds.Add(entry.PathId);
                RefreshRows();
            }

            // Команда Robur "zoom %pathId" — "Показать в плане" (из core.plugin)
            TryExecute("zoom", new object[] { entry.PathId });
        }

        // ── User status ───────────────────────────────────────────────────────

        private static (string label, Color color) GetUserStatusInfo(string status)
        {
            switch (status)
            {
                case "working":  return ("✔ Рабочая",      Color.FromArgb(0, 145, 0));
                case "review":   return ("◎ Проверяется",  Color.FromArgb(190, 105, 0));
                case "archive":  return ("▣ Архив",         Color.FromArgb(110, 110, 110));
                case "draft":    return ("✎ Черновик",      Color.FromArgb(80, 100, 190));
                default:         return ("",                SystemColors.ControlText);
            }
        }

        private void AssignStatus(List<ModelEntry> entries, string statusKey)
        {
            if (entries.Count == 0) return;
            foreach (var e in entries)
            {
                e.UserStatus = statusKey;
                if (statusKey == null)
                    _settings.ModelStatuses.Remove(e.PathId);
                else
                    _settings.ModelStatuses[e.PathId] = statusKey;
            }
            AutoSaveSettings();
            ApplyFilter();
        }

        // ── Status filter dropdown ────────────────────────────────────────────

        private static readonly (string Label, string Key, Color Color)[] StatusDefs =
        {
            ("✔ Рабочая",     "working", Color.FromArgb(0,   145,   0)),
            ("◎ Проверяется", "review",  Color.FromArgb(190, 105,   0)),
            ("▣ Архив",       "archive", Color.FromArgb(110, 110, 110)),
            ("✎ Черновик",    "draft",   Color.FromArgb(80,  100, 190)),
        };

        private void OnStatusFilterClick(object sender, EventArgs e)
        {
            var m = new ContextMenuStrip();
            var miAll = new ToolStripMenuItem("Все") { Checked = _activeStatuses.Count == 0 };
            miAll.Click += (s2, e2) => { _activeStatuses.Clear(); UpdateStatusBtn(); ApplyFilter(); };
            m.Items.Add(miAll);
            m.Items.Add(new ToolStripSeparator());
            foreach (var (label, key, _) in StatusDefs)
            {
                var k  = key;
                var mi = new ToolStripMenuItem(label) { Checked = _activeStatuses.Contains(k) };
                mi.Click += (s2, e2) =>
                {
                    if (_activeStatuses.Contains(k)) _activeStatuses.Remove(k);
                    else                             _activeStatuses.Add(k);
                    UpdateStatusBtn();
                    ApplyFilter();
                };
                m.Items.Add(mi);
            }
            m.Show(_statusBtn, new Point(0, _statusBtn.Height));
        }

        private void UpdateStatusBtn()
        {
            if (_activeStatuses.Count == 0)
            {
                _statusBtn.Text      = "Статус ▾";
                _statusBtn.BackColor = SystemColors.Control;
                _statusBtn.ForeColor = SystemColors.ControlText;
            }
            else
            {
                _statusBtn.Text      = $"Статус ({_activeStatuses.Count}) ▾";
                _statusBtn.BackColor = Color.FromArgb(200, 220, 255);
                _statusBtn.ForeColor = Color.FromArgb(10, 60, 160);
            }
        }

        // ── Virtual groups ────────────────────────────────────────────────────

        private void RebuildGroupPanel()
        {
            _groupPanel.Controls.Clear();

            var btnAll = MakeGroupBtn("Все", null, _activeGroup == null);
            btnAll.Margin = new Padding(0, 0, 6, 0);
            _groupPanel.Controls.Add(btnAll);

            foreach (var grp in _settings.Groups)
            {
                var g   = grp;
                var btn = MakeGroupBtn(g, g, string.Equals(_activeGroup, g, StringComparison.Ordinal));
                _groupTip.SetToolTip(btn, "Правая кнопка — управление группой");
                btn.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Right)
                        ShowGroupContextMenu((Button)s, g);
                };
                _groupPanel.Controls.Add(btn);
            }

            // "+ Новая" button
            var btnNew = new Button
            {
                Text      = "+ Новая",
                Width     = 70,
                Height    = 24,
                Margin    = new Padding(4, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 8f),
                Cursor    = Cursors.Hand
            };
            btnNew.FlatAppearance.BorderColor = Color.FromArgb(100, 150, 200);
            btnNew.FlatAppearance.BorderSize  = 1;
            btnNew.BackColor = Color.FromArgb(230, 240, 255);
            btnNew.ForeColor = Color.FromArgb(30, 80, 160);
            btnNew.Click += (s, e) => OnCreateGroup();
            _groupPanel.Controls.Add(btnNew);
        }

        private Button MakeGroupBtn(string text, string groupKey, bool active)
        {
            var font = new Font("Segoe UI", 8.5f);
            var btn = new Button
            {
                Text      = text,
                Height    = 24,
                Width     = Math.Max(40, TextRenderer.MeasureText(text, font).Width + 16),
                Margin    = new Padding(1, 0, 1, 0),
                FlatStyle = FlatStyle.Flat,
                Font      = font,
                Tag       = groupKey,
                Cursor    = Cursors.Hand
            };
            ApplyGroupBtnStyle(btn, active);
            btn.Click += (s, e) =>
            {
                _activeGroup = groupKey;
                foreach (Control c in _groupPanel.Controls)
                    if (c is Button b) ApplyGroupBtnStyle(b, string.Equals(b.Tag as string, groupKey, StringComparison.Ordinal));
                // "Все" button has Tag = null
                if (groupKey == null)
                    foreach (Control c in _groupPanel.Controls)
                        if (c is Button b && b.Tag == null) ApplyGroupBtnStyle(b, true);
                ApplyFilter();
            };
            return btn;
        }

        private static void ApplyGroupBtnStyle(Button btn, bool active)
        {
            btn.FlatStyle = FlatStyle.Flat;
            if (active)
            {
                btn.BackColor = Color.FromArgb(200, 220, 255);
                btn.ForeColor = Color.FromArgb(10, 60, 160);
                btn.FlatAppearance.BorderColor = Color.FromArgb(80, 130, 220);
                btn.FlatAppearance.BorderSize  = 1;
            }
            else
            {
                btn.BackColor = SystemColors.Control;
                btn.ForeColor = SystemColors.ControlText;
                btn.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
                btn.FlatAppearance.BorderSize  = 1;
            }
        }

        private void ShowGroupContextMenu(Button btn, string groupName)
        {
            var m = new ContextMenuStrip();
            m.Items.Add($"Открыть все в «{groupName}»",  null, (s, e) => OpenCloseGroup(groupName, true));
            m.Items.Add($"Закрыть все в «{groupName}»",  null, (s, e) => OpenCloseGroup(groupName, false));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add($"Назначить выделенным: «{groupName}»", null, (s, e) => AssignGroup(ActiveEntries(), groupName));
            m.Items.Add("Убрать из группы",                     null, (s, e) => AssignGroup(ActiveEntries(), null));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Переименовать...",                      null, (s, e) => OnRenameGroup(groupName));
            m.Items.Add("Удалить группу",                        null, (s, e) => OnDeleteGroup(groupName));
            m.Show(btn, new Point(0, btn.Height));
        }

        private void OpenCloseGroup(string groupName, bool open)
        {
            var targets = _all.Where(e => string.Equals(e.Group, groupName, StringComparison.Ordinal)).ToList();
            int count = 0;
            foreach (var entry in targets)
            {
                if (open && !entry.IsOpen)
                {
                    if (TryExecute("open", new object[] { entry.PathId }))
                    { entry.IsOpen = true; _openIds.Add(entry.PathId); count++; }
                }
                else if (!open && entry.IsOpen)
                {
                    if (TryExecute("close", new object[] { entry.PathId }))
                    { entry.IsOpen = false; _openIds.Remove(entry.PathId); count++; }
                }
            }
            RefreshRows();
            AddLog(open
                ? $"Открыто в группе «{groupName}»: {count}"
                : $"Закрыто в группе «{groupName}»: {count}");
        }

        private void BuildGroupSubmenu(ToolStripMenuItem mi)
        {
            mi.DropDownItems.Clear();
            foreach (var g in _settings.Groups)
            {
                var grp = g;
                mi.DropDownItems.Add(grp, null, (s, e) => AssignGroup(ActiveEntries(), grp));
            }
            if (_settings.Groups.Count > 0) mi.DropDownItems.Add(new ToolStripSeparator());
            mi.DropDownItems.Add("Новая группа...", null, (s, e) => OnCreateGroup());
            mi.DropDownItems.Add("Без группы",      null, (s, e) => AssignGroup(ActiveEntries(), null));
        }

        private void AssignGroup(List<ModelEntry> entries, string groupName)
        {
            if (entries.Count == 0) return;
            foreach (var e in entries)
            {
                e.Group = groupName;
                if (groupName == null)
                    _settings.ModelGroups.Remove(e.PathId);
                else
                    _settings.ModelGroups[e.PathId] = groupName;
            }
            AutoSaveSettings();
            ApplyFilter();
            AddLog(groupName == null
                ? $"Убрано из группы: {entries.Count}"
                : $"Назначена группа «{groupName}»: {entries.Count}");
        }

        private void OnCreateGroup()
        {
            string name = ShowInputDialog("Новая группа", "Название группы:", "");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (_settings.Groups.Contains(name)) return;
            _settings.Groups.Add(name);
            AutoSaveSettings();
            RebuildGroupPanel();
        }

        private void OnRenameGroup(string oldName)
        {
            string newName = ShowInputDialog("Переименовать группу", "Новое название:", oldName);
            if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == oldName) return;
            newName = newName.Trim();
            int idx = _settings.Groups.IndexOf(oldName);
            if (idx < 0) return;
            _settings.Groups[idx] = newName;
            foreach (var e in _all)
                if (string.Equals(e.Group, oldName, StringComparison.Ordinal))
                    e.Group = newName;
            var keys = new List<string>(_settings.ModelGroups.Keys);
            foreach (var k in keys)
                if (_settings.ModelGroups[k] == oldName) _settings.ModelGroups[k] = newName;
            if (_activeGroup == oldName) _activeGroup = newName;
            AutoSaveSettings();
            RebuildGroupPanel();
            ApplyFilter();
        }

        private void OnDeleteGroup(string groupName)
        {
            _settings.Groups.Remove(groupName);
            var keys = new List<string>(_settings.ModelGroups.Keys);
            foreach (var k in keys)
                if (_settings.ModelGroups[k] == groupName) _settings.ModelGroups.Remove(k);
            foreach (var e in _all)
                if (string.Equals(e.Group, groupName, StringComparison.Ordinal)) e.Group = null;
            if (_activeGroup == groupName) _activeGroup = null;
            AutoSaveSettings();
            RebuildGroupPanel();
            ApplyFilter();
        }

        // ── Move to folder ────────────────────────────────────────────────────

        private void OnMoveToFolder(object sender, EventArgs e)
        {
            SyncCheckedState();
            var entries = ActiveEntries();
            if (entries.Count == 0) return;

            string projDir = GetProjectDirPath();
            var folders    = CollectProjectFolderPaths(projDir);
            foreach (var f in CollectProjectFolderPathsFromPathIds())   // server: disk paths absent
                if (!folders.Contains(f)) folders.Add(f);
            folders.Sort(StringComparer.OrdinalIgnoreCase);

            using (var dlg = new MoveToFolderDialog(entries, projDir, folders))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    foreach (var log in dlg.MoveLog)
                        AddLog(log);
                    LoadModels();
                    if (dlg.MoveLog.Count > 0)
                        _statusLabel.Text = $"Перемещено: {dlg.MoveLog.Count}  |  Не забудьте сохранить проект (Ctrl+S)";
                }
            }
        }

        private void OnCreateProjectFolder()
        {
            string projDir = GetProjectDirPath();

            // Collect existing relative dirs for the location combo
            var existingDirs = new List<string> { "." };
            existingDirs.AddRange(CollectProjectFolderPaths(projDir));

            // Inline dialog: name + location
            var dlg = new Form
            {
                Text            = "Создать папку в проекте",
                Size            = new Size(400, 160),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition   = FormStartPosition.CenterParent,
                MinimizeBox     = false, MaximizeBox = false,
                Icon            = AbrIcon.Create()
            };

            var lblName     = new Label  { Text = "Название:",    AutoSize = true, Location = new Point(10, 16) };
            var txtName     = new TextBox{ Location = new Point(100, 12), Width = 276 };
            var lblLoc      = new Label  { Text = "В папке:",     AutoSize = true, Location = new Point(10, 46) };
            var cmbLoc      = new ComboBox
            {
                Location = new Point(100, 42), Width = 276,
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode   = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };
            foreach (var d in existingDirs) cmbLoc.Items.Add(d);
            cmbLoc.Text = ".";

            var btnOk     = UiTheme.MakeButton("Создать", BtnKind.Primary, 90, 24);
            btnOk.Location = new Point(196, 80); btnOk.DialogResult = DialogResult.OK;
            var btnCancel = UiTheme.MakeButton("Отмена", BtnKind.Ghost, 80, 24);
            btnCancel.Location = new Point(296, 80); btnCancel.DialogResult = DialogResult.Cancel;
            dlg.Controls.AddRange(new Control[] { lblName, txtName, lblLoc, cmbLoc, btnOk, btnCancel });
            dlg.AcceptButton = btnOk; dlg.CancelButton = btnCancel;
            txtName.Select();

            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            string folderName = txtName.Text.Trim();
            string locationRel = cmbLoc.Text.Trim();
            if (string.IsNullOrEmpty(folderName)) return;

            // Create the folder through Robur's project API. This works for ANY storage backend (local
            // disk, UNC, cloud tfs://) and the folder is registered in the project so Robur sees it —
            // unlike a raw Directory.CreateDirectory, which only made a Windows folder Robur ignored.
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(locationRel) && locationRel != ".")
                parts.AddRange(locationRel.Replace('\\', '/')
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
            parts.Add(folderName);
            string folderPath = string.Join("/", parts);

            IProjectModel created;
            try { created = PluginCoreOps.CreateFolder(parts.ToArray()); }
            catch (Exception ex) { MessageDlg.Show($"Ошибка создания папки:\n{ex.Message}"); return; }

            if (created == null)
            {
                MessageDlg.Show($"Не удалось создать папку '{folderName}' в проекте.");
                return;
            }

            LoadModels();
            AddLog($"Папка создана: {folderPath}");
            _statusLabel.Text = $"Папка '{folderName}' добавлена в проект  |  Не забудьте сохранить (Ctrl+S)";
        }

        private bool TryRegisterFolderInProject(string newFolderDisk, string projDirPath, out string error)
        {
            error = null;
            try
            {
                // Get root project model
                IProjectModel rootModel = null;
                foreach (var e in _all)
                {
                    rootModel = e.Model?.Project?.Model;
                    if (rootModel != null) break;
                }
                if (rootModel == null) { error = "Нет корневой модели"; return false; }

                object rootData = rootModel.LockRead();
                var refsEnum = ProjectRefs.Enumerate(rootData);
                if (refsEnum == null) { error = "References недоступен"; return false; }

                // Find an existing folder reference as template for cloning
                object template = null;

                foreach (var r in refsEnum)
                {
                    var uriPropT = r?.GetType().GetProperty("Uri");
                    var uriValT  = uriPropT?.GetValue(r) as URI;
                    if (uriValT == null) continue;
                    string absT = uriValT.IsAbsoluteUri
                        ? uriValT.AsAbsoluteUri
                        : new URI(rootModel.Uri.DirectoryUri, uriValT.AsAbsoluteUri).AsAbsoluteUri;
                    string diskT = MoveToFolderDialog.UriToDiskPath(absT);
                    if (diskT != null && Directory.Exists(diskT))
                    {
                        template = r;
                        break;
                    }
                }
                if (template == null) { error = "Нет шаблонной папки для клонирования"; return false; }

                // Clone via MemberwiseClone (protected → reflection)
                var cloneMethod = template.GetType().GetMethod(
                    "MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
                if (cloneMethod == null) { error = "MemberwiseClone недоступен"; return false; }

                object newRef = cloneMethod.Invoke(template, null);

                // Set URI to new folder
                var uriProp = newRef.GetType().GetProperty("Uri");
                if (uriProp == null || !uriProp.CanWrite) { error = "Свойство Uri не записываемо"; return false; }

                string relDir = MoveToFolderDialog.ComputeRelDir(projDirPath, Path.GetDirectoryName(newFolderDisk));
                string folderNameOnly = Path.GetFileName(newFolderDisk);
                // URI: relative to project root (e.g. "Archive/MyFolder" or just "MyFolder")
                string relPath = string.IsNullOrEmpty(relDir) || relDir == "./" || relDir == "/"
                    ? folderNameOnly
                    : relDir.TrimEnd('/') + "/" + folderNameOnly;
                var newFolderUri = new URI(rootModel.Uri.DirectoryUri, relPath);
                uriProp.SetValue(newRef, newFolderUri);

                // Add to References collection
                var refsRaw  = ProjectRefs.Raw(rootData);
                var refsObj  = refsRaw as System.Collections.IList;
                if (refsObj != null)
                {
                    refsObj.Add(newRef);
                }
                else
                {
                    // Try via reflection Add method
                    var addMethod = refsRaw?.GetType().GetMethod("Add");
                    if (addMethod == null) { error = "References.Add недоступен"; return false; }
                    addMethod.Invoke(refsRaw, new[] { newRef });
                }

                rootModel.Save(forced: true)?.AsyncWaitHandle.WaitOne();
                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private string GetProjectDirPath()
        {
            foreach (var e in _all)
            {
                try
                {
                    // Original behaviour FIRST — never regress local projects that resolved via this.
                    string tgtStr  = e.Model?.Project?.TargetProjectUri?.ToString() ?? "";
                    string tgtPath = MoveToFolderDialog.UriToDiskPath(tgtStr);
                    if (tgtPath != null)
                    {
                        string dir = Path.GetDirectoryName(tgtPath);
                        if (dir != null) return dir;
                    }

                    // Fallback A: the root project model file URI (its folder = project dir). Proven
                    // source used by TryMoveEntry; AsAbsoluteUri yields the local:/// form UriToDiskPath
                    // understands.
                    string rootUri  = e.Model?.Project?.Model?.Uri?.AsAbsoluteUri;
                    string rootPath = MoveToFolderDialog.UriToDiskPath(rootUri ?? "");
                    if (rootPath != null)
                    {
                        string dir = Path.GetDirectoryName(rootPath);
                        if (dir != null) return dir;
                    }

                    // Fallback B: TargetProjectUri as absolute (directory URI).
                    string tgtAbs     = e.Model?.Project?.TargetProjectUri?.AsAbsoluteUri;
                    string tgtAbsPath = MoveToFolderDialog.UriToDiskPath(tgtAbs ?? "");
                    if (tgtAbsPath != null)
                        return tgtAbsPath.TrimEnd('\\', '/');
                }
                catch { }
            }
            return null;
        }

        private List<string> CollectProjectFolderPaths(string projDir)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            if (projDir == null) return result;

            foreach (var e in _all)
            {
                string diskPath = GetDiskPath(e);
                if (diskPath == null) continue;
                string dir = Path.GetDirectoryName(diskPath);
                if (dir == null || !dir.StartsWith(projDir, StringComparison.OrdinalIgnoreCase)) continue;
                string rel = dir.Substring(projDir.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (!string.IsNullOrEmpty(rel) && seen.Add(rel))
                    result.Add(rel);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        // Project-relative folder paths derived from node PathIds (works for server/cloud projects
        // where disk paths are absent). Includes folder nodes themselves and parents of any node.
        private List<string> CollectProjectFolderPathsFromPathIds()
        {
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var e in _all)
            {
                var parts = (e.PathId ?? "").Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (string.Equals(e.ModelType, "folder", StringComparison.OrdinalIgnoreCase) && parts.Length >= 1)
                {
                    string self = string.Join("/", parts);
                    if (seen.Add(self)) result.Add(self);
                }
                if (parts.Length >= 2)
                {
                    string parent = string.Join("/", parts.Take(parts.Length - 1));
                    if (seen.Add(parent)) result.Add(parent);
                }
            }
            return result;
        }

        private string ShowInputDialog(string title, string prompt, string defaultValue)
        {
            var dlg = new Form
            {
                Text            = title,
                Size            = new Size(340, 120),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition   = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false,
                Icon = AbrIcon.Create()
            };
            var lbl = new Label  { Text = prompt,        AutoSize = true, Location = new Point(10, 12) };
            var txt = new TextBox{ Text = defaultValue,  Location = new Point(10, 30), Width = 304 };
            var ok  = UiTheme.MakeButton("OK", BtnKind.Primary, 80, 24);
            ok.Location = new Point(140, 58); ok.DialogResult = DialogResult.OK;
            var cn  = UiTheme.MakeButton("Отмена", BtnKind.Ghost, 80, 24);
            cn.Location = new Point(224, 58); cn.DialogResult = DialogResult.Cancel;
            dlg.Controls.AddRange(new Control[] { lbl, txt, ok, cn });
            dlg.AcceptButton = ok; dlg.CancelButton = cn;
            txt.SelectAll();
            return dlg.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
        }

        // These hold the resolved members for the CURRENTLY-active node type only. Node members are
        // resolved with DeclaredOnly on node.GetType(), so each MethodInfo.DeclaringType is a concrete
        // obfuscated node type. The Robur tree is heterogeneous (root vs folder vs model nodes are
        // different runtime types), so a single set of fields cannot serve all of them — invoking a
        // type-A MethodInfo on a type-B node throws TargetException. SwitchNodeType() multiplexes these
        // fields by node runtime type via _nodeMethodsByType, so detection runs once per type and each
        // node is only ever touched with members declared on its own type.
        private MethodInfo _nodeCollapseMethod;
        private MethodInfo _nodeExpandMethod;
        private MethodInfo _nodeChildrenGetter;
        private MethodInfo _nodeTextGetter;
        // Collection members are resolved on the DECLARED nodeCollType (shared base), so a single set
        // is valid for every assignable collection instance — no per-type multiplexing needed.
        private MethodInfo _collCountMethod;
        private MethodInfo _collItemMethod;

        private Type _activeNodeType;
        private readonly Dictionary<Type, MethodInfo[]> _nodeMethodsByType = new Dictionary<Type, MethodInfo[]>();

        // Multiplex the per-node-type member fields. Persists the current type's resolved members and
        // loads (or zero-initialises) the incoming type's set. Cheap no-op when the type is unchanged.
        private void SwitchNodeType(object node)
        {
            var t = node.GetType();
            if (t == _activeNodeType) return;

            if (_activeNodeType != null)
                _nodeMethodsByType[_activeNodeType] = new[]
                {
                    _nodeTextGetter, _nodeChildrenGetter,
                    _nodeExpandMethod, _nodeCollapseMethod
                };

            if (_nodeMethodsByType.TryGetValue(t, out var m))
            {
                _nodeTextGetter     = m[0];
                _nodeChildrenGetter = m[1];
                _nodeExpandMethod   = m[2];
                _nodeCollapseMethod = m[3];
            }
            else
            {
                _nodeTextGetter = _nodeChildrenGetter =
                    _nodeExpandMethod = _nodeCollapseMethod = null;
            }
            _activeNodeType = t;
        }


        // Collapse all nodes not in keepNames (and not parents of kept nodes).
        // Returns true if this subtree contains a kept node.
        // Call after RefreshTree — assumes nodes are loaded (not lazy).
        // Applies expand/collapse to nodes whose caption matches one of targetNames (selected models).
        // expand=true : matched node opens one level (ExpandNode, no child recursion); ancestors of a
        //               match are opened too so the node is visible. Non-matching leaves untouched.
        // expand=false: matched node collapses its entire subtree (CollapseNode recurses internally).
        // Returns true if a target was found anywhere in this subtree (so the caller keeps the ancestor open).
        private bool ApplyToMatchingNodes(object collection, Type nodeCollType, BindingFlags bf,
                                          HashSet<string> targetNames, bool expand)
        {
            EnsureCollectionMethods(nodeCollType);
            if (_collCountMethod == null || _collItemMethod == null) return false;

            int count    = (int)_collCountMethod.Invoke(collection, null);
            bool anyMatch = false;
            for (int i = 0; i < count; i++)
            {
                var node = _collItemMethod.Invoke(collection, new object[] { i });
                if (node == null) continue;
                SwitchNodeType(node);

                var text      = GetNodeText(node, bf);
                var textNoExt = text != null ? Path.GetFileNameWithoutExtension(text) : null;
                bool isTarget = text != null &&
                    (targetNames.Contains(text) ||
                     (!string.IsNullOrEmpty(textNoExt) && targetNames.Contains(textNoExt)));

                if (isTarget)
                {
                    anyMatch = true;
                    if (expand)
                        ExpandNode(node, nodeCollType, bf);
                    else
                        CollapseNode(node, nodeCollType, bf);
                }
                else
                {
                    // To search deeper we only need this node's children LOADED, not visually expanded —
                    // Robur keeps them cached once loaded. So if they're already present, search in place
                    // with ZERO churn (no expand/collapse, no UI events). Only force a lazy load (expand)
                    // when children are missing, and collapse back only those we had to expand. This kills
                    // the per-node toggle storm on an already-browsed tree.
                    var children   = GetNodeChildren(node, nodeCollType, bf);
                    bool hadChildren = children != null;
                    if (expand && !hadChildren)
                    {
                        ExpandNode(node, nodeCollType, bf);                  // force Robur to load children
                        children = GetNodeChildren(node, nodeCollType, bf); // re-read after load
                    }

                    bool childMatch = children != null &&
                        ApplyToMatchingNodes(children, nodeCollType, bf, targetNames, expand);
                    if (childMatch)
                    {
                        anyMatch = true;
                        if (expand) ExpandNode(node, nodeCollType, bf); // reveal: keep this ancestor open
                    }
                    else if (expand && !hadChildren)
                    {
                        CollapseNode(node, nodeCollType, bf); // we expanded only to load → restore closed
                    }
                }
            }
            return anyMatch;
        }

        private static object GetRootNodeCollection(Control treeview, BindingFlags bf, out Type nodeCollType)
        {
            // Use bfAll (no DeclaredOnly) so inherited Nodes/Count accessors are found too
            var bfAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            nodeCollType = null;
            foreach (var m in treeview.GetType().GetMethods(bfAll))
            {
                if (!m.IsSpecialName || m.GetParameters().Length != 0) continue;
                var rt = m.ReturnType;
                if (rt.IsPrimitive || rt == typeof(void) || rt == typeof(string)) continue;
                var countM = Array.Find(rt.GetMethods(bfAll),
                    x => x.IsSpecialName && x.ReturnType == typeof(int) && x.GetParameters().Length == 0);
                if (countM == null) continue;
                var result = m.Invoke(treeview, null);
                if (result != null) { nodeCollType = rt; return result; }
            }
            return null;
        }

        private void EnsureCollectionMethods(Type nodeCollType)
        {
            // Resolve Count/Item on the DECLARED collection type (nodeCollType), not the runtime
            // type of the first collection. Root Nodes and Node.Children can be different runtime
            // subclasses; a MethodInfo from one subclass throws TargetException when Invoke'd on the
            // other. The declared base type's MethodInfo is valid on every assignable instance.
            if (_collCountMethod != null) return;
            var bfAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _collCountMethod = Array.Find(nodeCollType.GetMethods(bfAll),
                x => x.IsSpecialName && x.ReturnType == typeof(int) && x.GetParameters().Length == 0);
            _collItemMethod = Array.Find(nodeCollType.GetMethods(bfAll),
                x => x.IsSpecialName && x.GetParameters().Length == 1
                     && x.GetParameters()[0].ParameterType == typeof(int));
        }

        private void ExpandNodeCollection(object collection, Type nodeCollType, BindingFlags bf)
        {
            EnsureCollectionMethods(nodeCollType);
            if (_collCountMethod == null || _collItemMethod == null) return;

            int count = (int)_collCountMethod.Invoke(collection, null);
            for (int i = 0; i < count; i++)
            {
                var node = _collItemMethod.Invoke(collection, new object[] { i });
                if (node == null) continue;
                ExpandNode(node, nodeCollType, bf);
                // Read children AFTER expand — lazy nodes load on expand
                var children = GetNodeChildren(node, nodeCollType, bf);
                if (children != null) ExpandNodeCollection(children, nodeCollType, bf);
            }
        }

        private void CollapseNodeCollection(object collection, Type nodeCollType, BindingFlags bf)
        {
            EnsureCollectionMethods(nodeCollType);
            if (_collCountMethod == null || _collItemMethod == null) return;

            int count = (int)_collCountMethod.Invoke(collection, null);
            for (int i = 0; i < count; i++)
            {
                var node = _collItemMethod.Invoke(collection, new object[] { i });
                if (node == null) continue;
                CollapseNode(node, nodeCollType, bf);
            }
        }

        // Idempotent, self-verifying. Collapses the node (and its subtree) if open; no-op if already
        // closed or a leaf. The collapse method is discovered once per node type by observing which void
        // flips IsNodeOpen true→false — so an inverted assignment is impossible by construction.
        private void CollapseNode(object node, Type nodeCollType, BindingFlags bf)
        {
            SwitchNodeType(node);
            EnsureNodeChildrenGetter(node, nodeCollType, bf);
            if (_nodeChildrenGetter == null) return;   // no state to track → leaf, nothing to collapse
            if (!IsNodeOpen(node, bf)) return;         // already closed → idempotent no-op

            // Collapse the subtree first — children are only reachable while this node is still open.
            var children = _nodeChildrenGetter.Invoke(node, null);
            if (children != null) CollapseNodeCollection(children, nodeCollType, bf);

            // Recursion above re-pointed the active-type fields at child types — restore ours.
            SwitchNodeType(node);

            if (_nodeCollapseMethod != null) { _nodeCollapseMethod.Invoke(node, null); return; }

            // Discover: node is open (entry guard guaranteed it). Collapse is the void that drives
            // IsExpanded True→False. If the state getter isn't known yet (whole tree was already
            // expanded), learn it here — safe because the node is genuinely open. A void that instead
            // opened something (a getter went False→True) is wrong; revert that toggle and keep looking.
            var bools  = GetNodeBoolGetters(node, bf);
            var before = Array.ConvertAll(bools, g => SafeBool(g, node));
            foreach (var v in GetNodeVoids(node, bf))
            {
                if (v == _nodeExpandMethod) continue;  // never the expand method
                v.Invoke(node, null);

                int down = -1, up = -1;
                for (int i = 0; i < bools.Length; i++)
                {
                    bool now = SafeBool(bools[i], node);
                    if (before[i] && !now && down < 0) down = i;   // True→False = IsExpanded on collapse
                    if (!before[i] && now && up < 0)   up = i;
                }
                if (down >= 0)
                {
                    SetStateGetter(bools[down].Name, "collapse", node, bf);
                    _nodeCollapseMethod = v;
                    return;   // node now collapsed
                }
                if (up >= 0) v.Invoke(node, null);   // v opened it (node wasn't really open) → revert toggle
                // else pure no-op → keep looking
            }
        }

        private object GetNodeChildren(object node, Type nodeCollType, BindingFlags bf)
        {
            SwitchNodeType(node);
            EnsureNodeChildrenGetter(node, nodeCollType, bf);
            return _nodeChildrenGetter?.Invoke(node, null);
        }

        // The children getter is the single source of truth for open/closed state: it returns the node's
        // child collection (ReturnType == nodeCollType) when open/loaded, and null when closed or lazy.
        // Resolved per type with the strict nodeCollType filter so the object it yields is always valid
        // for the collection Count/Item methods (which are declared on nodeCollType).
        private void EnsureNodeChildrenGetter(object node, Type nodeCollType, BindingFlags bf)
        {
            if (_nodeChildrenGetter == null)
                _nodeChildrenGetter = Array.Find(node.GetType().GetMethods(bf),
                    x => x.IsSpecialName && x.GetParameters().Length == 0 && x.ReturnType == nodeCollType);
        }

        // Name of the IsExpanded bool getter (True == open). The expansion-state getter is the same
        // obfuscated member on every node type (only the declaring type differs), so once we learn its
        // NAME from one reliable transition we can read it on any node by name. This is the only
        // steady-state-reliable signal: the children collection stays cached (non-null) after a collapse,
        // so children-presence detects a fresh expand edge but NOT the collapsed state.
        private string _stateGetterName;

        private MethodInfo StateGetter(object node, BindingFlags bf)
        {
            if (_stateGetterName == null) return null;
            return Array.Find(GetNodeBoolGetters(node, bf), g => g.Name == _stateGetterName);
        }

        // Single funnel for learning the IsExpanded getter name (first writer wins).
        private void SetStateGetter(string name, string via, object node, BindingFlags bf)
        {
            if (_stateGetterName != null || name == null) return;
            _stateGetterName = name;
        }

        private bool ChildrenPresent(object node)
        {
            return _nodeChildrenGetter != null && _nodeChildrenGetter.Invoke(node, null) != null;
        }

        // Reliable once the state getter is known; otherwise falls back to children-presence, which is
        // valid only for genuinely closed nodes whose children are still unloaded (null).
        private bool IsNodeOpen(object node, BindingFlags bf)
        {
            var g = StateGetter(node, bf);
            if (g != null) return SafeBool(g, node);
            return ChildrenPresent(node);
        }

        // Learn _stateGetterName up-front from a reliable children null→non-null edge, by probing a
        // genuinely closed (unloaded) node anywhere in the tree. This makes collapse reliable from the
        // first click: a known getter means we never have to guess on a cached-children node. If the
        // whole tree is already expanded (no unloaded node exists), learning is deferred to CollapseNode,
        // which is safe there because every node it touches is genuinely open.
        private void EnsureStateGetterLearned(object rootColl, Type nodeCollType, BindingFlags bf)
        {
            if (_stateGetterName != null) return;
            if (ProbeForStateGetter(rootColl, nodeCollType, bf, 0)) return;

            // No unloaded node anywhere (tree fully expanded, children all cached) → the children edge
            // isn't available. Calibrate on the first node assuming it is OPEN (true for a normally
            // displayed project root): collapse it, learn IsExpanded from the True→False flip, then
            // re-expand to restore. One node toggled and restored — no visible whole-tree collapse.
            CalibrateAssumingOpen(rootColl, nodeCollType, bf);
        }

        private void CalibrateAssumingOpen(object collection, Type nodeCollType, BindingFlags bf)
        {
            if (_collCountMethod == null || _collItemMethod == null) return;
            int count = (int)_collCountMethod.Invoke(collection, null);
            if (count == 0) return;
            var node = _collItemMethod.Invoke(collection, new object[] { 0 });
            if (node == null) return;
            SwitchNodeType(node);
            EnsureNodeChildrenGetter(node, nodeCollType, bf);
            if (_nodeChildrenGetter == null) return;

            var bools  = GetNodeBoolGetters(node, bf);
            var before = Array.ConvertAll(bools, g => SafeBool(g, node));
            foreach (var v in GetNodeVoids(node, bf))
            {
                v.Invoke(node, null);
                int down = -1;
                for (int i = 0; i < bools.Length; i++)
                    if (before[i] && !SafeBool(bools[i], node)) { down = i; break; }   // True→False = IsExpanded
                if (down >= 0)
                {
                    SetStateGetter(bools[down].Name, "calib", node, bf);
                    _nodeCollapseMethod = v;
                    // Restore: re-open via another void until IsExpanded reads True again.
                    var g = StateGetter(node, bf);
                    foreach (var v2 in GetNodeVoids(node, bf))
                        if (v2 != v) { v2.Invoke(node, null); if (g != null && SafeBool(g, node)) { _nodeExpandMethod = v2; break; } }
                    return;
                }
                // node wasn't open in the direction we assumed — undo this toggle and try the next void
                bool changed = false;
                for (int i = 0; i < bools.Length; i++) if (before[i] != SafeBool(bools[i], node)) { changed = true; break; }
                if (changed) v.Invoke(node, null);
            }
        }

        private bool ProbeForStateGetter(object collection, Type nodeCollType, BindingFlags bf, int depth)
        {
            if (depth > 12 || _collCountMethod == null || _collItemMethod == null) return false;
            int count = (int)_collCountMethod.Invoke(collection, null);
            for (int i = 0; i < count; i++)
            {
                var node = _collItemMethod.Invoke(collection, new object[] { i });
                if (node == null) continue;
                SwitchNodeType(node);
                EnsureNodeChildrenGetter(node, nodeCollType, bf);
                if (_nodeChildrenGetter == null) continue;   // leaf

                if (ChildrenPresent(node))
                {
                    // Already open → descend to look for an unloaded node deeper.
                    var kids = _nodeChildrenGetter.Invoke(node, null);
                    if (kids != null && ProbeForStateGetter(kids, nodeCollType, bf, depth + 1)) return true;
                    SwitchNodeType(node);
                    continue;
                }

                // Closed-unloaded node: invoke voids until children appear; the bool getter that flipped
                // False→True across that edge is IsExpanded. Then close it back to restore original state.
                var bools  = GetNodeBoolGetters(node, bf);
                var before = Array.ConvertAll(bools, g => SafeBool(g, node));
                foreach (var v in GetNodeVoids(node, bf))
                {
                    v.Invoke(node, null);
                    if (ChildrenPresent(node))
                    {
                        for (int k = 0; k < bools.Length; k++)
                            if (!before[k] && SafeBool(bools[k], node)) { SetStateGetter(bools[k].Name, "probe", node, bf); break; }
                        // restore: close it again via the void that drives IsExpanded back to false
                        var g = StateGetter(node, bf);
                        if (g != null)
                            foreach (var v2 in GetNodeVoids(node, bf))
                                if (v2 != v) { v2.Invoke(node, null); if (!SafeBool(g, node)) break; }
                        if (_stateGetterName != null) return true;
                    }
                }
            }
            return false;
        }

        // Candidate parameterless void methods on a node type — expand/collapse live here (obfuscated
        // names like _0086_0087). Cached per runtime type; discovery in Expand/CollapseNode picks which
        // is which by verified post-condition, so we never depend on a fragile direction heuristic.
        private readonly Dictionary<Type, MethodInfo[]> _nodeVoidsByType = new Dictionary<Type, MethodInfo[]>();
        private MethodInfo[] GetNodeVoids(object node, BindingFlags bf)
        {
            var t = node.GetType();
            if (!_nodeVoidsByType.TryGetValue(t, out var v))
            {
                v = Array.FindAll(t.GetMethods(bf),
                    x => !x.IsSpecialName && x.ReturnType == typeof(void) && x.GetParameters().Length == 0);
                _nodeVoidsByType[t] = v;
            }
            return v;
        }

        // Node types for which expand discovery found no opening void (true leaves) — skip to avoid
        // re-spamming every void on them each traversal (a prior cause of event churn / hangs).
        private readonly HashSet<Type> _nonExpandableTypes = new HashSet<Type>();

        // Parameterless bool getters per node type (IsExpanded/IsCollapsed live here). Used as a
        // secondary open/closed signal because the children collection may stay cached (non-null) after
        // a collapse — making children-presence reliable for expand but not for collapse.
        private readonly Dictionary<Type, MethodInfo[]> _nodeBoolGettersByType = new Dictionary<Type, MethodInfo[]>();
        private MethodInfo[] GetNodeBoolGetters(object node, BindingFlags bf)
        {
            var t = node.GetType();
            if (!_nodeBoolGettersByType.TryGetValue(t, out var g))
            {
                g = Array.FindAll(t.GetMethods(bf),
                    x => x.IsSpecialName && x.ReturnType == typeof(bool) && x.GetParameters().Length == 0);
                _nodeBoolGettersByType[t] = g;
            }
            return g;
        }
        private static bool SafeBool(MethodInfo g, object node)
        {
            try { return (bool)g.Invoke(node, null); } catch { return false; }
        }

        private string GetNodeText(object node, BindingFlags bf)
        {
            SwitchNodeType(node);
            if (_nodeTextGetter != null)
                return _nodeTextGetter.Invoke(node, null) as string;

            string best = null;
            foreach (var m in node.GetType().GetMethods(bf))
            {
                if (!m.IsSpecialName || m.GetParameters().Length != 0) continue;
                if (m.ReturnType != typeof(string)) continue;
                var val = m.Invoke(node, null) as string;
                if (string.IsNullOrEmpty(val)) continue;
                if (val.IndexOf('\\') >= 0 || val.IndexOf('/') >= 0 || val.IndexOf(':') >= 0) continue;
                if (best == null || val.Length < best.Length) { best = val; _nodeTextGetter = m; }
            }
            return best;
        }

        // Idempotent, self-verifying. Opens the node if currently closed; no-op if already open or a
        // leaf. The expand method is discovered once per node type: from a CLOSED node, the void that
        // makes children appear IS expand. Because the post-condition (IsNodeOpen) is verified, an
        // inverted assignment is impossible — this replaces the old snapshot-direction detection that
        // repeatedly produced backwards expand/collapse across sessions.
        private void ExpandNode(object node, Type nodeCollType, BindingFlags bf)
        {
            SwitchNodeType(node);
            EnsureNodeChildrenGetter(node, nodeCollType, bf);
            if (_nodeChildrenGetter == null) return;          // no state to track → leaf, nothing to open
            if (IsNodeOpen(node, bf)) return;                 // already open → idempotent no-op

            if (_nodeExpandMethod != null) { _nodeExpandMethod.Invoke(node, null); return; }
            if (_nonExpandableTypes.Contains(node.GetType())) return;

            // Discover: node is closed. The void that opens it is expand. "Opened" = children appear
            // (null→non-null edge) OR, once the state getter is known, IsExpanded turns True. The edge
            // also lets us learn the IsExpanded getter name (the bool flipping False→True).
            var bools  = GetNodeBoolGetters(node, bf);
            var before = Array.ConvertAll(bools, g => SafeBool(g, node));
            foreach (var v in GetNodeVoids(node, bf))
            {
                bool cBefore = ChildrenPresent(node);
                v.Invoke(node, null);
                bool cAfter = ChildrenPresent(node);

                if (_stateGetterName == null && !cBefore && cAfter)
                    for (int i = 0; i < bools.Length; i++)
                        if (!before[i] && SafeBool(bools[i], node)) { SetStateGetter(bools[i].Name, "expand", node, bf); break; }

                var g = StateGetter(node, bf);
                bool opened = (!cBefore && cAfter) || (g != null && SafeBool(g, node));
                if (opened) { _nodeExpandMethod = v; return; }   // leave open
                // didn't open → no-op/unrelated void, try next
            }
            // Nothing opened it → true leaf. Remember so we don't re-spam its voids every traversal.
            _nonExpandableTypes.Add(node.GetType());
        }


        // Wraps a batch tree operation in BeginUpdate/EndUpdate (suppresses per-node repaints).
        // Falls back gracefully if the tree control doesn't expose these methods.
        private static void TreeBatch(Control treeCtrl, Action action)
        {
            var bfAll       = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var beginUpdate = treeCtrl.GetType().GetMethod("BeginUpdate", bfAll, null, Type.EmptyTypes, null);
            var endUpdate   = treeCtrl.GetType().GetMethod("EndUpdate",   bfAll, null, Type.EmptyTypes, null);
            beginUpdate?.Invoke(treeCtrl, null);
            try   { action(); }
            finally
            {
                endUpdate?.Invoke(treeCtrl, null);
                treeCtrl.Invalidate(true);
                treeCtrl.Update();
            }
        }

        private static Control FindControlByName(Control parent, string name)
        {
            foreach (Control c in parent.Controls)
            {
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                    return c;
                var found = FindControlByName(c, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
