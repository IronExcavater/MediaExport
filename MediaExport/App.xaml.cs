// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using System;
using Microsoft.Extensions.DependencyInjection;
using MediaExport.Services;

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
        serviceCollection.AddMediaExport();
        Services = serviceCollection.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
