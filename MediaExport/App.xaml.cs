// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MediaExport;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    public IServiceProvider Services { get; }
    public Window MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddMediaExport(Path.Combine(GetDataFolder(), "settings.json"));
        Services = serviceCollection.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
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
