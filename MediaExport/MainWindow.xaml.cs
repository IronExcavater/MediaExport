// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Export;
using MediaExport.Settings;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;

namespace MediaExport;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = Colors.Gray;

        AppWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(25, 255, 255, 255);
        AppWindow.TitleBar.ButtonHoverForegroundColor = Colors.White;

        navView.Loaded += NavView_Loaded;
        navFrame.Navigate(typeof(ExportPage));
    }
    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        var homeItem = navView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "MediaExport.Pages.ExportPage");

        if (homeItem != null)
        {
            navView.SelectedItem = homeItem;
        }

        navFrame.Navigate(typeof(ExportPage));
    }
    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        navView.IsPaneOpen = !navView.IsPaneOpen;
    }

    private void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            navFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItemContainer is NavigationViewItem item)
        {
            Type? page = item.Tag?.ToString() switch
            {
                "export" => typeof(ExportPage),
                "activity" => typeof(ActivityPage),
                _ => throw new ArgumentException("Unknown page tag")
            };
            if (page is not null)
                navFrame.Navigate(page);
        }
    }
}