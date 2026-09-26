// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using System;

namespace MediaExport.Export;

public sealed partial class ExportPage : Page
{
    private ExportViewModel ViewModel { get; }

    public ExportPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<ExportViewModel>();
        DataContext = ViewModel;
    }

    private async void ChangeFolder_Click(object sender, RoutedEventArgs args)
    {
        var picker = new FolderPicker(App.Current.MainWindow.AppWindow.Id);
        PickFolderResult? folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            ViewModel.OutputFolder = folder.Path;
    }
}
