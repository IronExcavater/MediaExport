// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

    private static MainWindow Shell => (MainWindow)App.Current.MainWindow;

    // Like File Explorer: click selects, double-click opens.
    private void JobList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if ((args.OriginalSource as FrameworkElement)?.DataContext is ExportJob { IsCompleted: true } job)
            job.OpenCommand.Execute(null);
    }

    private void JobList_SelectionChanged(object sender, SelectionChangedEventArgs args) =>
        removeButton.IsEnabled = jobList.SelectedItems.Count > 0;

    private void Remove_Click(object sender, RoutedEventArgs args) =>
        ViewModel.Remove(jobList.SelectedItems.Cast<ExportJob>());

    private void RemoveOne_Click(object sender, RoutedEventArgs args) =>
        ViewModel.Remove([(ExportJob)((FrameworkElement)sender).Tag]);

    private void ExportAgain_Click(object sender, RoutedEventArgs args) =>
        Shell.ExportAgain((ExportJob)((FrameworkElement)sender).Tag);

    private void CopyLink_Click(object sender, RoutedEventArgs args)
    {
        var package = new DataPackage();
        package.SetText(((ExportJob)((FrameworkElement)sender).Tag).Media.SourceUrl);
        Clipboard.SetContent(package);
    }
}
