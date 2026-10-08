// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Tooling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Windows.System;

namespace MediaExport.Settings;

public sealed partial class SettingsPage : Page
{
    private bool _checked;

    public SettingsPage()
    {
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; } = App.Current.ViewModels.Settings;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.RefreshHistory();
        if (_checked)
            return;

        _checked = true;
        await ViewModel.RefreshToolsAsync();
    }

    private async void Link_Click(object sender, RoutedEventArgs args)
    {
        if (((FrameworkElement)sender).Tag is { } link && Uri.TryCreate(link.ToString(), UriKind.Absolute, out Uri? uri))
            await Launcher.LaunchUriAsync(uri);
    }
}
