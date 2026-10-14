// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Windows.System;

namespace MediaExport.Settings;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<SettingsViewModel>();

    // A tool may have been installed or removed since the page was last shown, so each is looked for again.
    protected override async void OnNavigatedTo(NavigationEventArgs args) => await ViewModel.RefreshToolsAsync();

    // A tool's link button carries the link as its Tag, and it opens in the browser.
    private async void Link_Click(object sender, RoutedEventArgs args)
    {
        var link = (Uri)((FrameworkElement)sender).Tag;
        await Launcher.LaunchUriAsync(link);
    }
}
