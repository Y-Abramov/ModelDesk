using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Abr.Sdk;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;
using Topomatic.Controls.Dialogs;
using Topomatic.FoundationClasses;

namespace ModelDesk
{
    internal class MoveToFolderDialog : Form
    {
        private readonly List<ModelEntry> _entries;
        private readonly string _projectDirPath;

        private ComboBox _folderCombo;
        private DataGridView _grid;

        private readonly List<string> _moveLog = new List<string>();
        public IReadOnlyList<string> MoveLog => _moveLog;

        public MoveToFolderDialog(List<ModelEntry> entries, string projectDirPath, IEnumerable<string> existingFolderRelPaths)
        {
            _entries         = entries;
            _projectDirPath  = projectDirPath;

            Text            = "Переместить в папку";
            ClientSize      = new Size(600, 420);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize     = new Size(480, 320);
            StartPosition   = FormStartPosition.CenterParent;
            MinimizeBox     = false;
            MaximizeBox     = false;
            Font            = new Font("Segoe UI", 9f);
            Icon            = AbrIcon.Create();

            BuildUi(existingFolderRelPaths);
        }

        // ── UI ────────────────────────────────────────────────────────────────

        private void BuildUi(IEnumerable<string> suggestions)
        {
            // Top bar: target folder input
            var pTop = new Panel { Dock = DockStyle.Top, Height = 60 };

            var lblFolder = new Label { Text = "Целевая папка:", AutoSize = true, Location = new Point(8, 12) };

            _folderCombo = new ComboBox
            {
                Location         = new Point(118, 8),
                DropDownStyle    = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                Anchor           = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            foreach (var s in suggestions) _folderCombo.Items.Add(s);
            _folderCombo.TextChanged += (s, e) => UpdatePreview();

            var btnBrowse = UiTheme.MakeButton("…", BtnKind.Ghost, 28, 24);
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowse.Click += OnBrowse;

            var lblHint = new Label
            {
                Text      = "Путь относительно папки проекта. Несуществующие подпапки создаются автоматически.",
                Location  = new Point(8, 36),
                AutoSize  = true,
                ForeColor = Color.FromArgb(100, 100, 100),
                Font      = new Font("Segoe UI", 8f)
            };

            pTop.Controls.AddRange(new Control[] { lblFolder, _folderCombo, btnBrowse, lblHint });
            pTop.Resize += (s, e) =>
            {
                btnBrowse.Left    = pTop.Width - 34;
                btnBrowse.Top     = 8;
                _folderCombo.Width = pTop.Width - 118 - 38;
            };

            // Preview grid
            _grid = new DataGridView
            {
                Dock          = DockStyle.Fill,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly              = true,
                RowHeadersVisible     = false,
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

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "name", HeaderText = "Файл", Width = 220, ReadOnly = true,
                DefaultCellStyle = { BackColor = Color.FromArgb(245, 245, 245) }
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "from", HeaderText = "Из папки", Width = 160, ReadOnly = true,
                DefaultCellStyle = { ForeColor = Color.FromArgb(100, 100, 100) }
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "to", HeaderText = "В папку", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true
            });

            // Button bar
            var pBtn = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            var btnMove = UiTheme.MakeButton("Переместить", BtnKind.Primary, 110, 30);
            btnMove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var btnCancel = UiTheme.MakeButton("Отмена", BtnKind.Ghost, 80, 30);
            btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCancel.DialogResult = DialogResult.Cancel;
            btnMove.Click += OnApply;
            pBtn.Controls.AddRange(new Control[] { btnMove, btnCancel });
            pBtn.Resize += (s, e) =>
            {
                btnMove.Left   = pBtn.Width - 198;
                btnMove.Top    = 7;
                btnCancel.Left = pBtn.Width - 88;
                btnCancel.Top  = 7;
            };
            AcceptButton = btnMove;
            CancelButton = btnCancel;

            var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(210, 210, 210) };

