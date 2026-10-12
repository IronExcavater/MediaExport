// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0
using MediaExport.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Xaml;

namespace MediaExport.Settings;
// in seporate file to keep the settings model separate from the UI 
public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    void Apply(AppTheme theme);
}

// Gets the current theme from the settings and applies it to the app
public sealed class ThemeService(SettingsStore settings) : IThemeService
{
    public AppTheme CurrentTheme => settings.Current.Theme;

    public void Apply(AppTheme theme)
    {
        settings.Current.Theme = theme;
        settings.Save();

        var elementTheme = theme switch
        {
            AppTheme.Dark => ElementTheme.Dark,
            AppTheme.Light => ElementTheme.Light,
            _ => ElementTheme.Default
        };

        if (App.Current.MainWindow is MainWindow mainWindow)
            mainWindow.ApplyTheme(elementTheme);
        ApplyTitleBarColors(theme);
    }
    private static void ApplyTitleBarColors(AppTheme theme)
    {
        var titleBar = App.Current.MainWindow.AppWindow.TitleBar;

        if (theme == AppTheme.Light)
        {
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.Black;
        }
        else
        {
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.White;
        }
    }

}