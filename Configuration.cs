using Dalamud.Configuration;

namespace DalamudRepoBrowser
{
    public class Configuration : IPluginConfiguration
    {
        public const string DefaultRepoMaster = @"https://raw.githubusercontent.com/UnknownX7/DalamudRepoBrowser/master/repomaster.json";

        public int Version { get; set; }
        public string RepoMasters = DefaultRepoMaster;

        public void Initialize() { }

        public void Save() => DalamudApi.PluginInterface.SavePluginConfig(this);
    }
}
