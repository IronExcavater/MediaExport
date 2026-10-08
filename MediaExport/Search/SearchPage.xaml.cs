// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MediaExport.Search;

/// <summary>Every source's results for one search, as a feed of cards.</summary>
public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ResultsViewModel.Page))
                resultsScroll.ChangeView(null, 0, null);
        };
    }

    public ResultsViewModel ViewModel { get; } = App.Current.ViewModels.Results;
    public SearchViewModel Search { get; } = App.Current.ViewModels.Search;

    private async void Page_Loaded(object sender, RoutedEventArgs args)
    {
        if (Search.QueryText.Length == 0 && ViewModel.Query.Length == 0)
            await ViewModel.LoadPopularAsync();
    }

    private void Page_Click(object sender, RoutedEventArgs args)
    {
        if (((FrameworkElement)sender).Tag is int page)
            ViewModel.GoToPageCommand.Execute(page);
    }

    private void RecentSearch_Click(object sender, RoutedEventArgs args) =>
        ((MainWindow)App.Current.MainWindow).ShowResults((string)((FrameworkElement)sender).Tag);

    private void Result_Chosen(object? sender, MediaSearchResult media) => ((MainWindow)App.Current.MainWindow).Open(media);
}
