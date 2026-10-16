// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Export;
using MediaExport.Media.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.Specialized;
using System.Linq;
using Windows.System;

namespace MediaExport.Search;

public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
        ViewModel.Results.CollectionChanged += Results_CollectionChanged;
        ViewModel.SelectedProviders.CollectionChanged += (_, _) => ShowSkeletons();
        ShowSkeletons();
    }

    public SearchViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<SearchViewModel>();

    // More than a page holds, so that a tall window is filled to its bottom edge while a page loads.
    private const int SkeletonCount = 36;

    // The skeleton cards have the shapes the results will have, which are each chosen provider's in turn. That
    // puts them in the same rows as the results, so the page doesn't shift when they arrive.
    private void ShowSkeletons()
    {
        var kinds = ViewModel.SelectedProviders.Select(provider => provider.Kind).ToList();
        if (kinds.Count == 0)
            kinds.Add(MediaKind.Video);

        var shapes = Enumerable.Range(0, SkeletonCount).Select(index => MediaThumbnail.ShapeOf(kinds[index % kinds.Count]));
        skeletons.ItemsSource = shapes.ToArray();
    }

    private void PageBox_GotFocus(object sender, RoutedEventArgs args) => pageBox.SelectAll();

    private void PageBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args) =>
        args.Cancel = !args.NewText.All(char.IsDigit);

    // Enter leaves the box, and leaving it is what goes to the page.
    private void PageBox_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
            return;

        // A desktop app must say where to look for the next element, or the call fails.
        args.Handled = true;
        var withinThisWindow = new FindNextElementOptions { SearchRoot = XamlRoot.Content };
        FocusManager.TryMoveFocus(FocusNavigationDirection.Next, withinThisWindow);
    }

    private async void PageBox_LostFocus(object sender, RoutedEventArgs args)
    {
        var isNumber = int.TryParse(pageBox.Text, out var page);
        var isAnotherPage = isNumber && page >= 1 && page != ViewModel.Page;
        if (isAnotherPage)
            await ViewModel.GoToPageAsync(page);

        // Whatever was typed is replaced by the page that is showing, which undoes anything that isn't a page.
        pageBox.Text = ViewModel.Page.ToString();
    }

    // A pressed result opens in the Export page.
    private void Result_Click(object sender, RoutedEventArgs args)
    {
        var button = (MediaButton)sender;
        var export = App.Current.Services.GetRequiredService<ExportViewModel>();
        export.Open((MediaSearchResult)button.Tag);
    }

    private void EditSearch_Click(object sender, RoutedEventArgs args) =>
        ((MainWindow)App.Current.MainWindow).FocusSearch();

    // Results are cleared when a new search or page starts, which is when the view goes back to the top.
    private void Results_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            resultsScroll.ChangeView(
                horizontalOffset: null,
                verticalOffset: 0,
                zoomFactor: null,
                disableAnimation: true);
        }
    }
}
