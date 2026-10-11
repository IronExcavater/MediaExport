// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.Specialized;
using System.Globalization;
using System.Threading.Tasks;
using VirtualKey = Windows.System.VirtualKey;

namespace MediaExport.Search;

public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
        ViewModel.Results.CollectionChanged += Results_CollectionChanged;
    }

    public SearchViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<SearchViewModel>();

    private string PageText(int page) => page.ToString(CultureInfo.CurrentCulture);

    // Results are cleared when a new search or page starts, which is when the view goes back to the top.
    private void Results_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
            resultsScroll.ChangeView(horizontalOffset: null, verticalOffset: 0, zoomFactor: null, disableAnimation: true);
    }

    private void PageBox_GotFocus(object sender, RoutedEventArgs args) => pageBox.SelectAll();

    private async void PageBox_LostFocus(object sender, RoutedEventArgs args) => await GoToTypedPageAsync();

    private async void PageBox_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
            return;

        args.Handled = true;
        await GoToTypedPageAsync();
    }

    private async Task GoToTypedPageAsync()
    {
        var isNumber = int.TryParse(pageBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var page);
        var isAnotherPage = isNumber && page >= 1 && page != ViewModel.Page;
        if (isAnotherPage)
            await ViewModel.GoToPageAsync(page);

        // Whatever was typed is replaced by the page that is showing, which undoes anything that isn't a page.
        pageBox.Text = PageText(ViewModel.Page);
    }
}
