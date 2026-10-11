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
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using Windows.Graphics;

namespace MediaExport;

public sealed partial class MainWindow : Window
{
    // Maps each navigation item's Tag to the page it opens.
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Search"] = typeof(SearchPage),
        ["Export"] = typeof(ExportPage),
        ["Activity"] = typeof(ActivityPage)
    };

    // The window must keep its size this long before the app name slides. Sliding while the window is
    // still being resized makes both stutter, and a resize that wavers around the compact width would
    // slide the name in and out.
    private static readonly TimeSpan ResizeDebounce = TimeSpan.FromMilliseconds(400);

    private readonly SettingsStore _settings = App.Current.Services.GetRequiredService<SettingsStore>();
    private readonly DispatcherQueueTimer _resizeDebounceTimer;
    private bool _isAppNameHidden;

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

        RestoreWindowSize();
        Closed += (_, _) => SaveWindowSize();

        _resizeDebounceTimer = DispatcherQueue.CreateTimer();
        _resizeDebounceTimer.Interval = ResizeDebounce;
        _resizeDebounceTimer.IsRepeating = false;
        _resizeDebounceTimer.Tick += (_, _) => UpdateAppName();
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
        // them (microsoft-ui-xaml#10344). A negative right margin takes the
        // excess back.
        // The app name slides between no width and its full width. A storyboard can't animate from
        // MaxWidth's default, infinity, so the full width is set as a number.
        appName.MaxWidth = appNameText.ActualWidth;
        showAppNameWidth.To = appNameText.ActualWidth;

        double reservedWidth = AppWindow.TitleBar.RightInset;
        var neededWidth = reservedWidth / scale;
        titleBar.Margin = new Thickness(0, 0, neededWidth - reservedWidth, 0);
    }

    // Windows doesn't remember the size of a WinUI window between runs, so the app does.
    private void RestoreWindowSize()
    {
        var settings = _settings.Current;
        if (settings.WindowWidth is not null && settings.WindowHeight is not null)
            AppWindow.Resize(new SizeInt32(settings.WindowWidth.Value, settings.WindowHeight.Value));

        if (settings.IsWindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Maximize();
    }

    private void SaveWindowSize()
    {
        var settings = _settings.Current;
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        var state = presenter?.State;
        settings.IsWindowMaximized = state == OverlappedPresenterState.Maximized;

        // A maximised or minimised window keeps the size saved before, which is the size it returns to.
        if (state == OverlappedPresenterState.Restored)
        {
            settings.WindowWidth = AppWindow.Size.Width;
            settings.WindowHeight = AppWindow.Size.Height;
        }

        _settings.Save();
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

    private void SearchInput_Submitted(object? sender, EventArgs args)
    {
        GoTo("Search");
        Search.SearchCommand.Execute(null);
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        searchInput.FocusQueryBox();
        args.Handled = true;
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        navView.TogglePane();
        _settings.Current.IsPaneOpen = navView.IsExpanded;
        _settings.Save();
    }

    private void TitleBar_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer.Start();
    }

    // Slides the app name away behind the logo on a compact window, leaving its space to the search field,
    // and slides it back out on a wider one.
    private void UpdateAppName()
    {
        var isCompact = titleBar.ActualWidth < WindowSizes.Compact;
        if (isCompact == _isAppNameHidden)
            return;

        _isAppNameHidden = isCompact;
        var slide = isCompact ? hideAppName : showAppName;
        slide.Begin();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs args) => GoTo("Search");

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type? pageType = null;
        if (args.IsSettingsSelected)
            pageType = typeof(SettingsPage);
        else if (args.SelectedItemContainer?.Tag is string tag)
            pageType = Pages.GetValueOrDefault(tag);

        // The Search page shows the results itself, so the field's own list of them stays shut there.
        searchInput.IsResultsListEnabled = pageType != typeof(SearchPage);

        var isAlreadyShown = navFrame.CurrentSourcePageType == pageType;
        if (pageType is not null && !isAlreadyShown)
            navFrame.Navigate(pageType);
    }
}
