// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Linq;

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

    // MenuFlyout has no ItemsSource, so the versions are listed as it opens.
    private void Versions_Opening(object? sender, object args)
    {
        var menu = (MenuFlyout)sender!;
        var tool = (ToolViewModel)menu.Target.DataContext;
        menu.Items.Clear();

        if (tool.Releases.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = tool.HasError ? "Couldn't list versions" : "Looking for versions…", IsEnabled = false });
            return;
        }

        foreach (ToolRelease release in tool.Releases.Take(15))
        {
            bool isCurrent = tool.IsManaged && tool.Status?.Version == release.Version;
            var item = new ToggleMenuFlyoutItem
            {
                Text = release == tool.Latest ? $"{release.Version} (newest)" : release.Version,
                IsChecked = isCurrent,
                IsEnabled = !isCurrent,
                Command = tool.InstallVersionCommand,
                CommandParameter = release
            };
            ToolTipService.SetToolTip(item, $"Released {release.Published:d}");
            menu.Items.Add(item);
        }
    }
}
