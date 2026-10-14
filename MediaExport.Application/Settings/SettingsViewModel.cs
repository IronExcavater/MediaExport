// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using System;
using System.Collections.Generic;
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

    public SettingsViewModel(
        ToolchainService toolchain,
        SettingsStore settings,
        IThemeService themeService,
        IFolderLauncher folderLauncher)
    {
        _toolchain = toolchain;
        _settings = settings;
        _themeService = themeService;
        _folderLauncher = folderLauncher;

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

    /// <summary>Looks for every tool again, which the page also asks for when it is shown.</summary>
    [RelayCommand]
    public Task RefreshToolsAsync() => Task.WhenAll(Tools.Select(tool => tool.RefreshAsync()));

    /// <summary>Opens the folder the app installs tools into, which is made first if nothing is installed yet.</summary>
    [RelayCommand]
    private void OpenToolsFolder() => _folderLauncher.Open(_toolchain.Folder);

    [RelayCommand]
    public async Task InstallYtDlpAsync()
    {
        await _toolchain.InstallAsync(
            "yt-dlp",
            new Uri("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"));
    }

    [RelayCommand]
    public async Task InstallFfmpegAsync()
    {
        // ffmpeg usually requires a zip/extract workflow rather than a direct exe
        await _toolchain.InstallAsync(
            "ffmpeg",
            new Uri("https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip"));
    }
}
