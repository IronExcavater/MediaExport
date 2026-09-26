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
using Microsoft.UI.Xaml.Media;
using System;
using Windows.System;

namespace MediaExport;

public sealed partial class MainWindow : Window
{
    private readonly ExportViewModel _viewModel = App.Current.Services.GetRequiredService<ExportViewModel>();
    private readonly SettingsViewModel _settings = App.Current.Services.GetRequiredService<SettingsViewModel>();

    public MainWindow()
    {
        InitializeComponent();
        ConfigureTitleBar();
        root.DataContext = _viewModel;
        toolsBanner.DataContext = _settings;
        BuildProviderMenu();

        AppWindow.SetIcon("Assets/App/Square44x44Logo.scale-200.ico");

        var openSettings = new KeyboardAccelerator { Key = (VirtualKey)188, Modifiers = VirtualKeyModifiers.Control }; // Ctrl+,
        openSettings.Invoked += (_, args) =>
        {
            navView.SelectedItem = navView.SettingsItem;
            args.Handled = true;
        };
        root.KeyboardAccelerators.Add(openSettings);

        ApplyTheme();
        _settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.SelectedTheme))
                ApplyTheme();
        };
    }

    private void ApplyTheme()
    {
        (root.RequestedTheme, AppWindow.TitleBar.PreferredTheme) = _settings.SelectedTheme.Value switch
        {
            "Light" => (ElementTheme.Light, TitleBarTheme.Light),
            "Dark" => (ElementTheme.Dark, TitleBarTheme.Dark),
            _ => (ElementTheme.Default, TitleBarTheme.UseDefaultAppMode)
        };
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

    private void ProviderMenu_Opening(object? sender, object args) => _viewModel.IsSourceMenuOpen = true;

    private void ProviderMenu_Closed(object? sender, object args) => _viewModel.IsSourceMenuOpen = false;

    // The box only shows its clear button while focused; keep it whenever there's text to clear.
    private void SearchBox_Loaded(object sender, RoutedEventArgs args)
    {
        if (FindDescendant<TextBox>(searchBox) is not { } textBox)
            return;

        void ShowClearButton()
        {
            if (textBox.FocusState == FocusState.Unfocused)
                VisualStateManager.GoToState(textBox, textBox.Text.Length > 0 ? "ButtonVisible" : "ButtonCollapsed", false);
        }

        textBox.LostFocus += (_, _) => ShowClearButton();
        textBox.TextChanged += (_, _) => ShowClearButton();
        ShowClearButton();
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if ((child as T ?? FindDescendant<T>(child)) is { } match)
                return match;
        }

        return null;
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        searchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void GoTo_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        navView.SelectedItem = navView.MenuItems[sender.Key == VirtualKey.Number1 ? 0 : 1];
        args.Handled = true;
    }

    private void SetUpTools_Click(object sender, RoutedEventArgs args) =>
        navView.SelectedItem = navView.SettingsItem;

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
        if (args.ChosenSuggestion is SearchSuggestion suggestion)
        {
            Select(suggestion.Media);
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
