// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Export;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MediaExport.Activity;

public sealed partial class ActivityPage : Page
{
    public ActivityPage()
    {
        InitializeComponent();
        jobList.ItemsSource = App.Current.Services.GetRequiredService<ExportViewModel>().Jobs;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) =>
        emptyState.Visibility = jobList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}
