// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Export;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;

namespace MediaExport.Activity;

public sealed partial class ActivityPage : Page
{
    private readonly ExportViewModel _viewModel = App.Current.Services.GetRequiredService<ExportViewModel>();

    public ActivityPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    // One row open at a time, so only one web player is ever loaded.
    private void JobList_ItemClick(object sender, ItemClickEventArgs args)
    {
        var clicked = (ExportJob)args.ClickedItem;
        foreach (ExportJob job in _viewModel.Jobs)
            job.IsExpanded = job == clicked && !job.IsExpanded;
    }

    private void SelectToggle_Changed(object sender, RoutedEventArgs args)
    {
        bool selecting = selectToggle.IsChecked == true;
        jobList.SelectionMode = selecting ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
        jobList.IsItemClickEnabled = !selecting;
        selectAllButton.Visibility = removeButton.Visibility = selecting ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs args) => jobList.SelectAll();

    private void JobList_SelectionChanged(object sender, SelectionChangedEventArgs args) =>
        removeButton.IsEnabled = jobList.SelectedItems.Count > 0;

    private void Remove_Click(object sender, RoutedEventArgs args)
    {
        _viewModel.Remove(jobList.SelectedItems.Cast<ExportJob>());
        selectToggle.IsChecked = false;
    }
}
