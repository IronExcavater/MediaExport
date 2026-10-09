// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Core.Settings;
using MediaExport.Export;
using MediaExport.Search;
using MediaExport.Settings;
using MediaExport.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;

namespace MediaExport;

public sealed partial class MainWindow : Window
{
    // Maps each navigation item's Tag to the page it opens.
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Export"] = typeof(ExportPage),
        ["Search"] = typeof(SearchPage),
        ["Activity"] = typeof(ActivityPage)
    };

    private readonly SettingsStore _settings = App.Current.Services.GetRequiredService<SettingsStore>();

    /// <summary>The search state shared by the title bar and Search page.</summary>
    public SearchViewModel Search { get; } = App.Current.Services.GetRequiredService<SearchViewModel>();

    public MainWindow()
    {
        InitializeComponent();
        navView.IsExpanded = _settings.Current.IsPaneOpen;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        // The window icon has to be given as a file path; it can't come from Icons.xaml.
        AppWindow.SetIcon("Assets/App/Square44x44Logo.ico");
    }

    // The window's minimum size and the caption buttons' width are both measured in screen pixels, so
    // they are converted with the display's scale.
    private void Root_Loaded(object sender, RoutedEventArgs args)
    {
        var scale = root.XamlRoot.RasterizationScale;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(WindowSizes.MinWidth * scale);
            presenter.PreferredMinimumHeight = (int)(WindowSizes.MinHeight * scale);
        }

        // TitleBar reserves room for the caption buttons using their width in screen pixels as if it
        // were view pixels, so above 100% display scale it reserves too much and leaves a gap beside
        // them (microsoft-ui-xaml#10344). This workaround adds a negative right margin takes the
        // excess back.
        double reservedWidth = AppWindow.TitleBar.RightInset;
        var neededWidth = reservedWidth / scale;
        titleBar.Margin = new Thickness(0, 0, neededWidth - reservedWidth, 0);
    }

    /// <summary>Navigates to a page by its key in <see cref="Pages"/>.</summary>
    public void GoTo(string page)
    {
        foreach (var item in navView.MenuItems)
        {
            if (item is NavigationViewItem { Tag: string tag } && tag == page)
                navView.SelectedItem = item;
        }
    }

    // When the search input is submitted, open the Search page with the results.
    private void SearchInput_Submitted(object? sender, EventArgs args)
    {
        GoTo("Search");
        Search.SearchCommand.Execute(null);
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        searchInput.FocusEditor();
        args.Handled = true;
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        navView.TogglePane();
        _settings.Current.IsPaneOpen = navView.IsExpanded;
        _settings.Save();
    }

    // Hides the app name on a compact window, leaving its space to the search field.
    private void TitleBar_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var isCompact = args.NewSize.Width < WindowSizes.Compact;
        appName.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void NavView_Loaded(object sender, RoutedEventArgs args) => GoTo("Export");

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type? pageType = null;
        if (args.IsSettingsSelected)
            pageType = typeof(SettingsPage);
        else if (args.SelectedItemContainer?.Tag is string tag)
            pageType = Pages.GetValueOrDefault(tag);

        var isAlreadyShown = navFrame.CurrentSourcePageType == pageType;
        if (pageType is not null && !isAlreadyShown)
            navFrame.Navigate(pageType);
    }
}
