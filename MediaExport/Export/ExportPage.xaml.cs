// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        if (await App.PickFolderAsync() is { } folder)
            ViewModel.OutputFolder = folder;
    }

    // ComboBox items can't bind IsEnabled, so qualities the media doesn't have are disabled as the list opens.
    private void Quality_DropDownOpened(object? sender, object args)
    {
        var comboBox = (ComboBox)sender!;
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (QualityChoice quality in ViewModel.Qualities)
            {
                if (comboBox.ContainerFromItem(quality) is ComboBoxItem item)
                    item.IsEnabled = quality.IsAvailable;
            }
        });
    }
}
