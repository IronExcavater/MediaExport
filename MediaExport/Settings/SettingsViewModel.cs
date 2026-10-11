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

    [ObservableProperty]
    private int themeIndex;

    public SettingsViewModel(ToolchainService toolchain, SettingsStore settings)
    {
        _toolchain = toolchain;
        _settings = settings;

        // Initialize theme from settings
        themeIndex = _settings.Current.ThemeIndex;
    }

    // Update the theme when the user changes the selection in the UI
    partial void OnThemeIndexChanged(int value)
    {
        _settings.Current.ThemeIndex = value;
        _settings.Save();

        var theme = value switch
        {
            1 => ElementTheme.Dark,
            2 => ElementTheme.Light,
            _ => ElementTheme.Default
        };

        if (App.Current.MainWindow is MainWindow mainWindow)
            mainWindow.ApplyTheme(theme);
    }
}
