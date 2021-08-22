using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[assembly: AssemblyTitle("DalamudRepoBrowser")]
[assembly: AssemblyVersion("1.0.0.1")]

namespace DalamudRepoBrowser
{
    public class DalamudRepoBrowser : IDalamudPlugin
    {
        public string Name => "DalamudRepoBrowser";
        public static DalamudPluginInterface Interface { get; private set; }
        private PluginCommandManager commandManager;
        public static Configuration Config { get; private set; }
        public static DalamudRepoBrowser Plugin { get; private set; }

        public static int currentAPILevel;
        public static PropertyInfo dalamudRepoSettingsProperty;
        private static List<ThirdRepoSetting> _dalamudRepoSettings;
        public static List<ThirdRepoSetting> DalamudRepoSettings
        {
            get => _dalamudRepoSettings ??= (List<ThirdRepoSetting>)dalamudRepoSettingsProperty?.GetValue(_dalamudConfig);
            set => _dalamudRepoSettings = value;
        }

        public static List<(string url, List<(string name, string description, string repo)> plugins)> repoList = new();
        public static HashSet<string> fetchedRepos = new();
        public static int sortList;

        public static int _fetch = 0;

        private static object _dalamudPluginManager;
        private static object _dalamudPluginRepository;
        private static object _dalamudConfig;
        private static MethodInfo _pluginReload;
        private static MethodInfo _configSave;

        public void Initialize(DalamudPluginInterface p)
        {
            Plugin = this;
            Interface = p;

            Config = (Configuration)Interface.GetPluginConfig() ?? new();
            Config.Initialize(Interface);

            commandManager = new();

            try
            {
                ReflectRepos();
                Interface.UiBuilder.OnBuildUi += PluginUI.Draw;
            }
            catch (Exception e) { PluginLog.LogError(e, "Failed to load."); }
        }

        private static void ReflectRepos()
        {
            var dalamud = (Dalamud.Dalamud)Interface.GetType()
                .GetField("dalamud", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Interface);

            _dalamudPluginManager = dalamud?.GetType()
                .GetProperty("PluginManager", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(dalamud);

            currentAPILevel = (int)_dalamudPluginManager?.GetType()
                .GetField("DalamudApiLevel", BindingFlags.Static | BindingFlags.Public)
                ?.GetValue(_dalamudPluginManager);

            _dalamudPluginRepository = dalamud?.GetType()
                .GetProperty("PluginRepository", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(dalamud);

            _pluginReload = _dalamudPluginRepository?.GetType()
                .GetMethod("ReloadPluginMasterAsync", BindingFlags.Instance | BindingFlags.Public);

            _dalamudConfig = dalamud?.GetType()
                .GetProperty("Configuration", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(dalamud);

            _configSave = _dalamudConfig?.GetType()
                .GetMethod("Save", BindingFlags.Instance | BindingFlags.Public);

            dalamudRepoSettingsProperty = _dalamudConfig?.GetType()
                .GetProperty("ThirdRepoList", BindingFlags.Instance | BindingFlags.Public);
        }

        public static void AddRepo(string url)
        {
            DalamudRepoSettings.Add(new ThirdRepoSetting { Url = url, IsEnabled = true });
            SaveDalamudConfig();
            ReloadPluginMasters();
        }

        public static void ToggleRepo(string url)
        {
            try
            {
                var repo = DalamudRepoSettings.First(x => x.Url == url);
                repo.IsEnabled ^= true;
                SaveDalamudConfig();
                ReloadPluginMasters();
            }
            catch
            {
                AddRepo(url);
            }
        }

        public static void FetchRepoMasters()
        {
            lock (repoList)
            {
                _fetch++;
                repoList.Clear();
            }

            lock (fetchedRepos)
                fetchedRepos.Clear();

            foreach (var repoMaster in Config.RepoMasters.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(repoMaster))
                    FetchRepoListAsync(repoMaster);
            }
        }

        public static void FetchRepoListAsync(string repoMaster)
        {
            PluginLog.LogInformation($"Fetching repositories from {repoMaster}");

            var startedFetch = _fetch;
            Task.Run(() =>
            {
                try
                {
                    using var client = new WebClient();
                    var data = client.DownloadString(repoMaster);
                    var repos = JsonConvert.DeserializeObject<List<string>>(data);

                    if (_fetch != startedFetch) return;

                    PluginLog.LogInformation($"Fetched {repos.Count} repositories from {repoMaster}");

                    foreach (var url in repos)
                        FetchRepoPluginsAsync(url);
                }
                catch { PluginLog.LogError($"Failed loading repositories from {repoMaster}"); }
            });
        }

        public static void FetchRepoPluginsAsync(string url)
        {
            lock (fetchedRepos)
            {
                if (!fetchedRepos.Add(url))
                {
                    PluginLog.LogError($"{url} has already been fetched");
                    return;
                }
            }

            PluginLog.LogInformation($"Fetching plugins from {url}");

            var startedFetch = _fetch;
            Task.Run(() =>
            {
                try
                {
                    using var client = new WebClient();
                    var data = client.DownloadString(url);
                    var plugins = JArray.Parse(data);
                    var list = (from plugin in plugins
                        where (int)plugin["DalamudApiLevel"] == currentAPILevel
                        select ((string)plugin["Name"], (string)plugin["Description"], (string)plugin["RepoUrl"]))
                        .ToList();

                    if (list.Count == 0)
                    {
                        PluginLog.LogError($"{url} contains no usable plugins!");
                        return;
                    }

                    lock (repoList)
                    {
                        if (_fetch != startedFetch) return;

                        sortList = 60;
                        repoList.Add((url, list));
                    }
                }
                catch { PluginLog.LogError($"Failed loading plugins from {url}"); }
            });
        }

        public static bool GetRepoEnabled(string url)
        {
            var i = DalamudRepoSettings.FindIndex(x => x.Url == url);
            return i >= 0 && DalamudRepoSettings[i].IsEnabled;
        }

        public static void ReloadPluginMasters() => _pluginReload?.Invoke(_dalamudPluginRepository, null);

        public static void SaveDalamudConfig() => _configSave?.Invoke(_dalamudConfig, null);

        [Command("/xlrepos")]
        [HelpMessage("Opens the repository browser.")]
        private void OnXLRepos(string command, string argument)
        {
            PluginUI.isVisible ^= true;
            PluginUI.openSettings = false;
        }

        public static void PrintEcho(string message) => Interface.Framework.Gui.Chat.Print($"[DalamudRepoBrowser] {message}");
        public static void PrintError(string message) => Interface.Framework.Gui.Chat.PrintError($"[DalamudRepoBrowser] {message}");

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            commandManager.Dispose();

            Interface.UiBuilder.OnBuildUi -= PluginUI.Draw;

            Interface.SavePluginConfig(Config);
            Interface.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
