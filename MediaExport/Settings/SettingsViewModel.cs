// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Activity;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Export;
using MediaExport.Search;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MediaExport.Settings;

/// <summary>
/// What the Settings page shows, a group at a time: the app's appearance, the tools it depends on, the history it
/// keeps, and which version it is.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ToolchainService _toolchain;
    private readonly SettingsStore _settings;
    private readonly IThemeService _themeService;
    private readonly SearchViewModel _search;
    private readonly ExportViewModel _export;
    private readonly ActivityViewModel _activity;

    public SettingsViewModel(
        ToolchainService toolchain,
        SettingsStore settings,
        IThemeService themeService,
        SearchViewModel search,
        ExportViewModel export,
        ActivityViewModel activity)
    {
        _toolchain = toolchain;
        _settings = settings;
        _themeService = themeService;
        _search = search;
        _export = export;
        _activity = activity;

        // Initialize theme from settings
        ThemeIndex = (int)_settings.Current.Theme;

        // ffmpeg and ffprobe come together in an archive, which the app can't unpack yet, so only yt-dlp has
        // a file to install from.
        ToolInfo[] tools =
        [
            new("yt-dlp", "yt-dlp", "--version", new Uri("https://github.com/yt-dlp/yt-dlp#readme"),
                new Uri("https://github.com/yt-dlp/yt-dlp"),
                "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"),
            new("ffmpeg", "FFmpeg", "-version", new Uri("https://ffmpeg.org"),
                new Uri("https://github.com/FFmpeg/FFmpeg"), DownloadUrl: null),
            new("ffprobe", "FFprobe", "-version", new Uri("https://ffmpeg.org/ffprobe.html"),
                new Uri("https://github.com/FFmpeg/FFmpeg"), DownloadUrl: null)
        ];
        Tools = tools.Select(tool => new ToolViewModel(tool, toolchain)).ToList();
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

    public bool YtDlpInstalled => _toolchain.IsInstalled("yt-dlp");
    public bool FfmpegInstalled => _toolchain.IsInstalled("ffmpeg");

    /// <summary>Looks for every tool again, which the page asks for when it is shown.</summary>
    [RelayCommand]
    public Task RefreshToolsAsync() => Task.WhenAll(Tools.Select(tool => tool.RefreshAsync()));

    /// <summary>Updates every tool that the app installed and has a file to update from.</summary>
    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        foreach (var tool in Tools)
            await tool.UpdateAsync();
    }

    /// <summary>Opens the folder the app installs tools into, which is made first if nothing is installed yet.</summary>
    [RelayCommand]
    private void OpenToolsFolder()
    {
        Directory.CreateDirectory(_toolchain.Folder);
        Process.Start("explorer.exe", $"\"{_toolchain.Folder}\"");
    }

    [RelayCommand]
    public async Task InstallYtDlpAsync()
    {
        await _toolchain.EnsureInstalledAsync(
            "yt-dlp",
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
    }

    [RelayCommand]
    public async Task InstallFfmpegAsync()
    {
        // ffmpeg usually requires a zip/extract workflow rather than a direct exe
        await _toolchain.EnsureInstalledAsync(
            "ffmpeg",
            "https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip");
    }

    // The History group: how much the app remembers, and clearing it.

    /// <summary>How much of each kind of history is kept, as its row says it.</summary>
    public string RecentSearchesText => Count(_search.RecentSearches.Count, "search", "searches");
    public string RecentlyViewedText => Count(_export.RecentlyViewed.Count, "item", "items");
    public string ExportHistoryText
    {
        get
        {
            var finishedCount = _activity.Jobs.Count(job => job.IsFinished);
            var exports = Count(finishedCount, "export", "exports");
            return $"{exports}. The exported files are kept.";
        }
    }

    /// <summary>Reads the history's sizes again. The page asks for this when it is shown, as they change elsewhere.</summary>
    public void RefreshHistory()
    {
        OnPropertyChanged(nameof(RecentSearchesText));
        OnPropertyChanged(nameof(RecentlyViewedText));
        OnPropertyChanged(nameof(ExportHistoryText));
        ClearRecentSearchesCommand.NotifyCanExecuteChanged();
        ClearRecentlyViewedCommand.NotifyCanExecuteChanged();
        ClearExportHistoryCommand.NotifyCanExecuteChanged();
    }

    private bool HasRecentSearches() => _search.RecentSearches.Count > 0;
    private bool HasRecentlyViewed() => _export.RecentlyViewed.Count > 0;
    private bool HasExportHistory() => _activity.Jobs.Any(job => job.IsFinished);

    [RelayCommand(CanExecute = nameof(HasRecentSearches))]
    private void ClearRecentSearches()
    {
        _search.RecentSearches.Clear();
        RefreshHistory();
    }

    [RelayCommand(CanExecute = nameof(HasRecentlyViewed))]
    private void ClearRecentlyViewed()
    {
        _export.RecentlyViewed.Clear();
        RefreshHistory();
    }

    [RelayCommand(CanExecute = nameof(HasExportHistory))]
    private void ClearExportHistory()
    {
        _activity.ClearFinishedCommand.Execute(null);
        RefreshHistory();
    }

    private static string Count(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";

    // The About group.

    public string VersionText
    {
        get
        {
            var version = typeof(SettingsViewModel).Assembly.GetName().Version;
            return $"Version {version?.ToString(3) ?? "1.0.0"}";
        }
    }
}
