# ModelDesk — детали модуля

> Диспетчер моделей Robur: фильтрация, группировка, переименование, перемещение файлов моделей проекта (ЦММ, трасса, дорога и др.). Управление видимостью моделей в виде.

**Версия:** 1.2.0 | **Сборка:** Debug | **LangVersion:** latest | **Статус:** ✅ В продакшене  
**Артефакт:** `ModelDesk-1.2.0.tpm`  
**GitHub:** https://github.com/Y-Abramov/modeldesk

---

## Зарегистрированные команды

| Команда | Описание |
|---------|---------|
| `open_model_manager` | Открыть диспетчер моделей (ModelManagerDialog) |
| `switch_active_model` | Переключить активную модель |
| `hide_clicked_model` | Скрыть модель по клику |
| `isolate_model` | Изолировать модель (остальные скрыть) |
| `hide_all_models` | Скрыть все модели |
| `show_all_models` | Показать все модели |
| `about_modeldesk` | Диалог "О программе" |
| `open_road_object` | Проводник: окно дороги, вкладка «Сводка» (активная/первая открытая) |
| `road_construction` | Проводник: окно дороги, вкладка «Конструкция» (инлайн/пакетная правка) |
| `road_copy_construction` | Проводник: диалог копирования конструкции между дорогами |
| `open_model_card` | Карточка модели: сводка + ведомости (дорога → RoadObjectForm) |
| `model_make_sheet` | Карточка модели сразу на вкладке «Ведомости» |

---

## Сборка

```powershell
cd "c:\Dev\Модули\ModelView"
dotnet build -c Debug
```

---

## Файлы модуля

| Файл | Назначение |
|------|-----------|
| `ModelDeskPlugin.cs` | Команды ribbon: `open_model_manager`, `switch_active_model`, `hide_clicked_model`, `isolate_model`, `hide_all_models`, `show_all_models`, `about_modeldesk` |
| `ModelManagerDialog.cs` | Главный диалог диспетчера моделей (~2000 строк) |
| `ModelEntry.cs` | Модель данных строки списка |
| `ModelDeskSettings.cs` | Чтение/запись `{project}_modeldesk.json` |
| `MoveToFolderDialog.cs` | Диалог перемещения файлов + static helpers: `UriToDiskPath`, `ComputeRelDir`, `TryMoveEntry` |
| `BulkRenameDialog.cs` | Диалог массового переименования |
| `ListViewColumnSorter.cs` | Сортировка колонок ListView |
| `SwitchModelCommand.cs` | Логика `switch_active_model` (отдельный файл) |
| `..\Shared\Sdk\*.cs` (линк) | ABR SDK: `AbrIcon.Create()` + база `AbrAboutForm` (миграция 2026-07-12, локального AbrIcon.cs больше нет) |
| `AboutDialog.cs` | Тонкий наследник `Abr.Sdk.AbrAboutForm`; версия - из AssemblyVersion (csproj `<Version>`) |
| `SheetNode.cs` | Чистая модель каталога ведомостей + `SheetTree` (Flatten/SplitEarthworks/Filter). Линкуется в тесты |
| `RoburSheetCatalog.cs` | Чистый разбор `.plugin` (толерантный к дублям ключей JSON-парсер + сборка каталога). Линкуется в тесты |
| `SheetCatalogRobur.cs` | Мост к Robur: сканирует `*.plugin`, строит каталог через `RoburSheetCatalog`, запуск = `Execute(cmd)` |
| `SheetsTabControl.cs` | Вкладка «Ведомости»: поиск, группы «Земляные работы»/«Прочие», запуск |
| `ModelCardForm.cs` | Карточка модели не-дорожного типа: «Сводка» + «Ведомости» |

---

## Архитектура ModelManagerDialog

### Поля

```csharp
List<ModelEntry> _all          // все модели проекта
HashSet<string> _openIds       // id открытых моделей (из Robur)
HashSet<string> _activeTypes   // фильтр по типу (кнопки вверху)
HashSet<string> _activeStatuses // фильтр по статусу
ModelDeskSettings _settings    // данные из {project}_modeldesk.json
string _settingsPath           // путь к json
string _activeGroup            // текущая группа (null = Все)
bool _projectTreeCollapsed     // состояние кнопки Свернуть/Развернуть
List<(ModelEntry, string)> _lastUndoItems // для Undo переименования
```

### Ключевые методы

