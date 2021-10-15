using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Logging;
using Dalamud.Plugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DalamudRepoBrowser
{
    public class DalamudRepoBrowser : IDalamudPlugin
    {
        public string Name => "DalamudRepoBrowser";
        public static DalamudRepoBrowser Plugin { get; private set; }
        public static Configuration Config { get; private set; }

        public static int currentAPILevel;
        private static PropertyInfo dalamudRepoSettingsProperty;
        private static IEnumerable dalamudRepoSettings;
        public static IEnumerable DalamudRepoSettings
        {
            get => dalamudRepoSettings ??= (IEnumerable)dalamudRepoSettingsProperty?.GetValue(dalamudConfig);
            set => dalamudRepoSettings = value;
        }

        public static List<(string url, List<(string name, string description, string repo, bool valid)> plugins)> repoList = new();
        public static HashSet<string> fetchedRepos = new();
        public static int sortList;

        private static int fetch = 0;

        private static Assembly dalamudAssembly;
        private static Type dalamudServiceType;
        private static Type thirdPartyRepoSettingsType;
        private static object dalamudPluginManager;
        private static object dalamudConfig;
        private static MethodInfo pluginReload;
        private static MethodInfo configSave;

        public DalamudRepoBrowser(DalamudPluginInterface pluginInterface)
        {
            Plugin = this;
            DalamudApi.Initialize(this, pluginInterface);

            Config = (Configuration)DalamudApi.PluginInterface.GetPluginConfig() ?? new();
            Config.Initialize();

            try
            {
                ReflectRepos();
                DalamudApi.PluginInterface.UiBuilder.Draw += PluginUI.Draw;
            }
            catch (Exception e) { PluginLog.LogError(e, "Failed to load."); }
        }


        private static object GetService(string type)
        {
            var getType = dalamudAssembly.GetType(type);
            if (getType == null) return null;
            var getService = dalamudServiceType.MakeGenericType(getType).GetMethod("Get");
            return getService?.Invoke(null, null);
        }

        private static void ReflectRepos()
        {
            dalamudAssembly = Assembly.GetAssembly(typeof(DalamudPluginInterface));
            dalamudServiceType = dalamudAssembly?.GetType("Dalamud.Service`1");
            thirdPartyRepoSettingsType = dalamudAssembly?.GetType("Dalamud.Configuration.ThirdPartyRepoSettings");
            if (dalamudServiceType == null || thirdPartyRepoSettingsType == null) throw new NullReferenceException();

            dalamudPluginManager = GetService("Dalamud.Plugin.Internal.PluginManager");
            dalamudConfig = GetService("Dalamud.Configuration.Internal.DalamudConfiguration");

            currentAPILevel = (int)dalamudPluginManager?.GetType()
                .GetField("DalamudApiLevel", BindingFlags.Static | BindingFlags.Public)
                ?.GetValue(dalamudPluginManager)!;

            dalamudRepoSettingsProperty = dalamudConfig?.GetType()
                .GetProperty("ThirdRepoList", BindingFlags.Instance | BindingFlags.Public);

            pluginReload = dalamudPluginManager?.GetType()
                .GetMethod("SetPluginReposFromConfig", BindingFlags.Instance | BindingFlags.Public);

            configSave = dalamudConfig?.GetType()
                .GetMethod("Save", BindingFlags.Instance | BindingFlags.Public);
        }

        public static void AddRepo(string url)
        {
            var add = DalamudRepoSettings?.GetType()
                .GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
            if (add == null) return;

            var obj = Activator.CreateInstance(thirdPartyRepoSettingsType);
            if (obj == null) return;

            _ = new RepoSettings(obj)
            {
                Url = url,
                IsEnabled = true
            };

            add.Invoke(DalamudRepoSettings, new[] { obj });
            SaveDalamudConfig();
            ReloadPluginMasters();
        }

        public static RepoSettings GetRepoSettings(string url) => (from object obj in DalamudRepoSettings select new RepoSettings(obj)).FirstOrDefault(repoSettings => repoSettings.Url == url);

        public static void ToggleRepo(string url)
        {
            try
            {
                var repo = GetRepoSettings(url);
                repo.IsEnabled ^= true;
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
                fetch++;
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

            var startedFetch = fetch;
            Task.Run(() =>
            {
                try
                {
                    using var client = new WebClient();
                    var data = client.DownloadString(repoMaster);
                    var repos = JsonConvert.DeserializeObject<List<string>>(data);

                    if (fetch != startedFetch) return;

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

            var startedFetch = fetch;
            Task.Run(() =>
            {
                try
                {
                    using var client = new WebClient();
                    var data = client.DownloadString(url);
                    var plugins = JArray.Parse(data);
                    var list = (from plugin in plugins
                        select ((string)plugin["Name"], (string)plugin["Description"], (string)plugin["RepoUrl"], (int)plugin["DalamudApiLevel"] == currentAPILevel))
                        .ToList();

                    if (list.Count == 0)
                    {
                        PluginLog.LogInformation($"{url} contains no usable plugins!");
                        return;
                    }

                    lock (repoList)
                    {
                        if (fetch != startedFetch) return;

                        sortList = 60;
                        repoList.Add((url, list));
                    }
                }
                catch { PluginLog.LogError($"Failed loading plugins from {url}"); }
            });
        }

        public static bool GetRepoEnabled(string url)
        {
            var repo = GetRepoSettings(url);
            return repo is { IsEnabled: true };
        }

        public static void ReloadPluginMasters() => pluginReload?.Invoke(dalamudPluginManager, new object[] { true });

        public static void SaveDalamudConfig() => configSave?.Invoke(dalamudConfig, null);

        [Command("/xlrepos")]
        [HelpMessage("Opens the repository browser.")]
        private void OnXLRepos(string command, string argument)
        {
            PluginUI.isVisible ^= true;
            PluginUI.openSettings = false;
        }

        public static void PrintEcho(string message) => DalamudApi.ChatGui.Print($"[DalamudRepoBrowser] {message}");
        public static void PrintError(string message) => DalamudApi.ChatGui.PrintError($"[DalamudRepoBrowser] {message}");

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            Config.Save();
            DalamudApi.PluginInterface.UiBuilder.Draw -= PluginUI.Draw;
            DalamudApi.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
