using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Abr.Sdk;
using Topomatic.ApplicationPlatform;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;
using Topomatic.Controls.Dialogs;
using Topomatic.FoundationClasses;

namespace ModelDesk
{
    internal class BulkRenameDialog : Form
    {
        private static readonly List<string> _history = new List<string>();
        private static Dictionary<string, string> _templates; // name → pattern, lazy-loaded

        private readonly List<ModelEntry> _entries;
        private readonly List<string> _renameLog = new List<string>();
        private readonly List<(ModelEntry Entry, string OldName)> _undoItems = new List<(ModelEntry, string)>();
        private DataGridView _grid;
        private ComboBox _pattern;
        private int _insertPos = 0;

        public IReadOnlyList<string> RenameLog => _renameLog;
        public IReadOnlyList<(ModelEntry Entry, string OldName)> UndoItems => _undoItems;

        public BulkRenameDialog(List<ModelEntry> entries)
        {
            _entries = entries;

            Text            = "Массовое переименование";
            ClientSize      = new Size(580, 440);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize     = new Size(480, 340);
            StartPosition   = FormStartPosition.CenterParent;
            MinimizeBox     = false;
            MaximizeBox     = false;
            Font            = new Font("Segoe UI", 9f);
            Icon            = AbrIcon.Create();

            BuildUi();
        }

        private void BuildUi()
        {
            // Pattern bar
            var pPattern = new Panel { Dock = DockStyle.Top, Height = 36 };
            var lblPat   = new Label { Text = "Шаблон:", AutoSize = true, Location = new Point(8, 10) };
            _pattern = new ComboBox
            {
                Location  = new Point(66, 7),
                Width     = 350,
                Anchor    = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDown
            };
            foreach (var h in _history) _pattern.Items.Add(h);
            if (_pattern.Items.Count > 0) _pattern.SelectedIndex = 0;
            // Track cursor position while ComboBox has focus (LostFocus resets SelectionStart to 0 — don't use it)
            _pattern.KeyUp   += (s, e) => _insertPos = _pattern.SelectionStart;
            _pattern.MouseUp += (s, e) => _insertPos = _pattern.SelectionStart;

            // "★" — save named template; "▼" — load named template
            var btnSave = UiTheme.MakeButton("★", BtnKind.Ghost, 24, 26);
            btnSave.Location = new Point(420, 5);
            var tipSave = new ToolTip();
            tipSave.SetToolTip(btnSave, "Сохранить как шаблон");
            btnSave.Click += (s, e) => SaveCurrentTemplate();

            var btnLoad = UiTheme.MakeButton("▼", BtnKind.Ghost, 24, 26);
            btnLoad.Location = new Point(446, 5);
            var tipLoad = new ToolTip();
            tipLoad.SetToolTip(btnLoad, "Загрузить шаблон");
            btnLoad.Click += (s, e) => ShowTemplateMenu((Button)s);

            var btnFill = UiTheme.MakeButton("Заполнить ▶", BtnKind.Ghost, 100, 26);
            btnFill.Location = new Point(472, 5);
            btnFill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnFill.Click += OnFill;
            pPattern.Controls.AddRange(new Control[] { lblPat, _pattern, btnSave, btnLoad, btnFill });
            pPattern.Resize += (s, e) =>
            {
                btnFill.Left   = pPattern.Width - 108;
                btnLoad.Left   = pPattern.Width - 134;
                btnSave.Left   = pPattern.Width - 160;
                _pattern.Width = Math.Max(60, pPattern.Width - 230);
            };

            // Grid
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible     = false,
                AutoSizeRowsMode      = DataGridViewAutoSizeRowsMode.None,
                RowTemplate           = { Height = 24 },
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect           = true,
                BorderStyle           = BorderStyle.None,
                BackgroundColor       = SystemColors.Window,
                GridColor             = Color.FromArgb(220, 220, 220),
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight   = 26
            };
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);

