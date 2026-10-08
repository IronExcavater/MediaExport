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

namespace MediaExport;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.SetIcon("Assets/App/Square44x44Logo.ico");
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args) => navView.IsPaneOpen = !navView.IsPaneOpen;

    private void NavView_Loaded(object sender, RoutedEventArgs args) => navView.SelectedItem = navView.MenuItems[0];

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