            Controls.Add(_grid);
            Controls.Add(pTop);
            Controls.Add(sep);
            Controls.Add(pBtn);

            UpdatePreview();
        }

        // ── Handlers ──────────────────────────────────────────────────────────

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description  = "Выберите целевую папку";
                dlg.SelectedPath = _projectDirPath ?? "";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string sel = dlg.SelectedPath;
                if (_projectDirPath != null && sel.StartsWith(_projectDirPath, StringComparison.OrdinalIgnoreCase))
                {
                    string rel = sel.Substring(_projectDirPath.Length).TrimStart('\\', '/');
                    _folderCombo.Text = rel.Replace('\\', '/');
                }
                else
                {
                    _folderCombo.Text = sel; // absolute — outside project
                }
            }
        }

        private void UpdatePreview()
        {
            string targetDiskDir = ResolveTargetDir(_folderCombo.Text.Trim());
            _grid.Rows.Clear();

            foreach (var entry in _entries)
            {
                if (IsServerEntry(entry))
                {
                    string sFrom = ParentRel(entry.PathId);
                    string sTo   = _folderCombo.Text.Trim().Replace('\\', '/').Trim('/');
                    bool   sSame = string.Equals(sFrom, sTo, StringComparison.OrdinalIgnoreCase);
                    int    sIdx  = _grid.Rows.Add(entry.DisplayName,
                                                  sFrom.Length == 0 ? "." : sFrom,
                                                  sSame ? "(та же папка)" : (sTo.Length == 0 ? "." : sTo));
                    _grid.Rows[sIdx].Tag = entry;
                    if (sSame) _grid.Rows[sIdx].DefaultCellStyle.ForeColor = Color.FromArgb(160, 160, 160);
                    else       _grid.Rows[sIdx].Cells["to"].Style.ForeColor = Color.FromArgb(0, 110, 0);
                    continue;
                }

                string diskPath = GetEntryDiskPath(entry);
                string fromDisk = diskPath != null ? Path.GetDirectoryName(diskPath) : null;
                string fromRel  = RelToProject(fromDisk);
                string toRel    = targetDiskDir != null ? RelToProject(targetDiskDir) : _folderCombo.Text.Trim();
                string filename  = diskPath != null ? Path.GetFileName(diskPath) : entry.DisplayName;

                bool same = targetDiskDir != null && fromDisk != null &&
                            string.Equals(fromDisk, targetDiskDir, StringComparison.OrdinalIgnoreCase);

                int idx = _grid.Rows.Add(filename, fromRel, same ? "(та же папка)" : toRel);
                _grid.Rows[idx].Tag = entry;
                if (same || diskPath == null)
                    _grid.Rows[idx].DefaultCellStyle.ForeColor = Color.FromArgb(160, 160, 160);
                else
                    _grid.Rows[idx].Cells["to"].Style.ForeColor = Color.FromArgb(0, 110, 0);
            }
        }

        private void OnApply(object sender, EventArgs e)
        {
            string input = _folderCombo.Text.Trim();
            if (string.IsNullOrEmpty(input))
            {
                MessageDlg.Show("Укажите целевую папку.");
                return;
            }

            string targetRel = input.Replace('\\', '/').Trim('/'); // project-relative target folder

            var serverEntries = _entries.Where(IsServerEntry).ToList();
            var localEntries  = _entries.Where(en => !IsServerEntry(en)).ToList();

            var errors = new List<string>();
            int moved  = 0;

            // Server / cloud models: upload blob to new URI + repoint reference (non-destructive — the
            // old blob is never deleted).
            foreach (var entry in serverEntries)
            {
                string from = ParentRel(entry.PathId);
                if (TryMoveEntryServer(entry, targetRel, out string serr))
                {
                    if (!string.Equals(from, targetRel, StringComparison.OrdinalIgnoreCase))
                    {
                        _moveLog.Add($"{entry.DisplayName}: {(from.Length == 0 ? "." : from)} → {(targetRel.Length == 0 ? "." : targetRel)}");
                        moved++;
                    }
                }
                else errors.Add(serr);
            }

            // Local / UNC models: physical File.Move (existing behaviour).
            if (localEntries.Count > 0)
            {
                string targetDiskDir = ResolveTargetDir(input);
                if (targetDiskDir == null)
                {
                    var probe = localEntries.FirstOrDefault();
                    string sch  = probe?.Model?.Uri?.Scheme ?? "(null)";
                    string proj = probe?.Model?.Project?.Model?.Uri?.AsAbsoluteUri ?? "(null)";
                    errors.Add($"Локальные модели: не удалось определить путь к целевой папке. " +
                               $"Ввод='{input}', projDir='{_projectDirPath ?? "(null)"}', схема='{sch}', проект='{proj}'.");
                }
                else
                {
                    bool dirOk = true;
                    try { if (!Directory.Exists(targetDiskDir)) Directory.CreateDirectory(targetDiskDir); }
                    catch (Exception ex)
                    {
                        MessageDlg.Show($"Не удалось создать папку:\n{targetDiskDir}\n{ex.Message}");
                        dirOk = false;
                    }

                    if (dirOk)
                    {
                        foreach (var entry in localEntries)
                        {
                            string diskPath = GetEntryDiskPath(entry);
                            if (diskPath == null)
                            {
                                errors.Add($"[{entry.DisplayName}] Нет пути на диске (возможно, сетевой файл)");
                                continue;
                            }

                            string fromDir = Path.GetDirectoryName(diskPath);
                            if (string.Equals(fromDir, targetDiskDir, StringComparison.OrdinalIgnoreCase))
                                continue; // уже в нужной папке

                            if (TryMoveEntry(entry, targetDiskDir, _projectDirPath, out string error))
                            {
                                _moveLog.Add($"{entry.DisplayName}: {RelToProject(fromDir)} → {RelToProject(targetDiskDir)}");
                                moved++;
                            }
                            else
                            {
                                errors.Add(error);
                            }
                        }
                    }
                }
            }

            if (errors.Count > 0)
            {
                MessageDlg.Show($"Ошибки ({errors.Count}):\n" + string.Join("\n", errors));
                if (moved > 0) { DialogResult = DialogResult.OK; Close(); }
                return;
            }

            if (moved == 0)
            {
                MessageDlg.Show("Нет файлов для перемещения — все уже находятся в целевой папке.");
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string ResolveTargetDir(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            // Disk-path resolution is only meaningful for local projects. Server/cloud inputs (project
            // paths, possibly with characters Path.GetFullPath rejects) must not throw — they're handled
            // as project-relative paths by the server move, so just return null here.
            try
            {
                if (Path.IsPathRooted(input)) return Path.GetFullPath(input);
                // Folder suggestions come from Topomatic PathIds that carry a leading scope marker
                // (":Тест/1"). ":" is an invalid Windows path char → Path.Combine throws → null. Strip it.
                string rel = StripScope(input);
                if (string.IsNullOrEmpty(rel) || _projectDirPath == null) return null;
                return Path.GetFullPath(Path.Combine(_projectDirPath, rel.Replace('/', '\\')));
            }
            catch { return null; }
        }

        private string RelToProject(string diskPath)
        {
            if (_projectDirPath == null || diskPath == null) return diskPath;
            if (diskPath.StartsWith(_projectDirPath, StringComparison.OrdinalIgnoreCase))
            {
                string rel = diskPath.Substring(_projectDirPath.Length).TrimStart('\\', '/');
                return string.IsNullOrEmpty(rel) ? "." : rel.Replace('\\', '/');
            }
            return diskPath;
        }

        private static string GetEntryDiskPath(ModelEntry e)
        {
            try
            {
                string abs = e.Model?.Uri?.AsAbsoluteUri ?? "";
                string path = UriToDiskPath(abs);
                if (path == null) return null;
                if (File.Exists(path) || Directory.Exists(path)) return path;
                string parent = Path.GetDirectoryName(path);
                string name   = Path.GetFileName(path);
                if (parent == null || !Directory.Exists(parent)) return null;
                var hits = Directory.GetFiles(parent, name + ".*");
                return hits.Length == 1 ? hits[0] : null;
            }
            catch { return null; }
        }

        // ── TryMoveEntry (static) ─────────────────────────────────────────────

        internal static bool TryMoveEntry(ModelEntry entry, string targetDiskDir, string fallbackProjDirPath, out string error)
        {
            error = null;
            try
            {
                string absUri = entry.Model.Uri.AsAbsoluteUri;
                string oldLocalPath = UriToDiskPath(absUri);
                if (oldLocalPath == null)
                {
                    error = $"[{entry.DisplayName}] Неподдерживаемая схема URI: {absUri}";
                    return false;
                }

                // Resolve actual file on disk (may be missing extension in URI)
                if (!File.Exists(oldLocalPath) && !Directory.Exists(oldLocalPath))
                {
                    string parentDir = Path.GetDirectoryName(oldLocalPath);
                    string baseName  = Path.GetFileName(oldLocalPath);
                    if (parentDir != null && Directory.Exists(parentDir))
                    {
                        var candidates = Directory.GetFiles(parentDir, baseName + ".*");
                        if (candidates.Length == 1)
                            oldLocalPath = candidates[0];
                        else if (candidates.Length > 1)
                        { error = $"[{entry.DisplayName}] Неоднозначный файл: {baseName}.*"; return false; }
                        else
                        { error = $"[{entry.DisplayName}] Файл не найден: {oldLocalPath}"; return false; }
                    }
                    else
                    { error = $"[{entry.DisplayName}] Файл не найден: {oldLocalPath}"; return false; }
                }

                string filename = Path.GetFileName(oldLocalPath);
                string newLocalPath = Path.Combine(targetDiskDir, filename);

                if (File.Exists(newLocalPath))
                { error = $"[{entry.DisplayName}] Файл уже существует: {newLocalPath}"; return false; }

                // Derive URI extension (may differ from disk extension for extensionless URIs)
                string uriExt = entry.Model.Uri.Extension ?? string.Empty;
                if (uriExt.Length > 0 && !uriExt.StartsWith(".", StringComparison.Ordinal))
                    uriExt = "." + uriExt;
                string fileBaseName = Path.GetFileNameWithoutExtension(filename);

                // Determine project root path for computing relative URIs
                var rootModel = entry.Model.Project?.Model;
                string projDirPath = null;
                if (rootModel?.Uri != null)
                {
                    string rp = UriToDiskPath(rootModel.Uri.AsAbsoluteUri);
                    if (rp != null) projDirPath = Path.GetDirectoryName(rp);
                }
                if (projDirPath == null) projDirPath = fallbackProjDirPath;

                // Compute "targetDiskDir" relative to project root (as forward-slash string)
                string relDirStr = ComputeRelDir(projDirPath, targetDiskDir); // e.g. "Archive/"

                // Absolute Topomatic URI for the new file location
                URI projDirUri = rootModel?.Uri?.DirectoryUri;
                URI newAbsFileUri = projDirUri != null
                    ? new URI(new URI(projDirUri, relDirStr), fileBaseName + uriExt)
                    : null;

                // Close model if open, move file, update project, reopen
                var project = entry.Model.Project;
                bool wasOpen = project.IsModelOpened(entry.Model);
                if (wasOpen) project.CloseModel(entry.Model);

                if (!Directory.Exists(targetDiskDir))
                    Directory.CreateDirectory(targetDiskDir);

                File.Move(oldLocalPath, newLocalPath);

                // Update reference in project root model
                object rootData = rootModel?.LockRead();
                var    refs     = ProjectRefs.Enumerate(rootData);
                if (refs != null && newAbsFileUri != null && projDirUri != null)
                {
                    string oldAbs = entry.Model.Uri.AsAbsoluteUri;
                    foreach (var reference in refs)
                    {
                        var refUri = ProjectRefs.GetUri(reference);
                        if (refUri == null) continue;
                        URI refAbsolute = refUri.IsAbsoluteUri
                            ? refUri
                            : new URI(projDirUri, refUri.AsAbsoluteUri);

                        if (string.Equals(refAbsolute.AsAbsoluteUri, oldAbs, StringComparison.OrdinalIgnoreCase))
                        {
                            // Store as relative URI: dirRelativeToRoot + filename
                            var newAbsDirUri  = new URI(projDirUri, relDirStr);
                            var relDirFromRoot = newAbsDirUri.GetRelativeUri(projDirUri);
                            ProjectRefs.SetUri(reference, new URI(relDirFromRoot, fileBaseName + uriExt));
                            break;
                        }
                    }
                    rootModel.Save(forced: true)?.AsyncWaitHandle.WaitOne();
                }

                // Update entry.Model.Uri via reflection
                if (newAbsFileUri != null)
                {
                    var uriProp = entry.Model.GetType().GetProperty("Uri");
                    if (uriProp != null && uriProp.CanWrite)
                        uriProp.SetValue(entry.Model, newAbsFileUri);
                }

                if (wasOpen) project.OpenModel(entry.Model);

                entry.PathId = PluginCoreOps.FindModelPathId(entry.Model) ?? entry.PathId;
                return true;
            }
            catch (Exception ex)
            {
                error = $"[{entry.DisplayName}] {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        // Server/cloud entries are detected by scheme (anything other than local/file).
        internal static bool IsServerEntry(ModelEntry e)
        {
            var scheme = e?.Model?.Uri?.Scheme;
            return !string.IsNullOrEmpty(scheme)
                && !scheme.Equals("local", StringComparison.OrdinalIgnoreCase)
                && !scheme.Equals("file",  StringComparison.OrdinalIgnoreCase);
        }

        internal static string ParentRel(string pathId)
        {
            var p = (pathId ?? "").Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return p.Length >= 2 ? string.Join("/", p.Take(p.Length - 1)) : "";
        }

        // Move a server/cloud model to another folder. Diagnosis (confirmed by the user: the blob did
        // reach the server at the new location) showed the blob relocation WORKS; the earlier failure
        // was repointing the project reference with a RELATIVE URI — for cloud the reference must be
        // ABSOLUTE (RoburProjectModel.References stores/loads absolute URIs). This implementation is
        // NON-DESTRUCTIVE: it uploads the blob to the new URI and repoints the reference to the new
        // ABSOLUTE URI, but NEVER deletes the old blob (left as a harmless orphan). So no data can be
        // lost; if the reference isn't found, the move is aborted and the model stays put.
        internal static bool TryMoveEntryServer(ModelEntry entry, string targetProjectRelDir, out string error)
        {
            error = null;
            try
            {
                if (!PluginCoreOps.HasPermission(entry.Model, PluginCoreOps.EDITOR_PERMISSION))
                {
                    error = $"[{entry.DisplayName}] Модель заблокирована на сервере. Сначала «Разблокировать».";
                    return false;
                }

                var  project    = entry.Model.Project;
                var  rootModel  = project.Model;
                URI  rootDirUri = rootModel?.Uri?.DirectoryUri;
                if (rootDirUri == null) { error = $"[{entry.DisplayName}] Не удалось определить корень проекта."; return false; }

                string oldAbsUri = entry.Model.Uri.AsAbsoluteUri;
                string fileName  = StripScope(entry.Model.Uri.LastPathComponent);

                // Strip the leading scope marker (":") that Topomatic PathIds / corrupted references
                // carry — otherwise it leaks into the upload path AND the stored reference (e.g. ":2").
                string dst       = StripScope((targetProjectRelDir ?? "").Replace('\\', '/').Trim('/'));
                URI    targetDir = dst.Length > 0 ? new URI(rootDirUri, dst + "/") : rootDirUri;
                URI    newUri    = new URI(targetDir, fileName);
                string newAbsUri = newUri.AsAbsoluteUri;

                if (string.Equals(oldAbsUri, newAbsUri, StringComparison.OrdinalIgnoreCase))
                    return true; // already there

                var newRes = FileManager.Open(newAbsUri);
                if (newRes == null) { error = $"[{entry.DisplayName}] Целевой ресурс недоступен."; return false; }

                // Pre-check on the SERVER (not the local cache): does the target already exist?
                // FileManager.Accessable() only inspects LocalDatabasePath (a local cache slot), so it
                // is meaningless for a server URI here — use the remote IResource.Exists instead.
                if (ServerExists(newRes))
                { error = $"[{entry.DisplayName}] В целевой папке уже есть «{fileName}»."; return false; }

                // 1) Copy the blob to the new URI (confirmed reaching the server).
                string localFile = FileManager.SyncRead(oldAbsUri, out _);
                if (string.IsNullOrEmpty(localFile) || !File.Exists(localFile))
                { error = $"[{entry.DisplayName}] Не удалось получить файл модели с сервера."; return false; }

                bool wasOpen = project.IsModelOpened(entry.Model);
                if (wasOpen) project.CloseModel(entry.Model);

                newRes.Upload(localFile);

                // 1b) Verify against the SERVER, not the local cache. FileManager.Accessable() only
                //     checks LocalDatabasePath — a local cache slot Upload NEVER populates — so it
                //     falsely reported «не подтвердилась» even though the blob reached the server.
                //     IResource.Exists routes through UpdateConnection().Exists = a real remote query.
                if (!ServerExists(newRes))
                { if (wasOpen) project.OpenModel(entry.Model); error = $"[{entry.DisplayName}] Загрузка в целевую папку не подтвердилась."; return false; }

                // 2) Repoint the project reference. Matching: references are stored either project-
                //    relative (base = empty URI) or absolute, and cloud paths are %-encoded (Cyrillic),
                //    so match on a normalized (resolved-absolute + URL-decoded) form.
                //    WRITING: the project file stores references PROJECT-RELATIVE (e.g. "Модели/ЦММ/
                //    Eg.sfcx"). Writing an absolute tfs:// URI makes Robur drop the model from the tree.
                //    So repoint to the clean relative path {targetFolder}/{name}.
                string relRefPath = dst.Length > 0 ? dst + "/" + fileName : fileName;
                URI    relRef     = new URI(relRefPath);

                object rootData  = rootModel.LockRead();
                var    refs      = ProjectRefs.Enumerate(rootData);
                bool repointed = false;
                string targetNorm = NormUri(entry.Model.Uri, rootDirUri);
                if (refs != null)
                {
                    foreach (var reference in refs)
                    {
                        var refUri = ProjectRefs.GetUri(reference);
                        if (refUri != null &&
                            string.Equals(NormUri(refUri, rootDirUri), targetNorm, StringComparison.OrdinalIgnoreCase))
                        {
                            ProjectRefs.SetUri(reference, relRef); // project-RELATIVE, matches Robur's stored format
                            repointed = true;
                            break;
                        }
                    }
                    if (repointed)
                        rootModel.Save(forced: true)?.AsyncWaitHandle.WaitOne();
                }

                if (!repointed)
                {
                    if (wasOpen) project.OpenModel(entry.Model);
                    string sample = refs == null
                        ? "(rootData=null)"
                        : string.Join(" | ", refs.Cast<object>().Take(6)
                                      .Select(r => NormUri(ProjectRefs.GetUri(r), rootDirUri)));
                    error = $"[{entry.DisplayName}] Ссылка не найдена (файл цел). Искал='{targetNorm}'. Ссылки: {sample}";
                    return false;
                }

                // 3) Point the model object at the new URI and reopen.
                var uriProp = entry.Model.GetType().GetProperty("Uri");
                if (uriProp != null && uriProp.CanWrite) uriProp.SetValue(entry.Model, newUri);
                if (wasOpen) project.OpenModel(entry.Model);

                entry.PathId = PluginCoreOps.FindModelPathId(entry.Model) ?? entry.PathId;

                // 4) Clean up the old blob now that the new one is confirmed uploaded AND the
                //    reference is repointed+saved. Best-effort: a delete failure leaves a harmless
                //    orphan (new blob + reference are already safe) rather than failing the move.
                try
                {
                    var oldRes = FileManager.Open(oldAbsUri);
                    if (oldRes != null && ServerExists(oldRes)) oldRes.Delete();
                }
                catch { /* orphan remains; move already succeeded */ }

                return true;
            }
            catch (Exception ex)
            {
                error = $"[{entry.DisplayName}] {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        // Server-side existence check. Unlike FileManager.Accessable() (which only inspects the LOCAL
        // cache at LocalDatabasePath and is never populated by Upload), IResource.Exists routes through
        // the scheme connection and queries the remote server. A false result errs safe (abort move).
        private static bool ServerExists(FileManager.IResource res)
        {
            try { return res != null && res.Exists; }
            catch { return false; }
        }

        // Strip the leading scope marker (":") that Topomatic PathIds carry, per segment. This colon is
        // NOT a real path character (invalid on Windows, absent from clean references like
        // "Модели/ЦММ/Eg.sfcx") — it leaks in from PathId scope prefixes and corrupted references.
        // ":Тест/1" → "Тест/1", "::Модели" → "Модели". A drive letter ("C:") is untouched (colon is
        // not leading). Empty segments are dropped.
        internal static string StripScope(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var segs = path.Replace('\\', '/').Split('/');
            var clean = segs.Select(s => s.TrimStart(':').Trim()).Where(s => s.Length > 0);
            return string.Join("/", clean);
        }

        // Normalize a reference/model URI to a comparable form: resolve project-relative refs to
        // absolute against the project root, then URL-decode (cloud Cyrillic paths are %-encoded).
        // Plain AsAbsoluteUri string equality misses both the relative-vs-absolute and encoding cases.
        private static string NormUri(URI u, URI rootDirUri)
        {
            if (u == null) return null;
            string s;
            try { s = u.IsAbsoluteUri ? u.AsAbsoluteUri : new URI(rootDirUri, u.AsAbsoluteUri).AsAbsoluteUri; }
            catch { s = u.AsAbsoluteUri; }
            try { s = Uri.UnescapeDataString(s); } catch { }
            return s?.Replace('\\', '/').TrimEnd('/');
        }

        // ── URI / path helpers ────────────────────────────────────────────────

        internal static string UriToDiskPath(string absUri)
        {
            if (absUri.StartsWith("local:///", StringComparison.OrdinalIgnoreCase))
                return absUri.Substring("local:///".Length).Replace('/', Path.DirectorySeparatorChar);
            if (absUri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                return absUri.Substring(8).Replace('/', Path.DirectorySeparatorChar);
            if (absUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                // UNC: file://server/share/path → \\server\share\path
                return "\\\\" + absUri.Substring(7).Replace('/', Path.DirectorySeparatorChar);
            }
            return null;
        }

        internal static string ComputeRelDir(string projDirPath, string targetDiskDir)
        {
            try
            {
                if (projDirPath == null) throw new InvalidOperationException("projDir is null");
                var baseUri   = new System.Uri(projDirPath.TrimEnd('\\', '/') + "\\");
                var targetUri = new System.Uri(targetDiskDir.TrimEnd('\\', '/') + "\\");
                string rel    = baseUri.MakeRelativeUri(targetUri).ToString();
                rel = System.Uri.UnescapeDataString(rel);
                if (!rel.EndsWith("/")) rel += "/";
                return rel;
            }
            catch
            {
                // Fallback: just forward-slash path
                string path = targetDiskDir.Replace('\\', '/').TrimEnd('/') + "/";
                if (projDirPath != null)
                {
                    string pref = projDirPath.Replace('\\', '/').TrimEnd('/') + "/";
                    if (path.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
                        path = path.Substring(pref.Length);
                }
                return path;
            }
        }
    }
}
