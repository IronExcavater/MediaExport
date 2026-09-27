// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Tooling;
using Microsoft.Extensions.DependencyInjection;
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

    public SettingsViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<SettingsViewModel>();

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

    // MenuFlyout has no ItemsSource, so the versions are listed as it opens, each with its release date.
    private void Versions_Opening(object? sender, object args)
    {
        var menu = (MenuFlyout)sender!;
        var tool = (ToolViewModel)menu.Target.DataContext;
        menu.Items.Clear();

        if (tool.Releases.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = tool.HasError ? "Couldn't find any versions" : "Looking for versions…", IsEnabled = false });
            return;
        }

        foreach (ToolRelease release in tool.Releases)
        {
            bool isCurrent = tool.Status?.Version == release.Version;
            menu.Items.Add(new ToggleMenuFlyoutItem
            {
                Text = release == tool.Latest ? $"{release.Version}   newest" : release.Version,
                KeyboardAcceleratorTextOverride = release.Published.ToLocalTime().ToString("d MMM yyyy"),
                IsChecked = isCurrent,
                IsEnabled = !(isCurrent && tool.IsManaged),
                Command = tool.InstallVersionCommand,
                CommandParameter = release
            });
        }
    }
}