| Метод | Что делает |
|-------|-----------|
| `LoadModels()` | Загружает все модели проекта через `PluginCoreOps`, заполняет `_all` и `_openIds` |
| `ApplyFilter()` | Фильтрует `_all` по типу + статусу + группе + тексту поиска → `RefreshRows()` |
| `RefreshRows()` | Перерисовывает `_list` (ListView) из отфильтрованного набора |
| `OnOpen/OnClose/OnIsolate` | Открытие/закрытие моделей через `PluginCoreOps` + обновляют `_openIds` |
| `OnRename` | Переименование файла на диске через `File.Move` + обновление URI в project |
| `OnMoveToFolder` | Вызывает `MoveToFolderDialog.TryMoveEntry(...)` |
| `SaveState/LoadState` | Сохранение/восстановление состояния (что открыто) в json |
| `ToggleProjectTree()` | Свернуть/развернуть дерево проекта в главном окне Robur (рефлексия) |
| `OnCreateProjectFolder()` | Создать папку в проекте (рефлексия по References, экспериментально) |
| `OnAutoGroupByFolder()` | Авто-группировка по папкам на диске |
| `OnExport()` | Экспорт списка моделей в CSV |

### UI-структура (Dock, сверху вниз)

```
pType (FlowLayoutPanel, DockStyle.Top)   — кнопки типов (иконки 26×26)
pSearch (Panel, DockStyle.Top, h=32)     — поиск + кнопки: Свернуть, Статус, Выбрать все, Снять выбор
_groupPanel (FlowLayoutPanel, Top)       — кнопки групп (видна только если есть группы)
SplitContainer (Fill)
  ├── Left: _list (ListView, CheckBoxes, FullRowSelect)
  └── Right: InfoPanel (инфо + заметка + лог)
statusBar (Panel, Bottom)               — _statusLabel + кнопки Открыть/Закрыть/... 
```

---

## Типы моделей (TypeFilters)

19 типов в массиве `TypeFilters`:  
ЦММ (`dtm`), Трасса (`survey`), Дорога (`road`), Геология (`global_glg`), Метро (`metro`),  
Обустройство (`arr`), ЖД (`rail`), Путевое (`railways`), Инженерные сети (`pipe`),  
Труба-водопропускная (`culvert`), Площадка (`site`), Картограммы (`cartograms`),  
Земляные (`soilworks`), DWG (`application/dwg`), DWP (`application/prf-dwl`),  
Облако (`cloud`), Документы (`application/edocx`), Файл (`octet-stream`), Папка (`folder`).

---

## IProjectModel.Move() — NO-OP

`IProjectModel.Move()` ничего не делает. Переименование:

```csharp
project.Models.Remove(model);
File.Move(oldPath, newPath);
project.Models.Add(newPath);
```

---

## Формат `{project}_modeldesk.json`

```json
{
  "open":          ["model-id-1", "model-id-2"],
  "groups":        ["ГруппаА", "ГруппаБ"],
  "modelGroups":   { "model-id-1": "ГруппаА" },
  "modelStatuses": { "model-id-1": "done" },
  "modelNotes":    { "model-id-1": "примечание" }
}
```

**Legacy-формат:** bare JSON array `[id1, id2]` — только поле `open`, обратная совместимость поддерживается.  
**Парсится:** вручную через `Regex` (не `JavaScriptSerializer`).

---

## ToggleProjectTree — рефлексия

Кнопка "Свернуть ▶" / "Развернуть ▼" управляет деревом Robur через рефлексию.  
Treeview — кастомный контрол без `CollapseAll`. Collapse/Expand — методы на **Node**, не на TreeView.

```
Treeview.Nodes (геттер SpecialName)        → NodeCollection
NodeCollection[int]                         → Node  
Node._0086_0087() / _0086_0088()           → Collapse / Expand (порядок определяется в рантайме)
Node.IsExpanded (bool SpecialName геттер)  → для авто-определения какой метод Collapse
Node.Children (SpecialName геттер)         → дочерние NodeCollection (рекурсия)
```

"Развернуть" → `ProjectExplorer.RefreshTree()` (сбрасывает дерево в исходное состояние).

---

## MoveToFolderDialog — static helpers

