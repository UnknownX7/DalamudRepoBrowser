using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;

namespace DalamudRepoBrowser
{
    public class Configuration : IPluginConfiguration
    {
        public const string DefaultRepoMaster = @"https://raw.githubusercontent.com/UnknownX7/DalamudRepoBrowser/master/repomaster.json";

        public int Version { get; set; }
        public string RepoMasters = DefaultRepoMaster;

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
