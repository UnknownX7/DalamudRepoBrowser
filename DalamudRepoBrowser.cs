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
[assembly: AssemblyVersion("1.0.0.0")]

namespace DalamudRepoBrowser
{
    public class DalamudRepoBrowser : IDalamudPlugin
    {
        public string Name => "DalamudRepoBrowser";
        public static DalamudPluginInterface Interface { get; private set; }
        private PluginCommandManager commandManager;
        public static Configuration Config { get; private set; }
        public static DalamudRepoBrowser Plugin { get; private set; }

        public const string repoMaster = @"https://raw.githubusercontent.com/UnknownX7/DalamudRepoBrowser/master/repomaster.json";
        public static int currentAPILevel;
        public static List<ThirdRepoSetting> dalamudRepoSettings;
        public static List<(string url, List<(string name, string description)> plugins)> repoList = new();
        public static int sortList;

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
                FetchRepoListAsync();
                Interface.UiBuilder.OnBuildUi += PluginUI.Draw;
            }
            catch (Exception e) { PluginLog.LogError(e, "Failed to load."); }
        }

        private static void ReflectRepos()
        {
            var dalamud = (Dalamud.Dalamud)Interface.GetType()
                .GetField("dalamud", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Interface);

            var pluginManager = dalamud?.GetType()
                .GetProperty("PluginManager", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(dalamud);

            currentAPILevel = (int)pluginManager?.GetType()
                .GetField("DalamudApiLevel", BindingFlags.Static | BindingFlags.Public)
                ?.GetValue(pluginManager);

            var config = dalamud?.GetType()
                .GetProperty("Configuration", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(dalamud);

            dalamudRepoSettings = (List<ThirdRepoSetting>)config?.GetType()
                .GetProperty("ThirdRepoList", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(config);
        }

        public static void AddRepo(string url) => dalamudRepoSettings.Add(new ThirdRepoSetting{Url = url, IsEnabled = true});

        public static void ToggleRepo(string url)
        {
            try
            {
                var repo = dalamudRepoSettings.First(x => x.Url == url);
                repo.IsEnabled ^= true;
            }
            catch
            {
                AddRepo(url);
            }
        }

        public static void FetchRepoListAsync()
        {
            repoList.Clear();

            PluginLog.LogInformation($"Fetching repositories from {repoMaster}");

            Task.Run(() =>
            {
                try
                {
                    using var client = new WebClient();
                    var data = client.DownloadString(repoMaster);
                    var repos = JsonConvert.DeserializeObject<List<string>>(data);

                    PluginLog.LogInformation($"Fetched {repos.Count} repositories.");

                    foreach (var url in repos)
                        FetchRepoPluginsAsync(url);
                }
                catch { PluginLog.LogError("Failed loading repomaster!"); }
            });
        }

        public static void FetchRepoPluginsAsync(string url)
        {
            PluginLog.LogInformation($"Fetching plugin list from {url}");

            Task.Run(() =>
            {
                try
                {
                    using var client = new WebClient();
                    var data = client.DownloadString(url);
                    var plugins = JArray.Parse(data);
                    var list = (from plugin in plugins where (int)plugin["DalamudApiLevel"] == currentAPILevel select ((string)plugin["Name"], (string)plugin["Description"])).ToList();
                    if (list.Count == 0)
                    {
                        PluginLog.LogError($"{url} contains no usable plugins!");
                        return;
                    }

                    sortList = 60;
                    repoList.Add((url, list));
                }
                catch { PluginLog.LogError($"Failed loading from {url}"); }
            });
        }

        public static bool GetRepoEnabled(string url)
        {
            var i = dalamudRepoSettings.FindIndex(x => x.Url == url);
            return i >= 0 && dalamudRepoSettings[i].IsEnabled;
        }

        [Command("/xlrepos")]
        [HelpMessage("Opens the repository browser.")]
        private void OnXLRepos(string command, string argument) => PluginUI.isVisible ^= true;

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
