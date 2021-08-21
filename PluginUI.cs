using System;
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
        public static bool openSettings = false;
        private static bool _firstOpen = true;

        public static bool AddHeaderIcon(string id, string icon)
        {
            if (ImGui.IsWindowCollapsed()) return false;

            var scale = ImGuiHelpers.GlobalScale;
            var prevCursorPos = ImGui.GetCursorPos();
            var buttonSize = new Vector2(20 * scale);
            var buttonPos = new Vector2(ImGui.GetWindowWidth() - buttonSize.X - 17 * scale - ImGui.GetStyle().FramePadding.X * 2, 2);
            ImGui.SetCursorPos(buttonPos);
            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRectFullScreen();

            var pressed = false;
            ImGui.InvisibleButton(id, buttonSize);
            var itemMin = ImGui.GetItemRectMin();
            var itemMax = ImGui.GetItemRectMax();
            var halfSize = ImGui.GetItemRectSize() / 2;
            var center = itemMin + halfSize;
            if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(itemMin, itemMax, false))
            {
                ImGui.GetWindowDrawList().AddCircleFilled(center, halfSize.X, ImGui.GetColorU32(ImGui.IsMouseDown(ImGuiMouseButton.Left) ? ImGuiCol.ButtonActive : ImGuiCol.ButtonHovered));
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    pressed = true;
            }

            ImGui.SetCursorPos(buttonPos);
            ImGui.PushFont(UiBuilder.IconFont);
            drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), itemMin + halfSize - ImGui.CalcTextSize(icon) / 2 + Vector2.One, 0xffffffff, icon);
            ImGui.PopFont();

            ImGui.PopClipRect();
            ImGui.SetCursorPos(prevCursorPos);

            return pressed;
        }

        public static void Draw()
        {
            if (DalamudRepoBrowser.sortList > 0 && --DalamudRepoBrowser.sortList <= 0)
                DalamudRepoBrowser.repoList = DalamudRepoBrowser.repoList.OrderBy(x => x.url).ToList();

            if (!isVisible) return;

            if (_firstOpen)
            {
                DalamudRepoBrowser.FetchRepoMasters();
                _firstOpen = false;
            }

            ImGui.SetNextWindowSize(new Vector2(830, 570) * ImGuiHelpers.GlobalScale);
            ImGui.Begin("Repository Browser", ref isVisible, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);

            ImGui.SetWindowFontScale(0.85f);
            if (AddHeaderIcon("RefreshRepoMaster", FontAwesomeIcon.SyncAlt.ToIconString()))
                DalamudRepoBrowser.FetchRepoMasters();
            ImGui.SetWindowFontScale(1);

            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button(FontAwesomeIcon.Wrench.ToIconString()))
                openSettings ^= true;
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "DO NOT INSTALL FROM REPOSITORIES YOU DO NOT TRUST.");

            if (openSettings)
            {
                var height = ImGui.GetFontSize() * Math.Min(DalamudRepoBrowser.Config.RepoMasters.Split('\n').Length + 1, 7) + ImGui.GetStyle().FramePadding.Y * 2;
                if (ImGui.InputTextMultiline("##RepoMasters", ref DalamudRepoBrowser.Config.RepoMasters, 65535, new Vector2(-1, height)))
                {
                    if (string.IsNullOrWhiteSpace(DalamudRepoBrowser.Config.RepoMasters))
                        DalamudRepoBrowser.Config.RepoMasters = Configuration.DefaultRepoMaster;
                    DalamudRepoBrowser.Config.Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("A list of repomaster.json to fetch, separated by newlines. Make sure to click the refresh button in the corner after editing this." +
                        "\nIf needed, erasing everything from this box will restore this to the default.");
            }

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

                            if (hasRepo && ImGui.IsMouseReleased(ImGuiMouseButton.Left) && repo.StartsWith(@"http"))
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
