using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Topomatic.ApplicationPlatform.Plugins;

namespace ModelDesk.Mcp
{
    /// <summary>Модель проекта в терминах MCP: только то, что нужно инструментам.
    /// ModelEntry (диалог) сюда не тянем - он несёт группы, заметки, статусы,
    /// кэш дат и блокировки, к инструментам отношения не имеющие.</summary>
    internal sealed class McpModel
    {
        public string Name;
        public string ModelType;
        public string PathId;
        public bool   IsOpen;
    }

    /// <summary>Перечисление моделей проекта и поиск по имени для MCP-инструментов.
    /// Те же вызовы PluginCoreOps, что в ModelManagerDialog.LoadModels - имя модели
    /// совпадает с колонкой «Модель» диспетчера.</summary>
    internal static class ModelLookup
    {
        public static List<McpModel> CollectAll()
        {
            var openIds = new HashSet<string>(StringComparer.Ordinal);
            PluginCoreOps.FilterOpenedModels(pm =>
            {
                var pid = PluginCoreOps.FindModelPathId(pm);
                if (!string.IsNullOrEmpty(pid)) openIds.Add(pid);
                return false;
            });

            var models = new List<McpModel>();
            PluginCoreOps.FilterModels(pm =>
            {
                var pid = PluginCoreOps.FindModelPathId(pm);
                if (string.IsNullOrEmpty(pid)) return false;
                var fileName = PluginCoreOps.GetFileName(pm) ?? string.Empty;
                models.Add(new McpModel
                {
                    Name      = Path.GetFileNameWithoutExtension(fileName),
                    ModelType = pm.ModelType ?? string.Empty,
                    PathId    = pid,
                    IsOpen    = openIds.Contains(pid)
                });
                return false;
            });
            return models;
        }

        /// <summary>Ровно одно совпадение по имени. Ноль или несколько - ArgumentException
        /// с перечнем вариантов: молча изолировать не ту модель хуже, чем переспросить.</summary>
        public static McpModel FindByName(string name)
        {
            var all = CollectAll();
            var hits = all.Where(
                m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();

            if (hits.Count == 1) return hits[0];

            if (hits.Count == 0)
                throw new ArgumentException(
                    "Модель '" + name + "' не найдена в проекте. Модели проекта: " +
                    string.Join(", ", all.Select(m => m.Name)) + ".");

            throw new ArgumentException(
                "Имя '" + name + "' носят несколько моделей: " +
                string.Join(", ", hits.Select(m => m.Name + " (" + m.ModelType + ")")) +
                ". Уточните, какая нужна.");
        }
    }
}
