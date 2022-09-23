using System.Collections.Generic;
using Dalamud.Configuration;

namespace DalamudRepoBrowser
{
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; }
        public string RepoMasters = string.Empty;
        public bool HideEnabledRepos = false;
        public bool HideBranches = true;
        public int RepoSort = 0;
        public int ShowOutdated = 0;
        public int MinStars = 2;
        public int MaxPlugins = 20;
        public HashSet<string> SeenRepos = new();
        public long LastUpdatedRepoList = 0;

        public void Initialize() { }

        public void Save() => DalamudApi.PluginInterface.SavePluginConfig(this);
    }
}
