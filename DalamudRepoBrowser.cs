using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Logging;
using Dalamud.Plugin;
using Newtonsoft.Json.Linq;

namespace DalamudRepoBrowser
{
    public struct RepoInfo
    {
        public readonly string owner;
        public readonly string fullName;
        public readonly long lastUpdated;
        public readonly uint stars;
        public readonly byte apiLevel;
        public readonly string url;

        public RepoInfo(JToken json)
        {
            owner = (string)json["owner"] ?? string.Empty;
            fullName = (string)json["fullName"] ?? string.Empty;
            lastUpdated = (long?)json["lastUpdated"] ?? 0;
            stars = (uint?)json["stargazersCount"] ?? 0;
            apiLevel = (byte?)json["dalamudApiLevel"] ?? 0;
            url = (string)json["pluginMasterUrl"] ?? string.Empty;
        }
    }

    public struct PluginInfo
    {
        public readonly string name;
        public readonly string description;
        public readonly string punchline;
        public readonly string repoURL;
        public readonly byte apiLevel;
        public readonly List<string> tags;
        public readonly List<string> categoryTags;

        public PluginInfo(JToken json)
        {
            name = (string)json["Name"] ?? string.Empty;
            description = (string)json["Description"] ?? string.Empty;
            punchline = (string)json["Punchline"] ?? string.Empty;
            repoURL = (string)json["RepoUrl"] ?? string.Empty;
            apiLevel = (byte?)json["DalamudApiLevel"] ?? 0;
            tags = new();
            categoryTags = new();

            var tagsArray = (JArray)json["Tags"];
            if (tagsArray != null)
            {
                foreach (var t in tagsArray)
                {
                    var tag = (string)t;
                    if (tag == null) continue;
                    tags.Add(tag);
                }
            }

            var categoryTagsArray = (JArray)json["CategoryTags"];
            if (categoryTagsArray == null) return;
            foreach (var t in categoryTagsArray)
            {
                var tag = (string)t;
                if (tag == null) continue;
                categoryTags.Add(tag);
            }
        }
    }

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

        public static readonly string repoMaster = @"https://api.kalilistic.io/dalamud/repos";
        public static List<(RepoInfo repo, List<PluginInfo> plugins)> repoList = new();
        public static HashSet<string> fetchedRepos = new();
        public static int sortList;
        public static HashSet<string> prevSeenRepos = new();

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
                prevSeenRepos = Config.SeenRepos.ToHashSet();
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
                .GetMethod("SetPluginReposFromConfigAsync", BindingFlags.Instance | BindingFlags.Public);

            configSave = dalamudConfig?.GetType()
                .GetMethod("Save", BindingFlags.Instance | BindingFlags.Public);

            if (dalamudPluginManager == null || dalamudConfig == null || dalamudRepoSettingsProperty == null || pluginReload == null || configSave == null) throw new NullReferenceException();
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

            FetchRepoListAsync(repoMaster);
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
                    var repos = JArray.Parse(data);

                    if (fetch != startedFetch) return;

                    PluginLog.LogInformation($"Fetched {repos.Count} repositories from {repoMaster}");

                    foreach (var info in repos)
                        FetchRepoPluginsAsync(info);
                }
                catch { PluginLog.LogError($"Failed loading repositories from {repoMaster}"); }
            });
        }

        public static void FetchRepoPluginsAsync(JToken json)
        {
            RepoInfo info;

            try
            {
                info = new RepoInfo(json);
            }
            catch
            {
                PluginLog.LogError($"Failed parsing {(string)json["pluginMasterUrl"]}.");
                return;
            }

            lock (fetchedRepos)
            {
                if (!fetchedRepos.Add(info.url))
                {
                    PluginLog.LogError($"{info.url} has already been fetched");
                    return;
                }
            }

            PluginLog.LogInformation($"Fetching plugins from {info.url}");

            var startedFetch = fetch;
            Task.Run(() =>
            {
                try
                {
                    using var client = new WebClient();
                    var data = client.DownloadString(info.url);
                    var plugins = JArray.Parse(data);
                    var list = (from plugin in plugins select new PluginInfo(plugin)).ToList();

                    if (list.Count == 0)
                    {
                        PluginLog.LogInformation($"{info.url} contains no usable plugins!");
                        return;
                    }

                    lock (repoList)
                    {
                        if (fetch != startedFetch) return;

                        sortList = 60;
                        repoList.Add((info, list));
                    }
                }
                catch { PluginLog.LogError($"Failed loading plugins from {info.url}"); }
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
