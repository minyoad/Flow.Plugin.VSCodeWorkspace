// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Flow.Plugin.VSCodeWorkspaces
{
    using Flow.Launcher.Plugin;
    using Properties;
    using RemoteMachinesHelper;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Windows.Controls;
    using VSCodeHelper;
    using WorkspacesHelper;

    public class Main : IPlugin, IPluginI18n, ISettingProvider, IContextMenu
    {
        internal static PluginInitContext Context { get; private set; }

        private static Settings _settings;

        public string Name => GetTranslatedPluginTitle();

        public string Description => GetTranslatedPluginDescription();

        private VSCodeInstance _defaultInstance;

        private readonly VSCodeWorkspacesApi _workspacesApi = new();

        private readonly VSCodeRemoteMachinesApi _machinesApi = new();

        public List<Result> Query(Query query)
        {
            var results = new List<Result>();
            var workspaces = new List<VsCodeWorkspace>();

            // User defined extra workspaces
            if (_defaultInstance != null)
            {
                workspaces.AddRange(_settings.CustomWorkspaces.Select(uri =>
                    VSCodeWorkspacesApi.ParseVSCodeUri(uri, _defaultInstance)));
            }

            // Search opened workspaces
            if (_settings.DiscoverWorkspaces)
            {
                workspaces.AddRange(_workspacesApi.Workspaces);
            }

            // Simple de-duplication
            results.AddRange(workspaces.Distinct()
                .Select(CreateWorkspaceResult)
            );

            // Search opened remote machines
            if (_settings.DiscoverMachines)
            {
                _machinesApi.Machines.ForEach(a =>
                {
                    var title = $"{a.Host}";

                    if (!string.IsNullOrEmpty(a.User) && !string.IsNullOrEmpty(a.HostName))
                    {
                        title += $" [{a.User}@{a.HostName}]";
                    }

                    var tooltip = Resources.SSHRemoteMachine;

                    results.Add(new Result
                    {
                        Title = title,
                        SubTitle = Resources.SSHRemoteMachine,
                        Icon = a.VSCodeInstance.RemoteIcon,
                        TitleToolTip = tooltip,
                        Action = c =>
                        {
                            bool hide;
                            try
                            {
                                var process = new ProcessStartInfo
                                {
                                    FileName = a.VSCodeInstance.ExecutablePath,
                                    UseShellExecute = true,
                                    Arguments =
                                        $"--new-window --enable-proposed-api ms-vscode-remote.remote-ssh --remote ssh-remote+{((char)34) + a.Host + ((char)34)}",
                                    WindowStyle = ProcessWindowStyle.Hidden,
                                };
                                Process.Start(process);

                                hide = true;
                            }
                            catch (Win32Exception)
                            {
                                var name = $"{Context.CurrentPluginMetadata.Name}";
                                string msg = Resources.OpenFail;
                                Context.API.ShowMsg(name, msg, string.Empty);
                                hide = false;
                            }

                            return hide;
                        },
                        ContextData = a,
                    });
                });
            }

            if (query.ActionKeyword == string.Empty ||
                (query.ActionKeyword != string.Empty && query.Search != string.Empty))
            {
                var searchTerms = query.Search.ToLowerInvariant()
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                results = results.Where(r =>
                {
                    var title = r.Title.ToLowerInvariant();
                    var subTitle = r.SubTitle?.ToLowerInvariant() ?? string.Empty;
                    // 检查所有搜索词是否都在标题或子标题中出现（任意位置）
                    bool allTermsMatch = searchTerms.All(term => 
                        title.Contains(term) || subTitle.Contains(term));
                    if (allTermsMatch)
                    {
                        // 如果包含所有搜索词，计算模糊搜索分数（优先用标题计算）
                        var fuzzyScore = Context.API.FuzzySearch(query.Search, r.Title).Score;
                        // 如果模糊搜索分数为0但确实包含所有搜索词，给一个基础分数
                        r.Score = fuzzyScore > 0 ? fuzzyScore : 50;
                        return true;
                    }
                    // 再尝试模糊搜索，处理拼写错误等情况
                    var score = Context.API.FuzzySearch(query.Search, r.Title).Score;
                    r.Score = score;
                    return score > 0;
                }).ToList();
            }


            return results;
        }

        private static Result CreateWorkspaceResult(VsCodeWorkspace ws)
        {
            var title = $"{ws.FolderName}";
            var typeWorkspace = ws.WorkspaceTypeToString();

            if (ws.WorkspaceLocation != WorkspaceLocation.Local)
            {
                title = ws.Label != null
                    ? $"{ws.Label}"
                    : $"{title}{(ws.ExtraInfo != null ? $" - {ws.ExtraInfo}" : string.Empty)} ({typeWorkspace})";
            }

            var tooltip =
                $"{Resources.Workspace}{(ws.WorkspaceLocation != WorkspaceLocation.Local ? $" {Resources.In} {typeWorkspace}" : string.Empty)}: {SystemPath.RealPath(ws.RelativePath)}";

            return new Result
            {
                Title = title,
                SubTitle = tooltip,
                Icon = ws.VSCodeInstance.WorkspaceIcon,
                TitleToolTip = tooltip,
                Action = c =>
                {
                    try
                    {
                        var modifierKeys = c.SpecialKeyState.ToModifierKeys();
                        if (modifierKeys == System.Windows.Input.ModifierKeys.Control)
                        {
                            Context.API.OpenDirectory(SystemPath.RealPath(ws.RelativePath));
                            return true;
                        }

                        var process = new ProcessStartInfo
                        {
                            FileName = ws.VSCodeInstance.ExecutablePath,
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                        };

                        process.ArgumentList.Add(ws.WorkspaceType == WorkspaceType.Workspace
                            ? "--file-uri"
                            : "--folder-uri");

                        process.ArgumentList.Add(ws.Path);

                        Process.Start(process);
                        return true;
                    }
                    catch (Win32Exception)
                    {
                        var name = $"{Context.CurrentPluginMetadata.Name}";
                        string msg = Resources.OpenFail;
                        Context.API.ShowMsg(name, msg, string.Empty);
                    }

                    return false;
                },
                ContextData = ws,
            };
        }

        public void Init(PluginInitContext context)
        {
            Context = context;
            _settings = context.API.LoadSettingJsonStorage<Settings>();

            VSCodeInstances.LoadVSCodeInstances();

            // Prefer stable version, or the first one we got
            _defaultInstance = VSCodeInstances.Instances.Find(e => e.VSCodeVersion == VSCodeVersion.Stable) ??
                              VSCodeInstances.Instances.FirstOrDefault();
        }

        public Control CreateSettingPanel() => new SettingsView(Context, _settings);

        public void OnCultureInfoChanged(CultureInfo newCulture)
        {
            Resources.Culture = newCulture;
        }

        public string GetTranslatedPluginTitle()
        {
            return Resources.PluginTitle;
        }

        public string GetTranslatedPluginDescription()
        {
            return Resources.PluginDescription;
        }

        public List<Result> LoadContextMenus(Result selectedResult)
        {
            List<Result> results = new();
            if (selectedResult.ContextData is VsCodeWorkspace ws && ws.WorkspaceLocation == WorkspaceLocation.Local)
            {
                results.Add(new Result
                {
                    Title = Resources.OpenFolder,
                    SubTitle = Resources.OpenFolderSubTitle,
                    Icon = ws.VSCodeInstance.WorkspaceIcon,
                    TitleToolTip = Resources.OpenFolderSubTitle,
                    Action = c =>
                    {
                        Context.API.OpenDirectory(SystemPath.RealPath(ws.RelativePath));
                        return true;
                    },
                    ContextData = ws,
                });
            }

            return results;
        }
    }
}