// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace MediaExport.Search;

/// <summary>Every source's results for one search, as a feed of cards.</summary>
public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
    }

    public ResultsViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<ResultsViewModel>();

    private void Result_Chosen(object? sender, MediaSearchResult media) => ((MainWindow)App.Current.MainWindow).Open(media);
}
