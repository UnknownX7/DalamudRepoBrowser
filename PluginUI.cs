using System.Diagnostics;
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
            var indent = 32 * ImGuiHelpers.GlobalScale;
            var spacing = indent / 6;
            var padding = indent / 8;
            lock (DalamudRepoBrowser.repoList)
            {
                foreach (var (url, plugins) in DalamudRepoBrowser.repoList)
                {
                    var enabled = DalamudRepoBrowser.GetRepoEnabled(url);
                    if (ImGui.Button($"Copy Link##{url}"))
                        ImGui.SetClipboardText(url);
                    ImGui.SameLine();
                    if (ImGui.Checkbox($"{url}##Enabled", ref enabled))
                        DalamudRepoBrowser.ToggleRepo(url);

                    ImGui.Indent(indent);
                    ImGui.SetWindowFontScale(0.9f);
                    for (int i = 0; i < plugins.Count; i++)
                    {
                        var (name, description, repo) = plugins[i];
                        // This is dumb, ImGui is dumb
                        var prevCursor = ImGui.GetCursorPos();
                        ImGui.Dummy(ImGui.CalcTextSize(name));
                        var textMin = ImGui.GetItemRectMin();
                        var textMax = ImGui.GetItemRectMax();
                        textMin.X -= padding;
                        textMax.X += padding;
                        ImGui.GetWindowDrawList().AddRectFilled(textMin, textMax, 0x20FFFFFF, ImGui.GetStyle().FrameRounding);
                        ImGui.SetCursorPos(prevCursor);
                        ImGui.Text(name);
                        if (ImGui.IsItemHovered())
                        {
                            var hasRepo = !string.IsNullOrEmpty(repo);
                            var hasDescription = !string.IsNullOrEmpty(description);
                            var tooltip = (hasRepo ? repo : string.Empty) + (hasDescription ? (hasRepo ? "\n" + description : description) : string.Empty);
                            if (!string.IsNullOrEmpty(tooltip))
                                ImGui.SetTooltip(tooltip);

                            if (hasRepo && ImGui.IsMouseReleased(ImGuiMouseButton.Left) && repo.StartsWith(@"https://"))
                                Process.Start(repo);
                        }

                        if (i % 6 == 5) continue;
                        ImGui.SameLine();
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spacing);
                    }
                    ImGui.SetWindowFontScale(1);
                    ImGui.Unindent(indent);

                    ImGui.Spacing();
                    ImGui.Separator();
                }
            }
            ImGui.EndChild();

            ImGui.End();
        }
    }
}