            var colCurrent = new DataGridViewTextBoxColumn
            {
                Name = "current", HeaderText = "Текущее имя", ReadOnly = true, Width = 260,
                DefaultCellStyle = { ForeColor = Color.FromArgb(100, 100, 100), BackColor = Color.FromArgb(245, 245, 245) }
            };
            var colNew = new DataGridViewTextBoxColumn
            {
                Name = "new", HeaderText = "Новое имя", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            _grid.Columns.Add(colCurrent);
            _grid.Columns.Add(colNew);

            // Populate grid
            foreach (var entry in _entries)
                _grid.Rows.Add(new object[] { entry.DisplayName, entry.DisplayName });
            for (int i = 0; i < _grid.Rows.Count; i++)
                _grid.Rows[i].Tag = _entries[i];

            // Variable insert buttons
            var pVars = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Color.FromArgb(248, 248, 248) };
            var lblVars = new Label
            {
                Text      = "Вставить:",
                AutoSize  = true,
                Location  = new Point(8, 9),
                ForeColor = Color.FromArgb(90, 90, 90),
                Font      = new Font("Segoe UI", 8.5f)
            };
            pVars.Controls.Add(lblVars);

            // (label, token) — button shows label, tooltip shows token
            var varDefs = new[] {
                ("Имя",    "$NAME"),
                ("Номер",  "$N"),
                ("Тип",    "$TYPE"),
                ("Папка",  "$FOLDER"),
            };
            int vx = 68;
            foreach (var (label, token) in varDefs)
            {
                var t = token; // capture for lambda
                var vb = new Button
                {
                    Text      = label,
                    Width     = 60,
                    Height    = 22,
                    Location  = new Point(vx, 6),
                    FlatStyle = FlatStyle.Flat,
                    Font      = new Font("Segoe UI", 8.5f),
                    BackColor = Color.FromArgb(235, 240, 250),
                    ForeColor = Color.FromArgb(30, 80, 160),
                    Cursor    = Cursors.Hand
                };
                vb.FlatAppearance.BorderColor = Color.FromArgb(180, 200, 230);
                var tt = new ToolTip();
                tt.SetToolTip(vb, token);
                vb.Click += (s, e) => InsertVariable(t);
                pVars.Controls.Add(vb);
                vx += 66;
            }

            // Button bar
            var pBtn = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            var btnApply = UiTheme.MakeButton("Применить", BtnKind.Primary, 100, 30);
            btnApply.Location = new Point(580 - 190, 7);
            btnApply.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var btnCancel = UiTheme.MakeButton("Отмена", BtnKind.Ghost, 80, 30);
            btnCancel.Location = new Point(580 - 88, 7);
            btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCancel.DialogResult = DialogResult.Cancel;
            btnApply.Click += OnApply;
            pBtn.Controls.AddRange(new Control[] { btnApply, btnCancel });
            pBtn.Resize += (s, e) =>
            {
                btnApply.Left  = pBtn.Width - 190;
                btnCancel.Left = pBtn.Width - 88;
            };
            AcceptButton = btnApply;
            CancelButton = btnCancel;

