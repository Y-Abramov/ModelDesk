using System;
using Topomatic.ApplicationPlatform.Plugins;

namespace ModelDesk
{
    public class PluginHost : PluginHostInitializator
    {
        protected override Type[] GetTypes()
        {
            return new Type[] { typeof(ModelDeskPlugin) };
        }

        public override void Initialize(PluginFactory factory)
        {
            base.Initialize(factory);
        }
    }
}
