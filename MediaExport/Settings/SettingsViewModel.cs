// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MediaExport.Settings;

/// <summary>The app's preferences, and the state of the tools it depends on.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ToolchainService _toolchain;
    private readonly SettingsStore _settings;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private int themeIndex;

    public SettingsViewModel(ToolchainService toolchain, SettingsStore settings, IThemeService themeService)
    {
        _toolchain = toolchain;
        _settings = settings;
        _themeService = themeService;

        // Initialize theme from settings
        themeIndex = (int)_settings.Current.Theme;
    }

    // Update the theme when the user changes the selection in the UI
    partial void OnThemeIndexChanged(int value)
    {
        var theme = (AppTheme)value;
        _themeService.Apply(theme);
    }

    public List<ThemeOption> ThemeOptions { get; } =
    [
        new() { Name = "System", Glyph = "\uE8B8" },
        new() { Name = "Dark", Glyph = "\uE708" },
        new() { Name = "Light", Glyph = "\uE706" }
    ];

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

    public bool YtDlpInstalled => _toolchain.IsInstalled("yt-dlp");
    public bool FfmpegInstalled => _toolchain.IsInstalled("ffmpeg");

}
