// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Core.Settings;
using MediaExport.Export;
using MediaExport.Search;
using MediaExport.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace MediaExport;

public sealed partial class MainWindow : Window
{
    // The pages by the Tag of their item in the navigation pane. A new page is a line here and an item there.
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Export"] = typeof(ExportPage),
        ["Search"] = typeof(SearchPage),
        ["Activity"] = typeof(ActivityPage)
    };

    private readonly SettingsStore _settings = App.Current.Services.GetRequiredService<SettingsStore>();

    public MainWindow()
    {
        InitializeComponent();
        navView.IsExpanded = _settings.Current.IsPaneOpen;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        // Windows takes the window's icon as a file, not a resource, so this one path stays.
        AppWindow.SetIcon("Assets/App/Square44x44Logo.ico");
    }

    /// <summary>Shows a page by its name in <see cref="Pages"/>, e.g. from another page.</summary>
    public void GoTo(string page)
    {
        foreach (object item in navView.MenuItems)
        {
            if (item is NavigationViewItem { Tag: string tag } && tag == page)
                navView.SelectedItem = item;
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        navView.IsExpanded = !navView.IsExpanded;
        _settings.Current.IsPaneOpen = navView.IsExpanded;
        _settings.Save();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs args) => GoTo("Export");

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type? pageType = args.IsSettingsSelected ? typeof(SettingsPage)
            : args.SelectedItemContainer?.Tag is string tag ? Pages.GetValueOrDefault(tag)
            : null;

        if (pageType is not null && navFrame.CurrentSourcePageType != pageType)
            navFrame.Navigate(pageType);
    }
}