```csharp
// Конвертация URI → путь на диске
MoveToFolderDialog.UriToDiskPath(absUri)
// local:/// → Windows path; file:/// → Windows path; file://server/ → \\server\ (UNC)

// Относительный путь между двумя папками
MoveToFolderDialog.ComputeRelDir(projDir, targetDir)  // internal static

// Переместить файл + обновить project reference
MoveToFolderDialog.TryMoveEntry(entry, targetDiskDir, fallbackProjDir, out error)
// Логика: закрыть → File.Move → обновить References (рефлексия) → переоткрыть
```

**Статус `TryMoveEntry`:** ✅ подтверждено рантаймом (локально + сервер, включая cleanup старого блоба на сервере).

---

## Поиск главного окна Robur (Control)

```csharp
private static Control FindControlByName(Control parent, string name)
{
    foreach (Control c in parent.Controls)
    {
        if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
        var found = FindControlByName(c, name);
        if (found != null) return found;
    }
    return null;
}
```

---

## Открытые задачи / нерешённые вопросы

1. **Немодальный режим** — обсуждалось, не реализовано. Подход: `Show()` + singleton в `ModelDeskPlugin`, кнопка "Обновить" вместо таймера, кнопка OK → "Закрыть".
2. **Вложенные папки на сервере** — `PluginCoreOps.CreateFolder` создаёт только в корне; в подпапке нет. Отложено.
3. **Edge: модель занята ДРУГИМ юзером** — `AddPermission` не поможет, нужен `IResource.UnlockWrite()` force-путь. Отложено.
4. **DWP подтипы** — возможно есть другие кроме `application/prf-dwl`.

---

## Команды видимости моделей

`switch_active_model`, `hide_clicked_model`, `isolate_model`, `hide_all_models`, `show_all_models` — **только здесь**. Не дублировать в других модулях.

---

## Доступ к RoadAlignment (Проводник, v1.2.0 — в разработке)

Мост в `RoadModelAccess.cs`. Путь доступа подтверждён по коду `SwitchModelCommand` +
рефлексией SDK **16.0.60.11**:

```
IProjectModel.LockRead() -> AlignmentModel (Topomatic.Alg.Model)   // парного UnlockRead в SDK НЕТ
AlignmentModel.Alignment -> Alignment; для дороги == RoadAlignment (Topomatic.Alg.Road)
```

- `RoadAlignment` **не имеет свойства Name** — отображаемое имя брать `PluginCoreOps.GetFileName(model)`.
- Таблицы конструкции: `RoadAlignment.Urb` (UrbParams) → `MainStrips.Widths/.Grades` (IList<`MainItem`>),
  `SideStrips.Widths` (IList<`SideItem`>), `DividerStrips.Widths` (IList<`DividerItem`>),
  `Construction.ConstructionItems` (IList<`ConstructionItem`>). Namespace структур —
  `Topomatic.Alg.Road.Urb.GeneralStrips`. Строки — **value-type struct** (public поля), правка =
  пересборка IList. Транзакция: `urb.BeginUpdate(caption)/EndUpdate()` → штатный Undo.
  ⚠ `ConstructionItem` помечен `[Obsolete]` в 60.11 (работает; замену уточнить при доработке).
  ⚠ Виражи (`VirageWidthItem`) не имеют контейнера на верхнем уровне UrbParams в 60.11 — в v1 не входят.
- Поперечники: `RoadAlignment.SelectedSections` : `SelectedSectionsCollection` (Topomatic.Alg.Crs).
  `.Items` = `IEnumerable<KeyValuePair<string, SelectedSections>>`. Набор `SelectedSections`:
  `int Count`, `double this[int i]` (станция по индексу, read-only),
  `bool this[double station]` (флаг выбора, **read+write**), `AddSection(double)`, `Clear()`;
  транзакция `BeginUpdate/EndUpdate`.

Прореживание поперечников из v1 **исключено** (план 2026-07-25: чистка таблиц/поперечники отменены).
`GetSectionStations` (чтение) используется в `RoadSummary` для счётчика поперечников; write-путь
`RemoveSections` оставлен как задел, UI его не вызывает. Спайк `road_dump_spike` удалён (2026-07-25).

## PluginHost

Вызывает `base.Initialize(factory)` стандартно (в отличие от AbrModules).

---

## UiTheme + Bootstrap (2026-07-14)

