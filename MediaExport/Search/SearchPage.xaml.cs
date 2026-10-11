// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Specialized;

namespace MediaExport.Search;

public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
        ViewModel.Results.CollectionChanged += Results_CollectionChanged;
    }

    public SearchViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<SearchViewModel>();

    /// <summary>One for each skeleton card shown while a page loads. Only how many there are matters.</summary>
    public int[] LoadingSlots { get; } = new int[12];

    // Results are cleared when a new search or page starts, which is when the view goes back to the top.
    private void Results_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
            resultsScroll.ChangeView(horizontalOffset: null, verticalOffset: 0, zoomFactor: null, disableAnimation: true);
    }
}
