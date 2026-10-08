// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace MediaExport.Search;

/// <summary>Every source's results for one search, as a feed of cards.</summary>
public sealed partial class SearchPage : Page
{
    // The narrowest a card gets, and the room under its picture for its words.
    private const double MinimumCard = 300;
    private const double CardText = 84 + 10 + 16;

    public SearchPage()
    {
        InitializeComponent();
    }

    public ResultsViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<ResultsViewModel>();

    // As many cards across as fit, each exactly wide enough to fill its share, with a 16:9 picture.
    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        double width = args.NewSize.Width;
        int across = Math.Max(1, (int)((width + feed.MinColumnSpacing) / (MinimumCard + feed.MinColumnSpacing)));
        double card = Math.Floor((width - feed.MinColumnSpacing * (across - 1)) / across);
        feed.MinItemWidth = card;
        feed.MinItemHeight = (card - 16) * 9 / 16 + CardText;
    }

    private void Result_Click(object sender, RoutedEventArgs args) =>
        ((MainWindow)App.Current.MainWindow).Open((MediaSearchResult)((FrameworkElement)sender).Tag);
}
