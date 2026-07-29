using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Abr.Sdk;
using Topomatic.Controls.Dialogs;

namespace ModelDesk
{
    // Вкладка «Ведомости»: зеркало родного меню Robur «Проект → Создать ведомость»
    // для активной модели. Земляные работы вынесены в первую группу.
    internal sealed class SheetsTabControl : UserControl
    {
        private readonly string _pathId;

        private TextBox  _search;
        private ListView _list;
        private Button   _btnRun;
        private Label    _note;

        private List<SheetNode> _all = new List<SheetNode>();

        internal SheetsTabControl(string pathId)
        {
            _pathId   = pathId;
            BackColor = Color.White;
            Dock      = DockStyle.Fill;

            var top = new Panel { Dock = DockStyle.Top, Height = 38 };

            _search = new TextBox { Location = new Point(8, 8), Width = 260 };
            _search.TextChanged += (s, e) => ApplyFilter();
            top.Controls.Add(_search);

            var lblSearch = new Label
            {
                Text      = "Поиск:",
                Location  = new Point(274, 11),
                AutoSize  = true,
                ForeColor = Color.FromArgb(110, 110, 110)
            };
            top.Controls.Add(lblSearch);

            var btnReload = UiTheme.MakeButton("Обновить", BtnKind.Ghost, 90, 26);
            btnReload.Location = new Point(330, 6);
            btnReload.Click += (s, e) => Reload();
            top.Controls.Add(btnReload);

            _list = new ListView
            {
                Dock          = DockStyle.Fill,
                View          = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                ShowGroups    = true
            };
            _list.Columns.Add("Ведомость", 380);
            _list.Columns.Add("Раздел", 170);
            _list.Columns.Add("Описание", 300);
            _list.DoubleClick        += (s, e) => RunSelected();
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };

            _btnRun = UiTheme.MakeButton("Создать ведомость", BtnKind.Primary, 160, 30);
            _btnRun.Location = new Point(8, 7);
            _btnRun.Enabled  = false;
            _btnRun.Click   += (s, e) => RunSelected();
            bottom.Controls.Add(_btnRun);

            _note = new Label
            {
                Location  = new Point(180, 13),
                AutoSize  = true,
                ForeColor = Color.FromArgb(110, 110, 110),
                Text      = string.Empty
            };
            bottom.Controls.Add(_note);

            Controls.Add(_list);
            Controls.Add(bottom);
            Controls.Add(top);

            Reload();
        }

        // Перестроить каталог: активировать свою модель, спросить у Robur меню.
        internal void Reload()
        {
            _list.Items.Clear();
            _list.Groups.Clear();
            _all = new List<SheetNode>();

            SheetCatalogRobur.Activate(_pathId);

            string error;
            var root = SheetCatalogRobur.BuildCatalog(out error);
            if (root == null)
            {
                _note.Text = error ?? "Каталог ведомостей недоступен.";
                UpdateButtons();
                return;
            }

            _all = SheetTree.Flatten(root);
            _note.Text = $"Доступно ведомостей: {_all.Count}";
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var shown = SheetTree.Filter(_all, _search.Text);

            List<SheetNode> earth, others;
            SheetTree.SplitEarthworks(shown, out earth, out others);

            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();

            var gEarth  = new ListViewGroup("Земляные работы");
            var gOthers = new ListViewGroup("Прочие ведомости");
            _list.Groups.Add(gEarth);
            _list.Groups.Add(gOthers);

            AddRows(earth,  gEarth);
            AddRows(others, gOthers);
            _list.EndUpdate();

            UpdateButtons();
        }

        private void AddRows(List<SheetNode> nodes, ListViewGroup group)
        {
            foreach (var n in nodes)
            {
                var item = new ListViewItem(SheetTree.Normalize(n.Text)) { Group = group, Tag = n };
                item.SubItems.Add(n.Path ?? string.Empty);
                item.SubItems.Add(n.Hint ?? string.Empty);
                if (!n.Enabled) item.ForeColor = Color.FromArgb(150, 150, 150);
                _list.Items.Add(item);
            }
        }

        private SheetNode Selected()
        {
            return _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as SheetNode : null;
        }

        private void UpdateButtons()
        {
            var n = Selected();
            _btnRun.Enabled = n != null && n.Enabled;
        }

        private void RunSelected()
        {
            var n = Selected();
            if (n == null) return;
            if (!n.Enabled)
            {
                MessageDlg.Show("Эта ведомость недоступна для активной модели.");
                return;
            }

            string error;
            if (!SheetCatalogRobur.Run(n, out error))
                MessageDlg.Show("Не удалось создать ведомость: " + error);
        }
    }
}
