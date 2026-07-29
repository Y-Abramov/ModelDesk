using System;
using System.Collections.Generic;
using System.Linq;
using Topomatic.ToolBridge;

namespace ModelDesk.Mcp
{
    /// <summary>MCP-инструменты видимости моделей. Публикуются мостом Topomatic.ToolBridge
    /// через broadcast "tool_request". Изоляция повторяет семантику ModelManagerDialog.OnIsolate
    /// (цель открыть, прочее закрыть), а не кнопки ленты isolate_model (та требует клика мышью
    /// и цель не открывает). Открытие цели обязательно: инструменты RoadStyle видят только
    /// открытые модели.</summary>
    internal sealed class ViewTools : ToolProvider
    {
        [ToolDef(
            Name = "modeldesk_list_models",
            Description = "[МОДЕЛИ] Возвращает модели проекта: имя, тип, открыта ли в виде. " +
                          "Имя модели отсюда - то, чем адресуются остальные инструменты ABR.",
            InputSchema = @"{
              'type': 'object',
              'properties': {
                'modelType': { 'type': 'string', 'description': 'Фильтр по типу модели, напр. road, dtm, site, soilworks. Без него - все модели.' }
              },
              'additionalProperties': false
            }",
            ReadOnlyHint = true)]
        private object ListModels(Dictionary<string, object> args)
        {
            var all = ModelLookup.CollectAll();
            var typeFilter = JsonUtils.GetString(args, "modelType", null);

            var shown = string.IsNullOrEmpty(typeFilter)
                ? all
                : all.Where(m => string.Equals(m.ModelType, typeFilter,
                        StringComparison.OrdinalIgnoreCase)).ToList();

            return new
            {
                result = new
                {
                    models = shown.Select(m => new
                    {
                        name = m.Name,
                        modelType = m.ModelType,
                        isOpen = m.IsOpen
                    }).ToArray(),
                    typesInProject = all.Select(m => m.ModelType)
                        .Where(t => !string.IsNullOrEmpty(t))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                },
                description = "Модели проекта. Поле isOpen - показана ли модель в виде. " +
                              "typesInProject - типы, реально встреченные в этом проекте.",
                status = "Получено моделей: " + shown.Count + "."
            };
        }

        [ToolDef(
            Name = "modeldesk_isolate_model",
            Description = "[МОДЕЛИ] Изолирует модель по имени: открывает её, если закрыта, " +
                          "и убирает из вида все остальные модели проекта.",
            InputSchema = @"{
              'type': 'object',
              'properties': {
                'modelName': { 'type': 'string', 'description': 'Имя модели из modeldesk_list_models.' }
              },
              'required': ['modelName'],
              'additionalProperties': false
            }",
            DestructiveHint = true)]
        private object IsolateModel(Dictionary<string, object> args)
        {
            var modelName = JsonUtils.RequireString(args, "modelName");
            var target = ModelLookup.FindByName(modelName);

            var opened = false;
            if (!target.IsOpen)
                opened = ModelDeskPlugin.TryExecute("open", new object[] { target.PathId });

            var closed = 0;
            foreach (var m in ModelLookup.CollectAll())
            {
                if (string.Equals(m.PathId, target.PathId, StringComparison.Ordinal)) continue;
                if (!m.IsOpen) continue;
                if (ModelDeskPlugin.TryExecute("close", new object[] { m.PathId })) closed++;
            }

            return new
            {
                result = new
                {
                    modelName = target.Name,
                    modelType = target.ModelType,
                    opened,
                    closedCount = closed
                },
                description = "Модель «" + target.Name + "» изолирована: она открыта, " +
                              "остальные модели убраны из вида.",
                status = "Изолировано. Убрано из вида: " + closed + "."
            };
        }

        [ToolDef(
            Name = "modeldesk_show_all_models",
            Description = "[МОДЕЛИ] Показывает все модели проекта - откат изоляции.",
            InputSchema = @"{
              'type': 'object',
              'properties': {},
              'additionalProperties': false
            }",
            DestructiveHint = true)]
        private object ShowAllModels(Dictionary<string, object> args)
        {
            var openedCount = 0;
            foreach (var m in ModelLookup.CollectAll())
            {
                if (m.IsOpen) continue;
                if (ModelDeskPlugin.TryExecute("open", new object[] { m.PathId })) openedCount++;
            }

            return new
            {
                result = new { openedCount },
                description = "Все модели проекта показаны в виде.",
                status = "Открыто моделей: " + openedCount + "."
            };
        }
    }
}
