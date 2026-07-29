using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Abr.Sdk;

namespace ModelDesk
{
    // Карточка модели произвольного типа: базовая сводка + ведомости.
    // Для дорожных моделей используется RoadObjectForm (там своя, богатая «Сводка»).
    internal sealed class ModelCardForm : Form
    {
        internal enum Tab { Summary, Sheets }

        private readonly TabControl _tabs;

        internal ModelCardForm(string title, string pathId, IList<KeyValuePair<string, string>> props)
        {
            Text            = "Карточка модели - " + title;
            Icon            = AbrIcon.Create();
            ClientSize      = new Size(880, 560);
            MinimumSize     = new Size(700, 440);
            StartPosition   = FormStartPosition.CenterParent;
            Font            = new Font("Segoe UI", 9f);

            _tabs = new TabControl { Dock = DockStyle.Fill };

            var pageSummary = new TabPage("Сводка") { Name = "summary", BackColor = Color.White };
            pageSummary.Controls.Add(BuildSummary(props));
            _tabs.TabPages.Add(pageSummary);

            var pageSheets = new TabPage("Ведомости") { Name = "sheets", BackColor = Color.White };
            pageSheets.Controls.Add(new SheetsTabControl(pathId));
            _tabs.TabPages.Add(pageSheets);

            Controls.Add(_tabs);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            var close = UiTheme.MakeButton("Закрыть", BtnKind.Ghost, 110, 30);
            close.Anchor   = AnchorStyles.Right | AnchorStyles.Top;
            close.Location = new Point(ClientSize.Width - 122, 9);
            close.Click   += (s, e) => Close();
            bottom.Controls.Add(close);
            Controls.Add(bottom);
        }

        internal void SelectTab(Tab tab)
        {
            string name = tab == Tab.Summary ? "summary" : "sheets";
            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                if (string.Equals(_tabs.TabPages[i].Name, name, StringComparison.Ordinal))
                {
                    _tabs.SelectedIndex = i;
                    return;
                }
            }
        }

        private static Control BuildSummary(IList<KeyValuePair<string, string>> props)
        {
            var grid = new ListView
            {
                Dock          = DockStyle.Fill,
                View          = View.Details,
                FullRowSelect = true,
                GridLines     = false,
                HeaderStyle   = ColumnHeaderStyle.Nonclickable
            };
            grid.Columns.Add("Параметр", 220);
            grid.Columns.Add("Значение", 560);

            if (props != null)
            {
                foreach (var p in props)
                {
                    var item = new ListViewItem(p.Key);
                    item.SubItems.Add(p.Value ?? string.Empty);
                    grid.Items.Add(item);
                }
            }
            return grid;
        }
    }
}
