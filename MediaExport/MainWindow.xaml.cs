// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Converters;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Export;
using MediaExport.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace MediaExport;

public sealed partial class MainWindow : Window
{
    private readonly ExportViewModel _viewModel = App.Current.Services.GetRequiredService<ExportViewModel>();

    public MainWindow()
    {
        InitializeComponent();
        ConfigureTitleBar();
        root.DataContext = _viewModel;
        BuildProviderMenu();

        AppWindow.SetIcon("Assets/App/Square44x44Logo.scale-200.ico");
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

    }

    private void BuildProviderMenu()
    {
        foreach (IMediaSearchProvider provider in _viewModel.Providers)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = provider.DisplayName,
                GroupName = "providers",
                IsChecked = provider == _viewModel.SelectedProvider,
                Icon = new ImageIcon { Source = IconImageConverter.GetIcon(provider.Id) }
            };
            item.Click += (_, _) =>
            {
                _viewModel.SelectedProvider = provider;
                searchBox.Focus(FocusState.Programmatic);
            };
            providerMenu.Items.Add(item);
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args) =>
        navView.IsPaneOpen = !navView.IsPaneOpen;

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        searchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs args) =>
        _viewModel.ShowSuggestions();

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason is not AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        _viewModel.SearchText = sender.Text;
        _viewModel.SearchCommand.Execute(null);
    }

    // Only a click or Enter selects; arrowing through suggestions shouldn't load each one.
    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is MediaSearchResult result)
        {
            Select(result);
            return;
        }

        _viewModel.SearchText = sender.Text;
        _viewModel.SearchCommand.Execute(null);
    }

    // Results always open in Export, whichever page the search started from.
    private void Select(MediaSearchResult result)
    {
        _viewModel.SelectResultCommand.Execute(result);
        navView.SelectedItem = navView.MenuItems[0];
    }

    private void ErrorTip_CloseButtonClick(TeachingTip sender, object args) =>
        _viewModel.ErrorMessage = null;

    private void NavView_Loaded(object sender, RoutedEventArgs e) =>
        navView.SelectedItem = navView.MenuItems[0];

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type? pageType = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : ((args.SelectedItemContainer as NavigationViewItem)?.Tag as string) switch
            {
                "Export" => typeof(ExportPage),
                "Activity" => typeof(ActivityPage),
                _ => null
            };

        if (pageType is not null && navFrame.CurrentSourcePageType != pageType)
            navFrame.Navigate(pageType);
    }
}
