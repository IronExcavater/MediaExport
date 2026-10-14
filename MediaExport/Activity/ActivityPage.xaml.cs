// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Export;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MediaExport.Activity;

public sealed partial class ActivityPage : Page
{
    public ActivityPage()
    {
        InitializeComponent();
    }

    public ActivityViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<ActivityViewModel>();

    // A row's button carries its export as its Tag.
    private static ExportJob GetJob(object sender) => (ExportJob)((FrameworkElement)sender).Tag;

    private void Remove_Click(object sender, RoutedEventArgs args) => ViewModel.Remove(GetJob(sender));

    private void Search_Click(object sender, RoutedEventArgs args)
    {
        var mainWindow = (MainWindow)App.Current.MainWindow;
        mainWindow.GoTo("Search");
    }

    // Opening media shows the Export page, where it can be exported again.
    private void Open_Click(object sender, RoutedEventArgs args)
    {
        var export = App.Current.Services.GetRequiredService<ExportViewModel>();
        export.Open(GetJob(sender).Media);
    }
}
