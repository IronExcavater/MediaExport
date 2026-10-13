// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MediaExport.Settings;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<SettingsViewModel>();

    // A tool may have been installed or removed since the page was last shown, and searches and exports have been
    // made, so both are read again.
    protected override async void OnNavigatedTo(NavigationEventArgs args)
    {
        ViewModel.RefreshHistory();
        await ViewModel.RefreshToolsAsync();
    }
}
