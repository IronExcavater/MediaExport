// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Services;
using MediaExport.Core.Settings;
using MediaExport.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.IO;

namespace MediaExport;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    /// <summary>Everything the app is made of that's shared: pages ask here for their view models and services.</summary>
    public IServiceProvider Services { get; }

    public Window MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        Services = new ServiceCollection().AddMediaExport(GetDataFolder()).BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();

        // Apply the theme from settings on launch
        var settings = Services.GetRequiredService<SettingsStore>();
        var themeService = Services.GetRequiredService<IThemeService>();
        themeService.Apply(settings.Current.Theme);

        MainWindow.Activate();
    }

    private static string GetDataFolder()
    {
        try
        {
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (InvalidOperationException)
        {
            // Unpackaged runs have no package data folder.
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaExport");
        }
    }
}
