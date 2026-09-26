// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MediaExport.Settings;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel = App.Current.Services.GetRequiredService<SettingsViewModel>();

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    // The WinGet check is slow, so it runs the first time Settings is opened rather than at startup.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (_viewModel.Tools.Count == 0 && _viewModel.CheckToolsCommand.CanExecute(null))
            _viewModel.CheckToolsCommand.Execute(null);
    }

    private async void ChangeFolder_Click(object sender, RoutedEventArgs args)
    {
        if (await App.PickFolderAsync() is { } folder)
            _viewModel.Export.OutputFolder = folder;
    }

    private void Install_Click(object sender, RoutedEventArgs args) =>
        _viewModel.InstallCommand.Execute((ToolStatus)((Button)sender).Tag);

    private void Update_Click(object sender, RoutedEventArgs args) =>
        _viewModel.UpdateCommand.Execute((ToolStatus)((Button)sender).Tag);
}
