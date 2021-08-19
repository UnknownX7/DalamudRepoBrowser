using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using ImGuiNET;

namespace DalamudRepoBrowser
{
    public static class PluginUI
    {
        public static bool isVisible = false;

        public static void Draw()
        {
            if (DalamudRepoBrowser.sortList > 0 && --DalamudRepoBrowser.sortList <= 0)
                DalamudRepoBrowser.repoList = DalamudRepoBrowser.repoList.OrderBy(x => x.url).ToList();

            if (!isVisible) return;

            ImGui.SetNextWindowSize(new Vector2(830, 570) * ImGuiHelpers.GlobalScale);
            ImGui.Begin("Repository Browser", ref isVisible, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);

            ImGui.TextColored(new Vector4(1, 0, 0, 1), "DO NOT INSTALL FROM REPOSITORIES YOU DO NOT TRUST.");

            ImGui.Separator();

            ImGui.BeginChild("RepoList");
            foreach (var (url, plugins) in DalamudRepoBrowser.repoList)
            {
                var enabled = DalamudRepoBrowser.GetRepoEnabled(url);
                if (ImGui.Button($"Copy Link##{url}"))
                    ImGui.SetClipboardText(url);
                ImGui.SameLine();
                if (ImGui.Checkbox($"{url}##Enabled", ref enabled))
                    DalamudRepoBrowser.ToggleRepo(url);

                var indent = 32 * ImGuiHelpers.GlobalScale;
                ImGui.Indent(indent);
                for (int i = 0; i < plugins.Count; i++)
                {
                    var (name, description) = plugins[i];
                    ImGui.Text(name);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(description);
                    if (i % 5 != 4)
                        ImGui.SameLine();
                }
                ImGui.Unindent(indent);

                ImGui.Spacing();
                ImGui.Separator();
            }
            ImGui.EndChild();

            ImGui.End();
        }
    }
}
