// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Export;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;

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
    private void Retry_Click(object sender, RoutedEventArgs args) => ViewModel.Retry(GetJob(sender));

    private void JobsList_SelectionChanged(object sender, SelectionChangedEventArgs args) =>
        ViewModel.Select(jobsList.SelectedItems.Cast<ExportJob>());

    private void SelectAll_Click(object sender, RoutedEventArgs args) => jobsList.SelectAll();
    private void ClearSelection_Click(object sender, RoutedEventArgs args) => jobsList.SelectedItems.Clear();

    private void Search_Click(object sender, RoutedEventArgs args)
    {
        var mainWindow = (MainWindow)App.Current.MainWindow;
        mainWindow.GoTo("Search");
    }

    // Opening media shows the Export page, where it can be exported again.
    private void Open_Click(object sender, RoutedEventArgs args)
    {
        var export = App.Current.Services.GetRequiredService<ExportViewModel>();
        var job = GetJob(sender);
        export.Open(job.Media, job.Options);
    }

    private void OpenFile_Click(object sender, RoutedEventArgs args)
    {
        var file = GetJob(sender).Files.FirstOrDefault();
        if (file is not null && System.IO.File.Exists(file))
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
    }

    private void CopyLink_Click(object sender, RoutedEventArgs args)
    {
        var data = new DataPackage();
        data.SetText(GetJob(sender).Media.SourceUrl);
        Clipboard.SetContent(data);
    }
}
