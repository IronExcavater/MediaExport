// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Activity;
using MediaExport.Export;
using MediaExport.Search;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MediaExport.Settings;

/// <summary>The app's preferences, and the state of the tools it depends on.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ToolchainService _toolchain;
    private readonly SettingsStore _settings;
    private readonly IThemeService _themeService;
    private readonly IFolderLauncher _folderLauncher;
    private readonly SearchViewModel _search;
    private readonly ExportViewModel _export;
    private readonly ActivityViewModel _activity;
    private bool _checkedAutoUpdates;

    public SettingsViewModel(
        ToolchainService toolchain,
        SettingsStore settings,
        IThemeService themeService,
        IFolderLauncher folderLauncher,
        SearchViewModel search,
        ExportViewModel export,
        ActivityViewModel activity)
    {
        _toolchain = toolchain;
        _settings = settings;
        _themeService = themeService;
        _folderLauncher = folderLauncher;
        _search = search;
        _export = export;
        _activity = activity;

        // Initialize theme from settings
        ThemeIndex = (int)_settings.Current.Theme;

        ToolInfo[] tools =
        [
            new("yt-dlp", "yt-dlp", "yt-dlp", "--version", new Uri("https://github.com/yt-dlp/yt-dlp#readme"),
                new Uri("https://github.com/yt-dlp/yt-dlp"),
                new Uri("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe")),
            new("ffmpeg", "FFmpeg", "ffmpeg", "-version", new Uri("https://ffmpeg.org"),
                new Uri("https://github.com/FFmpeg/FFmpeg"),
                new Uri("https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip")),
            new("deno", "Deno", "deno", "--version", new Uri("https://deno.land"),
                new Uri("https://github.com/denoland/deno"), 
                new Uri("https://github.com/denoland/deno/releases/download/v2.9.7/deno-x86_64-pc-windows-msvc.zip"))
        ];
        foreach (var tool in tools)
            EnsureToolSettings(tool);
        Tools = tools.Select(tool => new ToolViewModel(tool, toolchain)).ToList();

        _search.RecentSearches.CollectionChanged += (_, _) => NotifyHistoryState();
        _export.RecentlyViewed.CollectionChanged += (_, _) => NotifyHistoryState();

        _activity.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ActivityViewModel.HasFinishedHistory))
                NotifyHistoryState();
        };
    }

    // The Appearance group: the theme.

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    public List<ThemeOption> ThemeOptions { get; } =
    [
        new() { Name = "System", Glyph = "" },
        new() { Name = "Dark", Glyph = "" },
        new() { Name = "Light", Glyph = "" }
    ];

    // Update the theme when the user changes the selection in the UI
    partial void OnThemeIndexChanged(int value)
    {
        // A ComboBox reports -1 while it has nothing selected, which is not a theme.
        if (!Enum.IsDefined((AppTheme)value))
            return;

        var theme = (AppTheme)value;
        _themeService.Apply(theme);
    }

    // The Tools group: what is installed, and keeping it up to date.

    /// <summary>The programs the app depends on, each with whether it is installed.</summary>
    public IReadOnlyList<ToolViewModel> Tools { get; }

    /// <summary>
    /// Whether the tools are kept up to date. Each tool has a setting of its own for this, and the page has one
    /// switch, which is on when all of them are and sets them all.
    /// </summary>
    public bool AutoUpdate
    {
        get => _settings.Current.Tools.Values.All(tool => tool.AutoUpdate);
        set
        {
            foreach (var tool in _settings.Current.Tools.Values)
                tool.AutoUpdate = value;

            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Looks for every tool again, which the page also asks for when it is shown.</summary>
    [RelayCommand]
    public async Task RefreshToolsAsync()
    {
        await Task.WhenAll(Tools.Select(tool => tool.RefreshCommand.ExecuteAsync(null)));
        if (_checkedAutoUpdates)
            return;

        _checkedAutoUpdates = true;
        var updates = Tools.Where(tool =>
            tool.CanUpdate &&
            _settings.Current.Tools.TryGetValue(tool.Id, out var settings) &&
            settings.AutoUpdate);
        foreach (var tool in updates)
            await tool.UpdateCommand.ExecuteAsync(null);
    }

    /// <summary>Opens the folder the app installs tools into, which is made first if nothing is installed yet.</summary>
    [RelayCommand]
    private void OpenToolsFolder() => _folderLauncher.Open(_toolchain.Folder);

    private void EnsureToolSettings(ToolInfo tool)
    {
        if (!_settings.Current.Tools.TryGetValue(tool.Id, out var settings))
        {
            settings = new ToolSettings { Id = tool.Id, AutoUpdate = true };
            _settings.Current.Tools[tool.Id] = settings;
        }

        settings.Name = tool.Name;
        settings.Executable = tool.Executable;
    }

    public bool HasRecentSearches =>
    _settings.Current.LastSearch is not null || _search.RecentSearches.Count > 0;

    public bool HasRecentMedia => _export.RecentlyViewed.Count > 0;
    public bool HasExportHistory => _activity.HasFinishedHistory;
    public bool HasAnyHistory => HasRecentSearches || HasRecentMedia || HasExportHistory;
    public event EventHandler? LastSearchCleared;

    private void NotifyHistoryState()
    {
        OnPropertyChanged(nameof(HasRecentSearches));
        OnPropertyChanged(nameof(HasRecentMedia));
        OnPropertyChanged(nameof(HasExportHistory));
        OnPropertyChanged(nameof(HasAnyHistory));
    }

    [RelayCommand]
    public void ClearAllHistory()
    {
        ClearRecentSearches();
        ClearRecentMedia();
        _activity.ClearFinishedCommand.Execute(null);
        NotifyHistoryState();
    }

    [RelayCommand]
    public void ClearRecentSearches()
    {
        _search.RecentSearches.Clear();
        _settings.Current.LastSearch = null;
        _settings.Save();
        NotifyHistoryState();

        LastSearchCleared?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void ClearRecentMedia()
    {
        _export.RecentlyViewed.Clear();
        _settings.Save();
        NotifyHistoryState();
    }

    [RelayCommand]
    public void ClearExportHistory()
    {
        _activity.ClearFinishedCommand.Execute(null);
        NotifyHistoryState();
    }

}
