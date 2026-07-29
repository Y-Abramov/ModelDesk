using System.Collections.Generic;
using ModelDesk.Mcp;
using Topomatic.ApplicationPlatform.Plugins;
using Topomatic.ToolBridge;

namespace ModelDesk
{
    public partial class ModelDeskPlugin
    {
        /// <summary>Хендлер broadcast "tool_request" от Topomatic.ToolBridge.
        /// args[0] - List&lt;ToolProvider&gt;, куда плагины складывают свои провайдеры.
        /// Имя "generate_tools" занято самим Topomatic.ToolBridge.dll (собственный
        /// broadcast-хендлер вендора) - дубль тихо ломает инициализацию ToolBridge
        /// целиком (см. PavePlan: Log.log "Dublicated function 'generate_tools'").
        /// Имя команды per-plugin, совпадение с мостом не требуется.</summary>
        [cmd("modeldesk_generate_tools")]
        private void GenerateTools(object[] args)
        {
            if (args == null || args.Length == 0) return;
            var providers = args[0] as List<ToolProvider>;
            if (providers == null) return;
            providers.Add(new ViewTools());
            providers.Add(new SheetTools());
        }
    }
}
