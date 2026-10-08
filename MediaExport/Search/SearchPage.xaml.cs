// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MediaExport.Search;

/// <summary>Every source's results for one search, as a feed of cards.</summary>
public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
    }

    public ResultsViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<ResultsViewModel>();

    // It only ever picks every source; clicking it while lit would otherwise unlight it with them all still picked.
    private void Everywhere_Click(object sender, RoutedEventArgs args)
    {
        ViewModel.PickAllCommand.Execute(null);
        ((ToggleButton)sender).IsChecked = ViewModel.AreAllPicked;
    }

    private void Result_Chosen(object? sender, MediaSearchResult media) => ((MainWindow)App.Current.MainWindow).Open(media);
}
