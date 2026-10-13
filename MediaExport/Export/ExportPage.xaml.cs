// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace MediaExport.Export;

public sealed partial class ExportPage : Page
{
    // The player is never squeezed below this, however short the window is.
    private const double MinPlayerHeight = 160;

    public ExportPage()
    {
        InitializeComponent();
    }

    public ExportViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<ExportViewModel>();

    // As a card, the player takes the height its title leaves it in the page, so the page fits the window. As
    // a row its height is fixed by the Narrow state, which is also what tells the two layouts apart here.
    private void Editor_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var isRow = !double.IsNaN(player.Height);
        var heightLeft = mediaRow.ActualHeight - words.ActualHeight - mediaArea.RowSpacing;
        player.MaxHeight = isRow ? double.PositiveInfinity : Math.Max(MinPlayerHeight, heightLeft);
    }

    private void Search_Click(object sender, RoutedEventArgs args)
    {
        var mainWindow = (MainWindow)App.Current.MainWindow;
        mainWindow.GoTo("Search");
    }
}
