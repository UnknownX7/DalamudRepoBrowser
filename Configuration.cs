using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;

namespace DalamudRepoBrowser
{
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; }

        [JsonIgnore] private DalamudPluginInterface pluginInterface;

        public void Initialize(DalamudPluginInterface p)
        {
            pluginInterface = p;
        }

        public void Save()
        {
            pluginInterface.SavePluginConfig(this);
        }
    }
}