            var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(210, 210, 210) };

            Controls.Add(_grid);
            Controls.Add(pPattern);
            Controls.Add(pBtn);
            Controls.Add(sep);
            Controls.Add(pVars);
        }

        // ── Variable insert ───────────────────────────────────────────────────

        private void InsertVariable(string token)
        {
            string cur = _pattern.Text;
            int pos = Math.Min(_insertPos, cur.Length);
            _pattern.Text = cur.Substring(0, pos) + token + cur.Substring(pos);
            _insertPos = pos + token.Length;
            _pattern.Focus();
            _pattern.SelectionStart  = _insertPos;
            _pattern.SelectionLength = 0;
        }

        // ── Fill pattern ──────────────────────────────────────────────────────

        private void OnFill(object sender, EventArgs e)
        {
            string pat = _pattern.Text.Trim();
            if (string.IsNullOrEmpty(pat)) return;

            int digits = Math.Max(2, _entries.Count.ToString().Length);

            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                var entry = _grid.Rows[i].Tag as ModelEntry;
                if (entry == null) continue;
                _grid.Rows[i].Cells["new"].Value = ApplyPattern(pat, i + 1, digits, entry);
            }
        }

        private string ApplyPattern(string pat, int n, int digits, ModelEntry entry)
        {
            // PathId format: ":Модели/Автодороги/R/R.roadx" — not a Windows path, split directly
            var parts = (entry.PathId ?? "").Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            // parent folder = second-to-last segment (e.g. "R" in ".../R/R.roadx")
            string folder = parts.Length >= 2 ? parts[parts.Length - 2] : "";

            // Replace longer tokens first so $N doesn't match inside $NAME/$FOLDER
            return pat
                .Replace("$NAME",   entry.DisplayName)
                .Replace("$FOLDER", folder)
                .Replace("$TYPE",   entry.ModelType)
                .Replace("$N",      n.ToString("D" + digits));
        }

        // ── Apply rename ──────────────────────────────────────────────────────

        private void OnApply(object sender, EventArgs e)
        {
            _grid.EndEdit();

            var errors = new List<string>();
            _renameLog.Clear();
            _undoItems.Clear();
            int renamed = 0;

            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                var entry   = _grid.Rows[i].Tag as ModelEntry;
                var rawNew  = (_grid.Rows[i].Cells["new"].Value as string) ?? string.Empty;
                var newName = Path.GetFileNameWithoutExtension(rawNew.Trim());

                if (entry == null || string.IsNullOrEmpty(newName) || newName == entry.DisplayName)
                    continue;

                string oldName = entry.DisplayName;
                if (TryRenameEntry(entry, newName, out string renameError))
                {
                    _renameLog.Add($"{oldName} → {newName}");
                    _undoItems.Add((entry, oldName));
                    renamed++;
                }
                else
                {
                    errors.Add(renameError);
                }
            }

            // Save pattern to history
            string pat = _pattern.Text.Trim();
            if (!string.IsNullOrEmpty(pat) && !_history.Contains(pat))
            {
                _history.Insert(0, pat);
                if (_history.Count > 5) _history.RemoveAt(5);
            }

            if (errors.Count > 0)
            {
                MessageDlg.Show($"Ошибки ({errors.Count}):\n" + string.Join("\n", errors));
                if (renamed > 0) { DialogResult = DialogResult.OK; Close(); }
                return;
            }

            if (renamed == 0)
            {
                MessageDlg.Show("Нет изменений для применения.");
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        internal static bool TryRenameEntry(ModelEntry entry, string newName, out string error)
        {
            error = null;
            try
            {
                string absUri = entry.Model.Uri.AsAbsoluteUri;
                // Local/UNC files have a real disk path → rename physically (works today). Cloud/network
                // schemes (tfs://, …) have no disk path → delegate to Robur's storage-agnostic project
                // command, the same path its project tree uses (it handles the cloud relocation itself).
                bool isLocal = absUri.StartsWith("local:///", StringComparison.OrdinalIgnoreCase)
                            || absUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
                if (!isLocal)
                    return TryRenameViaCommand(entry, newName, out error);

                string oldLocalPath = absUri.Substring("local:///".Length)
                                            .Replace('/', Path.DirectorySeparatorChar);

                bool isDir = Directory.Exists(oldLocalPath);
                if (!isDir && !File.Exists(oldLocalPath))
                {
                    string parentDir = Path.GetDirectoryName(oldLocalPath);
                    string baseName  = Path.GetFileName(oldLocalPath);
                    if (parentDir != null && Directory.Exists(parentDir))
                    {
                        var candidates = Directory.GetFiles(parentDir, baseName + ".*");
                        if (candidates.Length == 1)
                            oldLocalPath = candidates[0];
                        else if (candidates.Length > 1)
                        {
                            error = $"[{entry.DisplayName}] Неоднозначный файл: {baseName}.*";
                            return false;
                        }
                        else
                        {
                            error = $"[{entry.DisplayName}] Файл не найден: {oldLocalPath}";
                            return false;
                        }
                    }
                    else
                    {
                        error = $"[{entry.DisplayName}] Файл не найден: {oldLocalPath}";
                        return false;
                    }
                }

                string diskExt = isDir ? string.Empty : Path.GetExtension(oldLocalPath);
                string uriExt  = entry.Model.Uri.Extension ?? string.Empty;
                if (uriExt.Length > 0 && !uriExt.StartsWith(".", StringComparison.Ordinal))
                    uriExt = "." + uriExt;

                string newLocalPath = Path.Combine(Path.GetDirectoryName(oldLocalPath), newName + diskExt);
                var    newUri       = new URI(entry.Model.Uri.DirectoryUri, newName + uriExt);

                if (isDir ? Directory.Exists(newLocalPath) : File.Exists(newLocalPath))
                {
                    error = $"[{entry.DisplayName}] Уже существует: {newName + diskExt}";
                    return false;
                }

                var project  = entry.Model.Project;
                bool wasOpen = project.IsModelOpened(entry.Model);
                if (wasOpen) project.CloseModel(entry.Model);

                if (isDir) Directory.Move(oldLocalPath, newLocalPath);
                else       File.Move(oldLocalPath, newLocalPath);

                var rootModel = project.Model;
                object rootData  = rootModel?.LockRead();
                var    refs      = ProjectRefs.Enumerate(rootData);
                string oldAbsUri = entry.Model.Uri.AsAbsoluteUri;
                if (refs != null)
                {
                    foreach (var reference in refs)
                    {
                        var refUri = ProjectRefs.GetUri(reference);
                        if (refUri == null) continue;
                        URI refAbsolute = refUri.IsAbsoluteUri
                            ? refUri
                            : new URI(rootModel.Uri.DirectoryUri, refUri.AsAbsoluteUri);
                        if (string.Equals(refAbsolute.AsAbsoluteUri, oldAbsUri,
                                          StringComparison.OrdinalIgnoreCase))
                        {
                            ProjectRefs.SetUri(reference, new URI(
                                entry.Model.Uri.DirectoryUri.GetRelativeUri(rootModel.Uri.DirectoryUri),
                                newName + uriExt));
                            break;
                        }
                    }
                    rootModel.Save(forced: true)?.AsyncWaitHandle.WaitOne();
                }

                var uriProp = entry.Model.GetType().GetProperty("Uri");
                if (uriProp != null && uriProp.CanWrite)
                    uriProp.SetValue(entry.Model, newUri);

                if (wasOpen) project.OpenModel(entry.Model);

                entry.DisplayName = newName;
                entry.PathId      = PluginCoreOps.FindModelPathId(entry.Model) ?? entry.PathId;
                return true;
            }
            catch (Exception ex)
            {
                error = $"[{entry.DisplayName}] {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        // Cloud/network rename via Robur's own storage-agnostic project-node rename. "mvitem" is —
        // despite its name — the project tree's "Переименовать…" action (core.plugin: id_mvitem →
        // cmd "mvitem \"%0\"", title "Переименовать..."). It relocates the server/cloud blob and fixes
        // references internally. The model must be unlocked first (editor permission; mvitem is flagged
        // $(editor)). It is interactive: passing the new name lets it rename silently when the command
        // consumes the argument, otherwise Robur prompts. ("rename" without args = CAD object rename —
        // a different command, not used here.)
        private static bool TryRenameViaCommand(ModelEntry entry, string newName, out string error)
        {
            error = null;
            try
            {
                if (!PluginCoreOps.HasPermission(entry.Model, PluginCoreOps.EDITOR_PERMISSION))
                {
                    error = $"[{entry.DisplayName}] Модель заблокирована на сервере. " +
                            "Сначала «Разблокировать» (ПКМ → Разблокировать).";
                    return false;
                }

                var    project   = entry.Model.Project;
                var    folderUri = entry.Model.Uri.DirectoryUri;
                string oldAbsUri = entry.Model.Uri.AsAbsoluteUri;
                string uriExt    = entry.Model.Uri.Extension ?? string.Empty;
                if (uriExt.Length > 0 && !uriExt.StartsWith(".", StringComparison.Ordinal))
                    uriExt = "." + uriExt;

                ApplicationHost.Current.Plugins.Execute("mvitem", new object[] { entry.PathId, newName });

                // Confirm by the expected new URI; fall back to "old blob gone" = a rename did happen
                // (the dispatcher reloads the whole list afterwards, so exact tracking isn't critical).
                var renamed = PluginCoreOps.FindModelFromFullPath(project, new URI(folderUri, newName + uriExt));
                if (renamed != null)
                {
                    entry.Model       = renamed;
                    entry.DisplayName = newName;
                    entry.PathId      = PluginCoreOps.FindModelPathId(renamed) ?? entry.PathId;
                    return true;
                }
                if (!FileManager.Accessable(oldAbsUri))
                {
                    entry.DisplayName = newName;
                    return true;
                }

                error = $"[{entry.DisplayName}] Переименование не выполнено (отменено в Robur или модель занята).";
                return false;
            }
            catch (Exception ex)
            {
                error = $"[{entry.DisplayName}] {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        // ── Named templates ───────────────────────────────────────────────────

        private static string TemplatesFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "Topomatic", "ModelDesk", "rename_templates.json");

        private static Dictionary<string, string> GetTemplates()
        {
            if (_templates != null) return _templates;
            _templates = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                string fp = TemplatesFilePath;
                if (!File.Exists(fp)) return _templates;
                string json = File.ReadAllText(fp, Encoding.UTF8);
                var matches = Regex.Matches(json, @"""((?:[^""\\]|\\.)*)"":\s*""((?:[^""\\]|\\.)*)""");
                foreach (Match m in matches)
                {
                    string k = m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
                    string v = m.Groups[2].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
                    _templates[k] = v;
                }
            }
            catch { }
            return _templates;
        }

        private static void PersistTemplates()
        {
            try
            {
                string dir = Path.GetDirectoryName(TemplatesFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                var parts = _templates.Select(kv => "  " + q(kv.Key) + ": " + q(kv.Value));
                File.WriteAllText(TemplatesFilePath, "{\n" + string.Join(",\n", parts) + "\n}", Encoding.UTF8);
            }
            catch { }
        }

        private void SaveCurrentTemplate()
        {
            string pat = _pattern.Text.Trim();
            if (string.IsNullOrEmpty(pat)) return;

            // Inline input dialog — no external dependency
            var form = new Form
            {
                Text = "Сохранить шаблон", Size = new Size(320, 120),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition   = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, Icon = AbrIcon.Create()
            };
            var lbl = new Label { Text = "Название:", Location = new Point(10, 12), AutoSize = true };
            var txt = new TextBox { Location = new Point(10, 32), Width = 284, Text = pat };
            var ok  = UiTheme.MakeButton("OK", BtnKind.Primary, 80, 24);
            ok.Location = new Point(130, 60); ok.DialogResult = DialogResult.OK;
            var cn  = UiTheme.MakeButton("Отмена", BtnKind.Ghost, 80, 24);
            cn.Location = new Point(214, 60); cn.DialogResult = DialogResult.Cancel;
            form.Controls.AddRange(new Control[] { lbl, txt, ok, cn });
            form.AcceptButton = ok; form.CancelButton = cn;
            if (form.ShowDialog(this) != DialogResult.OK) return;
            string name = txt.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;
            GetTemplates()[name] = pat;
            PersistTemplates();
        }

        private void ShowTemplateMenu(Button btn)
        {
            var templates = GetTemplates();
            var menu = new ContextMenuStrip();

            if (templates.Count > 0)
            {
                menu.Items.Add(new ToolStripLabel("— Мои шаблоны —") { ForeColor = Color.Gray });
                foreach (var kv in templates)
                {
                    var name = kv.Key;
                    var pat  = kv.Value;
                    var mi   = new ToolStripMenuItem($"{name}  ({pat})");
                    mi.Click += (s, e) => { _pattern.Text = pat; _insertPos = pat.Length; };
                    menu.Items.Add(mi);
                }
                // "Удалить шаблон..." submenu
                var delMenu = new ToolStripMenuItem("Удалить шаблон...");
                foreach (var kv in templates)
                {
                    var name = kv.Key;
                    var di = new ToolStripMenuItem(name);
                    di.Click += (s, e) => { GetTemplates().Remove(name); PersistTemplates(); };
                    delMenu.DropDownItems.Add(di);
                }
                menu.Items.Add(delMenu);
                menu.Items.Add(new ToolStripSeparator());
            }

            if (_history.Count > 0)
            {
                menu.Items.Add(new ToolStripLabel("— История —") { ForeColor = Color.Gray });
                foreach (var h in _history)
                {
                    var pat = h;
                    var mi  = new ToolStripMenuItem(pat);
                    mi.Click += (s, e) => { _pattern.Text = pat; _insertPos = pat.Length; };
                    menu.Items.Add(mi);
                }
            }

            if (menu.Items.Count == 0)
                menu.Items.Add(new ToolStripLabel("Нет сохранённых шаблонов") { ForeColor = Color.Gray });

            menu.Show(btn, new Point(0, btn.Height));
        }
    }
}
