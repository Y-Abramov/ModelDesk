using System;
using System.Collections.Generic;
using System.IO;
using Topomatic.ApplicationPlatform;

namespace ModelDesk
{
    // Мост к ведомостям Robur.
    //
    // Изначально каталог строился через PluginManager.GenerateMenu("rbproj.project.mksht").
    // Живой тест (2026-07-28) показал: вне штатного построения меню GenerateMenu отдаёт
    // ПУСТОЕ дерево (Count == 0) даже для активной дорожной модели - подтверждено декомпилом
    // Topomatic.ApplicationPlatform.Plugins.PluginManager: единственные живые вызовы у самого
    // Robur происходят строго внутри обработчика события CreateMenu с готовым `e.Root`,
    // никогда ad hoc из стороннего кода. Разбирать флаги-выражение и воспроизводить это
    // событие - не стоит того.
    //
    // Вместо этого читаем то же самое, что видит GenerateMenu, но напрямую из текста
    // .plugin-файлов: "actions" (id -> cmd/title/description) и "menubars"["rbproj.project.mksht"]
    // (id -> priority). Оба поля - открытый текст, разбор - RoburSheetCatalog (чистый, без
    // ссылок на Robur, покрыт тестами). Запуск ведомости - тот же Execute(cmd), которым
    // это же самое действие вызвал бы клик по родному пункту меню.
    internal static class SheetCatalogRobur
    {
        internal const string MenuUid = "rbproj.project.mksht";

        // Делает модель активной. Без этого сгенерированная ведомость будет по другой модели.
        internal static void Activate(string pathId)
        {
            if (string.IsNullOrEmpty(pathId)) return;
            try { ApplicationHost.Current.Plugins.Execute("activate", new object[] { pathId }); }
            catch { /* активация - best effort, каталог всё равно построится */ }
        }

        // Строит каталог ведомостей, сканируя *.plugin рядом с самим Robur.
        // Возвращает null и заполняет error, если каталог собрать не удалось.
        internal static SheetNode BuildCatalog(out string error)
        {
            error = null;
            try
            {
                var actions = new Dictionary<string, RoburSheetCatalog.ActionDef>();
                var collected = new List<(string id, double priority)>();

                string dir = AppDomain.CurrentDomain.BaseDirectory;
                foreach (string path in Directory.GetFiles(dir, "*.plugin"))
                {
                    string json;
                    try { json = File.ReadAllText(path); }
                    catch { continue; } // файл занят/недоступен - пропускаем, не роняем каталог

                    RoburSheetCatalog.Ingest(json, MenuUid, actions, collected);
                }

                var root = RoburSheetCatalog.BuildCatalog(actions, collected);
                if (root.Children.Count == 0)
                {
                    error = "Каталог ведомостей пуст. Не найдено ни одного .plugin с меню \"" + MenuUid + "\".";
                    return null;
                }
                return root;
            }
            catch (Exception ex)
            {
                error = "Каталог ведомостей недоступен: " + ex.Message;
                return null;
            }
        }

        // Запуск ведомости - тот же Execute(cmd), которым Robur вызывает клик по пункту
        // родного меню «Проект → Создать ведомость» (тот же механизм, что "activate"/"open"/
        // "close" в этом модуле). Модель должна быть уже активирована (см. Activate).
        internal static bool Run(SheetNode node, out string error)
        {
            error = null;

            string cmd = node == null ? null : node.Tag as string;
            if (string.IsNullOrEmpty(cmd)) { error = "Пункт меню недоступен."; return false; }

            string uid, arg;
            RoburSheetCatalog.SplitCmd(cmd, out uid, out arg);

            try
            {
                if (arg != null) ApplicationHost.Current.Plugins.Execute(uid, new object[] { arg });
                else ApplicationHost.Current.Plugins.Execute(uid);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
    }
}