- **UiTheme:** экшн-кнопки всех диалогов (ModelManagerDialog тулбар/статус-бар/`ActionBtn`/модалки, MoveToFolderDialog, BulkRenameDialog) - через `Abr.Sdk.UiTheme.MakeButton(text, BtnKind, w, h)` (Primary = Закрыть окно/Переместить/Применить/Создать/OK, Ghost = остальное). Тип-фильтры (`_typeBtns`/`_btnAll`) и групп-кнопки (`FlatStyle.Flat` с кастомным toggle-цветом) + variable-insert `vb` в BulkRename - НЕ трогать.
- **Bootstrap:** `ModelDeskPlugin.Initialize` (override) - `base.Initialize(factory)` + `Abr.Bootstrap.AbrBootstrap.Attach("Диспетчер моделей", null, null)`. csproj линкует `..\Shared\Bootstrap\*.cs` + ссылки `System.Web.Extensions`, `System.IO.Compression`, `System.IO.Compression.FileSystem`. `build-tpm.ps1` - с `-NeedsSetupExe`.

## Ведомости Robur (v1.2.0)

Ведомость в Robur называется **sheet**. Меню «Проект → Создать ведомость» = узел `rbproj.project.mksht`,
собирается из 20+ `.plugin`.

**GenerateMenu не используется - живьём отдаёт пустое дерево.** Первая версия строила каталог через
`PluginManager.GenerateMenu("rbproj.project.mksht", null, root)`; живой тест 2026-07-28 показал
`root.Count == 0` даже для активной дорожной модели. Декомпил `Topomatic.ApplicationPlatform.Plugins.PluginManager`
подтвердил: единственные живые вызовы `GenerateMenu` у самого Robur происходят строго внутри обработчика
события `CreateMenu`, с готовым `e.Root` от фреймворка - ad hoc вне этого события дерево остаётся пустым
(вероятная причина - контекст вычисления `flags`-выражений, привязанный к событию, не к «активной модели»).

**Текущий механизм - разбор `.plugin` напрямую + `Execute(cmd)`:**

```csharp
// RoburSheetCatalog.cs - чистый разбор (без Topomatic), линкуется в тесты
Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.plugin")   // 110 файлов рядом с Robur
  → JParser.Parse(json)                                                // толерантный к дублям ключей
  → ingest "actions" (id → cmd/title/description)
  → ingest "menubars"["rbproj.project.mksht"] (id → priority)          // ключ повторяется НЕСКОЛЬКО РАЗ
                                                                        // в одном файле - обычный JObject
                                                                        // потерял бы все, кроме последнего
  → BuildCatalog: резолвит id → SheetNode { Text=title, Tag=cmd }

// SheetCatalogRobur.cs - вызов у Robur
ApplicationHost.Current.Plugins.Execute("activate", new object[]{ pathId }); // как и раньше
ApplicationHost.Current.Plugins.Execute(cmd);                                // запуск ведомости
```

Тот же `Execute`, которым в этом модуле уже вызываются `"activate"/"open"/"close"` - живой, проверенный путь.
Живая проверка офлайн (2026-07-28, эта же машина): 110 `.plugin`, 0 ошибок разбора, 35 резолвящихся ведомостей,
включая `"Площадей и объемов..." → road_areas_and_volumes_sheet`.

**Известные упрощения v1** (сознательно, не баги):
- Вложенные подменю в `items` (объекты, не строки - напр. `occupied_area` в `extentions.plugin`) не
  разворачиваются, только плоские пункты.
- `flags`-выражение (применимость к типу модели) не интерпретируется - каталог показывает ВСЕ найденные
  ведомости всегда; если модель не подходит, ошибку покажет сам Robur при запуске команды.
- Cross-file ссылки `"namespace.id"` (напр. `arrangements_sheets.id_rsf_sheet_work`) резолвятся через
  fallback по суффиксу после последней точки.
- `cmd` с встроенным аргументом в кавычках (`"sheet_smdx_entity \"SmdxEntity\""`) разбирается через
  `RoburSheetCatalog.SplitCmd` перед `Execute(uid, args)`.

Задел на v2 (свой просмотр данных, ссылок в csproj пока НЕТ):
`tables_single_sheet(object[]{ "generate_<x>_sheet", modelId })` - тот же путь без мастера;
`Plugins.Execute("generate_<x>_sheet", new object[]{ projectModel, args })` → `UserSheet`
(`Topomatic.Tables.Export`) → `PrepareData(false)` → `Execute(TablesDocument)` →
`Table.RowsCount/ColumnsCount/Cell(r,c)`. Экспорт - `TablesExportService`.
