// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Controls;
using MediaExport.Core.Media.Providers;
using MediaExport.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MediaExport;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public static new App Current => (App)Application.Current;

    public AppViewModels ViewModels { get; private set; } = null!;
    public Window MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        // The app starts and seeks playback itself (from the timeline), which Chromium otherwise blocks until a click.
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--autoplay-policy=no-user-gesture-required");
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddMediaExport(GetDataFolder());
        _services = serviceCollection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        IconView.SourceIcons = _services.GetServices<IMediaSearchProvider>().ToDictionary(source => source.Id, source => source.Icon);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Anything that brings the app down is written down first, so it can be looked into.
        UnhandledException += (_, error) =>
            File.AppendAllText(Path.Combine(GetDataFolder(), "crash.log"), $"{DateTime.Now:u} {error.Exception}{Environment.NewLine}{Environment.NewLine}");

        ViewModels = _services.GetRequiredService<AppViewModels>();
        MainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow.Activate();
    }

    /// <returns>The chosen folder, or <c>null</c> if the picker was dismissed.</returns>
    public static async Task<string?> PickFolderAsync()
    {
        PickFolderResult? folder = await new FolderPicker(Current.MainWindow.AppWindow.Id).PickSingleFolderAsync();
        return folder?.Path;
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
