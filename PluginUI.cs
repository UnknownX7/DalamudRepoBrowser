using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using ImGuiNET;

namespace DalamudRepoBrowser
{
    public static class PluginUI
    {
        public static bool isVisible = false;
        public static bool openSettings = false;
        private static bool firstOpen = true;
        private static HashSet<RepoInfo> enabledRepos = new();
        private static HashSet<RepoInfo> search = new();
        private static string searchText = string.Empty;
        private static uint filteredCount = 0;

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
            drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), itemMin + halfSize - ImGui.CalcTextSize(icon) / 2 + Vector2.One, 0xFFFFFFFF, icon);
            ImGui.PopFont();

            ImGui.PopClipRect();
            ImGui.SetCursorPos(prevCursorPos);

            return pressed;
        }

        public static void Draw()
        {
            DalamudRepoBrowser.DalamudRepoSettings = null;

            if (DalamudRepoBrowser.sortList > 0 && --DalamudRepoBrowser.sortList <= 0)
            {
                DalamudRepoBrowser.repoList = DalamudRepoBrowser.Config.RepoSort switch
                {
                    0 => DalamudRepoBrowser.repoList.OrderByDescending(r => r.stars).ToList(),
                    1 => DalamudRepoBrowser.repoList.OrderBy(r => r.owner).ToList(),
                    2 => DalamudRepoBrowser.repoList.OrderBy(r => r.url).ToList(),
                    3 => DalamudRepoBrowser.repoList.OrderByDescending(r => r.plugins.Count(i => DalamudRepoBrowser.Config.ShowOutdated > 0 || i.apiLevel == DalamudRepoBrowser.currentAPILevel)).ToList(),
                    _ => DalamudRepoBrowser.repoList
                };

                enabledRepos = DalamudRepoBrowser.repoList.Where(r => DalamudRepoBrowser.GetRepoEnabled(r.url) || DalamudRepoBrowser.GetRepoEnabled(r.rawURL)).ToHashSet();

                DalamudRepoBrowser.repoList.ForEach(r => DalamudRepoBrowser.Config.SeenRepos.Add(r.url));
                DalamudRepoBrowser.repoList = DalamudRepoBrowser.repoList.OrderBy(r => DalamudRepoBrowser.prevSeenRepos.Contains(r.url)).ToList();

                DalamudRepoBrowser.Config.Save();
            }

            if (!isVisible) return;

            if (firstOpen)
            {
                DalamudRepoBrowser.FetchRepoMasters();
                firstOpen = false;
            }

            ImGui.SetNextWindowSizeConstraints(new Vector2(830, 570) * ImGuiHelpers.GlobalScale, new Vector2(9999));
            ImGui.Begin("Repository Browser", ref isVisible, ImGuiWindowFlags.NoCollapse);

            ImGui.SetWindowFontScale(0.85f);
            if (AddHeaderIcon("RefreshRepoMaster", FontAwesomeIcon.SyncAlt.ToIconString()))
                DalamudRepoBrowser.FetchRepoMasters();
            ImGui.SetWindowFontScale(1);

            ImGui.PushFont(UiBuilder.IconFont);

            if (ImGui.Button(FontAwesomeIcon.Wrench.ToIconString()))
                openSettings ^= true;

            ImGui.SameLine();

            if (ImGui.Button(FontAwesomeIcon.Globe.ToIconString()))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = @"https://xivplugins.com",
                    UseShellExecute = true
                });
            }

            ImGui.PopFont();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Source");

            ImGui.SameLine();

            ImGui.TextColored(new Vector4(1, 0, 0, 1), "DO NOT INSTALL FROM REPOSITORIES YOU DO NOT TRUST.");

            var inputWidth = ImGui.GetWindowContentRegionMax().X / 4;
            ImGui.SameLine(inputWidth * 3);
            ImGui.SetNextItemWidth(inputWidth);
            if (ImGui.InputTextWithHint("##Search", $"Search {filteredCount} / {DalamudRepoBrowser.repoList.Count} Repos", ref searchText, 64))
            {
                lock (DalamudRepoBrowser.repoList)
                {
                    search = DalamudRepoBrowser.repoList.Where(r
                        => r.url.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
                           || r.fullName.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
                           || r.plugins.Any(plugin => plugin.name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
                                || plugin.punchline.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
                                || plugin.description.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
                                || plugin.tags.Any(s => s.Equals(searchText, StringComparison.CurrentCultureIgnoreCase))
                                || plugin.categoryTags.Any(s => s.Equals(searchText, StringComparison.CurrentCultureIgnoreCase)))).ToHashSet();
                }
            }

            if (openSettings)
            {
                var save = false;

                ImGui.Columns(2, null, false);

                ImGui.TextUnformatted("\tDisplay Outdated");
                save |= ImGui.RadioButton("None", ref DalamudRepoBrowser.Config.ShowOutdated, 0);
                ImGui.SameLine();
                save |= ImGui.RadioButton("Plugins", ref DalamudRepoBrowser.Config.ShowOutdated, 1);
                ImGui.SameLine();
                save |= ImGui.RadioButton("Repos", ref DalamudRepoBrowser.Config.ShowOutdated, 2);

                ImGui.Spacing();

                ImGui.TextUnformatted("\tSort");
                save |= ImGui.RadioButton("Stars", ref DalamudRepoBrowser.Config.RepoSort, 0);
                ImGui.SameLine();
                save |= ImGui.RadioButton("Owner", ref DalamudRepoBrowser.Config.RepoSort, 1);
                ImGui.SameLine();
                save |= ImGui.RadioButton("URL", ref DalamudRepoBrowser.Config.RepoSort, 2);
                ImGui.SameLine();
                save |= ImGui.RadioButton("# Plugins", ref DalamudRepoBrowser.Config.RepoSort, 3);

                ImGui.SetNextItemWidth(ImGui.GetWindowWidth() / 5);
                save |= ImGui.SliderInt("Minimum Stars", ref DalamudRepoBrowser.Config.MinStars, 0, 20);

                ImGui.NextColumn();
                ImGui.TextUnformatted("");
                save |= ImGui.Checkbox("Hide Enabled Repos", ref DalamudRepoBrowser.Config.HideEnabledRepos);
                ImGui.TextUnformatted("");
                save |= ImGui.Checkbox("Hide Branches", ref DalamudRepoBrowser.Config.HideBranches);

                ImGui.SetNextItemWidth(ImGui.GetWindowWidth() / 5);
                save |= ImGui.SliderInt(DalamudRepoBrowser.Config.MaxPlugins >= 50 ? "∞ Plugins###MaxPlugins" : "Maximum Plugins###MaxPlugins", ref DalamudRepoBrowser.Config.MaxPlugins, 20, 50);

                ImGui.Columns(1);

                if (save)
                    DalamudRepoBrowser.sortList = 1;
            }

            ImGui.Separator();

            ImGui.BeginChild("RepoList");
            var indent = 32 * ImGuiHelpers.GlobalScale;
            var spacing = indent / 6;
            var padding = indent / 8;
            lock (DalamudRepoBrowser.repoList)
            {
                var doSearch = !string.IsNullOrEmpty(searchText);
                filteredCount = 0;
                foreach (var repoInfo in DalamudRepoBrowser.repoList)
                {
                    if (DalamudRepoBrowser.Config.HideBranches && !repoInfo.isDefaultBranch
                        || DalamudRepoBrowser.Config.ShowOutdated < 2 && repoInfo.apiLevel != DalamudRepoBrowser.currentAPILevel
                        || DalamudRepoBrowser.Config.MinStars > repoInfo.stars
                        || DalamudRepoBrowser.Config.MaxPlugins < 50 && DalamudRepoBrowser.Config.MaxPlugins < repoInfo.plugins.Count
                        || doSearch && !search.Contains(repoInfo)) continue;

                    var enabled = DalamudRepoBrowser.GetRepoEnabled(repoInfo.url) || DalamudRepoBrowser.GetRepoEnabled(repoInfo.rawURL);
                    if (enabled && DalamudRepoBrowser.Config.HideEnabledRepos && enabledRepos.Contains(repoInfo)) continue;

                    filteredCount++;

                    var seen = DalamudRepoBrowser.prevSeenRepos.Contains(repoInfo.url);

                    if (ImGui.Button($"Copy Link##{repoInfo.url}"))
                        ImGui.SetClipboardText(repoInfo.url);

                    ImGui.SameLine();

                    ImGui.PushFont(UiBuilder.IconFont);

                    if (!string.IsNullOrEmpty(repoInfo.gitRepoURL) && ImGui.Button($"{FontAwesomeIcon.Globe.ToIconString()}##{repoInfo.url}"))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = repoInfo.gitRepoURL,
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }

                    ImGui.PopFont();

                    if (!seen)
                    {
                        ImGui.GetWindowDrawList().AddRectFilledMultiColor(ImGui.GetItemRectMin(),
                            new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowSize().X, ImGui.GetItemRectMax().Y + ImGui.GetItemRectSize().Y),
                            0, ImGui.GetColorU32(ImGuiCol.TitleBgActive) | 0xFF000000, 0, 0);
                    }

                    ImGui.SameLine();

                    if (ImGui.Checkbox($"{repoInfo.url}##Enabled", ref enabled))
                        DalamudRepoBrowser.ToggleRepo(DalamudRepoBrowser.HasRepo(repoInfo.rawURL) ? repoInfo.rawURL : repoInfo.url);

                    ImGui.TextUnformatted($"Owner: {repoInfo.owner}");
                    ImGui.SameLine();
                    if (!repoInfo.isDefaultBranch && !string.IsNullOrEmpty(repoInfo.branchName))
                        ImGui.TextUnformatted($"Branch: {repoInfo.branchName}");
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), $"{repoInfo.stars} ★");

                    ImGui.Indent(indent);
                    ImGui.SetWindowFontScale(0.9f);

                    var count = 0;
                    foreach (var plugin in repoInfo.plugins)
                    {
                        var valid = plugin.apiLevel == DalamudRepoBrowser.currentAPILevel;
                        if (DalamudRepoBrowser.Config.ShowOutdated < 1 && !valid) continue;

                        // This is dumb, ImGui is dumb
                        var prevCursor = ImGui.GetCursorPos();
                        ImGui.Dummy(ImGui.CalcTextSize(plugin.name));
                        var textMin = ImGui.GetItemRectMin();
                        var textMax = ImGui.GetItemRectMax();
                        textMin.X -= padding;
                        textMax.X += padding;
                        var drawList = ImGui.GetWindowDrawList();
                        drawList.AddRectFilled(textMin, textMax, (uint)(valid ? 0x20FFFFFF : 0x200000FF), ImGui.GetStyle().FrameRounding);
                        ImGui.SetCursorPos(prevCursor);

                        if (!valid)
                        {
                            const uint color = 0xA00000FF;
                            var thickness = 2 * ImGuiHelpers.GlobalScale;
                            drawList.AddLine(textMin, textMax, color, thickness);
                            drawList.AddLine(new Vector2(textMin.X, textMax.Y), new Vector2(textMax.X, textMin.Y), color, thickness);
                        }

                        ImGui.Text(plugin.name);
                        if (ImGui.IsItemHovered())
                        {
                            var hasRepo = !string.IsNullOrEmpty(plugin.repoURL);
                            var hasPunchline = !string.IsNullOrEmpty(plugin.punchline);
                            var hasDescription = !string.IsNullOrEmpty(plugin.description);
                            var tooltip = (hasRepo ? plugin.repoURL : string.Empty);

                            if (hasPunchline)
                                tooltip += hasRepo ? $"\n-\n{plugin.punchline}" : plugin.punchline;

                            if (hasDescription)
                                tooltip += hasRepo || hasPunchline ? $"\n-\n{plugin.description}" : plugin.description;

                            if (!string.IsNullOrEmpty(tooltip))
                                ImGui.SetTooltip(tooltip);

                            if (hasRepo && ImGui.IsMouseReleased(ImGuiMouseButton.Left) && plugin.repoURL.StartsWith(@"http"))
                            {
                                try
                                {
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = plugin.repoURL,
                                        UseShellExecute = true
                                    });
                                }
                                catch { }
                            }
                        }

                        if (++count % 6 == 5) continue;
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
