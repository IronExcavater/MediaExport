// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using System.IO;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MediaExport.Export;

public sealed partial class ExportPage : Page
{
    // The player is never squeezed below this, however short the window is.
    private const double MinPlayerHeight = 160;

    public ExportPage()
    {
        InitializeComponent();

        Loaded += ExportPage_Loaded;
        Unloaded += ExportPage_Unloaded;
    }

    private void ExportPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        RefreshSelections();
    }

    private void ExportPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExportViewModel.Media)
            or nameof(ExportViewModel.SelectedExportMode)
            or nameof(ExportViewModel.SelectedFormat)
            or nameof(ExportViewModel.ExportModes)
            or nameof(ExportViewModel.AvailableFormats))
        {
            DispatcherQueue.TryEnqueue(RefreshSelections);
        }
    }

    private void RefreshSelections()
    {
        if (ExportModeComboBox.ItemsSource is not null)
        {
            ExportModeComboBox.SelectedItem =
                ViewModel.SelectedExportMode;
        }

        if (ExportFormatComboBox.ItemsSource is not null)
        {
            ExportFormatComboBox.SelectedItem =
                ViewModel.SelectedFormat;
        }
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

    private async void BrowseOutputFolder_Click(
    object sender,
    RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads
        };

        picker.FileTypeFilter.Add("*");

        // Use your application's actual main window.
        var hwnd = WindowNative.GetWindowHandle(App.Current.MainWindow);

        InitializeWithWindow.Initialize(picker, hwnd);

        StorageFolder? folder = await picker.PickSingleFolderAsync();

        if (folder is not null)
        {
            ViewModel.SelectedOutputFolder = folder.Path;
        }
    }
}
