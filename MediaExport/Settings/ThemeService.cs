// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0
using MediaExport.Core.Settings;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace MediaExport.Settings;

// Gets the current theme from the settings and applies it to the app
public sealed class ThemeService(SettingsStore settings) : IThemeService
{
    public AppTheme CurrentTheme => settings.Current.Theme;

    public void Apply(AppTheme theme)
    {
        if (settings.Current.Theme != theme)
        {
            settings.Current.Theme = theme;
            settings.Save();
        }

        var elementTheme = theme switch
        {
            AppTheme.Dark => ElementTheme.Dark,
            AppTheme.Light => ElementTheme.Light,
            _ => ElementTheme.Default
        };

        // The caption buttons are drawn by Windows, not by XAML, so they are given the theme separately. On the
        // system theme they follow Windows, which a fixed colour for them did not.
        var titleBarTheme = theme switch
        {
            AppTheme.Dark => TitleBarTheme.Dark,
            AppTheme.Light => TitleBarTheme.Light,
            _ => TitleBarTheme.UseDefaultAppMode
        };

        // SettingsViewModel can apply its initial theme while MainWindow is still being constructed.
        // OnLaunched applies the theme again after the window has been assigned.
        if (App.Current.MainWindow is not MainWindow mainWindow)
            return;

        mainWindow.ApplyTheme(elementTheme);
        mainWindow.AppWindow.TitleBar.PreferredTheme = titleBarTheme;
    }
}
