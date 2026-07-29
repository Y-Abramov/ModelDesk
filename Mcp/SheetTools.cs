using System;
using System.Collections.Generic;
using System.Linq;
using Topomatic.ToolBridge;

namespace ModelDesk.Mcp
{
    /// <summary>MCP-инструменты ведомостей Robur. Тонкая обёртка над SheetCatalogRobur/SheetTree
    /// (тот же каталог, что показывает вкладка «Ведомости»). Каталог строится разбором .plugin
    /// рядом с Robur - GenerateMenu живьём отдаёт пустое дерево, см. ModelView/CLAUDE.md.</summary>
    internal sealed class SheetTools : ToolProvider
    {
        [ToolDef(
            Name = "modeldesk_list_sheets",
            Description = "[ВЕДОМОСТИ] Каталог ведомостей Robur (меню «Проект - Создать ведомость»). " +
                          "Каталог общий для проекта и от модели не зависит. Поле isEarthworks " +
                          "отмечает ведомости земляных работ и объёмов.",
            InputSchema = @"{
              'type': 'object',
              'properties': {
                'query': { 'type': 'string', 'description': 'Поиск по подстроке в заголовке, описании и разделе.' },
                'earthworksOnly': { 'type': 'boolean', 'description': 'Только ведомости земляных работ и объёмов. По умолчанию false.' }
              },
              'additionalProperties': false
            }",
            ReadOnlyHint = true)]
        private object ListSheets(Dictionary<string, object> args)
        {
            var items = LoadCatalog();
            var query = JsonUtils.GetString(args, "query", null);
            var shown = SheetTree.Filter(items, query);

            List<SheetNode> earth, others;
            SheetTree.SplitEarthworks(shown, out earth, out others);

            var earthworksOnly = JsonUtils.GetBool(args, "earthworksOnly", false) ?? false;
            var selected = earthworksOnly ? earth : shown;
            var earthSet = new HashSet<SheetNode>(earth);

            return new
            {
                result = new
                {
                    sheets = selected.Select(n => new
                    {
                        title = SheetTree.Normalize(n.Text),
                        hint = n.Hint,
                        section = n.Path,
                        isEarthworks = earthSet.Contains(n)
                    }).ToArray(),
                    totalInCatalog = items.Count,
                    earthworksInCatalog = earth.Count
                },
                description = "Каталог ведомостей. Заголовок из поля title передаётся в " +
                              "modeldesk_run_sheet. Применимость к типу модели каталог не проверяет - " +
                              "неподходящую ведомость отвергнет сам Robur при запуске.",
                status = "Получено ведомостей: " + selected.Count + "."
            };
        }

        [ToolDef(
            Name = "modeldesk_run_sheet",
            Description = "[ВЕДОМОСТИ] Запускает ведомость по заголовку для названной модели. " +
                          "ВНИМАНИЕ: открывает штатный мастер ведомости Robur - модальное окно. " +
                          "Вызов не вернётся, пока оператор не пройдёт мастер; это ожидаемое " +
                          "поведение, а не зависание. Цифры ведомости инструмент не возвращает.",
            InputSchema = @"{
              'type': 'object',
              'properties': {
                'modelName': { 'type': 'string', 'description': 'Имя модели из modeldesk_list_models.' },
                'sheetTitle': { 'type': 'string', 'description': 'Заголовок ведомости из modeldesk_list_sheets.' }
              },
              'required': ['modelName', 'sheetTitle'],
              'additionalProperties': false
            }",
            DestructiveHint = true)]
        private object RunSheet(Dictionary<string, object> args)
        {
            var modelName = JsonUtils.RequireString(args, "modelName");
            var sheetTitle = JsonUtils.RequireString(args, "sheetTitle");

            var model = ModelLookup.FindByName(modelName);
            var node = FindSheet(sheetTitle);

            // Обязательно ДО запуска: без активации Robur построит ведомость по текущей
            // активной модели, а не по названной. Ошибки при этом не будет - придут не те данные.
            SheetCatalogRobur.Activate(model.PathId);

            string error;
            if (!SheetCatalogRobur.Run(node, out error))
                throw new InvalidOperationException(
                    "Не удалось запустить ведомость «" + SheetTree.Normalize(node.Text) + "»: " + error);

            return new
            {
                result = new
                {
                    modelName = model.Name,
                    sheetTitle = SheetTree.Normalize(node.Text),
                    launched = true
                },
                description = "Ведомость запущена для модели «" + model.Name + "».",
                status = "Запущено."
            };
        }

        private static List<SheetNode> LoadCatalog()
        {
            string error;
            var root = SheetCatalogRobur.BuildCatalog(out error);
            if (root == null)
                throw new InvalidOperationException(error ?? "Каталог ведомостей недоступен.");
            return SheetTree.Flatten(root);
        }

        /// <summary>Родные заголовки несут висящие точки («Площадей и объемов...») и кратные
        /// пробелы - сравниваем нормализованное с нормализованным с обеих сторон.</summary>
        private static SheetNode FindSheet(string title)
        {
            var items = LoadCatalog();
            var wanted = SheetTree.Normalize(title);

            var hits = items.Where(n => string.Equals(
                SheetTree.Normalize(n.Text), wanted, StringComparison.OrdinalIgnoreCase)).ToList();

            if (hits.Count == 1) return hits[0];

            if (hits.Count == 0)
                throw new ArgumentException(
                    "Ведомость '" + title + "' не найдена в каталоге. Доступные: " +
                    string.Join(", ", items.Select(n => SheetTree.Normalize(n.Text))) + ".");

            throw new ArgumentException(
                "Заголовок '" + title + "' носят несколько ведомостей каталога. Уточните запрос.");
        }
    }
}
