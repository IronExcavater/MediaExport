// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using Microsoft.UI.Xaml;

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
}
