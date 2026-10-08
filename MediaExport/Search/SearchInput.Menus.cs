// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Controls;
using MediaExport.Core.Media.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Globalization;
using Windows.System;

namespace MediaExport.Search;

public sealed partial class SearchInput
{
    private static readonly (string Label, SearchSort Value, string Glyph)[] RankingChoices =
    [
        ("Relevancy", SearchSort.Relevance, "\uE8D0"),
        ("Popularity", SearchSort.Popularity, "\uE8E1")
    ];

    private static readonly (string Label, SearchArrangement Value, string Glyph)[] ArrangementChoices =
    [
        ("In ranked order", SearchArrangement.AsRanked, "\uE8CB"),
        ("Newest first", SearchArrangement.NewestFirst, "\uE787"),
        ("Oldest first", SearchArrangement.OldestFirst, "\uE787"),
        ("Title, A to Z", SearchArrangement.TitleAToZ, "\uE8D2"),
        ("Title, Z to A", SearchArrangement.TitleZToA, "\uE8D2")
    ];

    private void Provider_Click(object sender, RoutedEventArgs args)
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        foreach (IMediaSearchProvider provider in ViewModel.Providers)
        {
            ImageIcon? icon = IconView.UriFor(provider.Icon, ActualTheme) is { } uri
                ? new ImageIcon { Source = new SvgImageSource(uri) }
                : null;
            ToggleMenuFlyoutItem item = ChoiceMenu.Single(
                provider.DisplayName, provider, icon, provider == ViewModel.SelectedProvider);
            item.Click += (_, _) =>
            {
                item.IsChecked = true;
                ViewModel.SelectedProvider = provider;
                if (ViewModel.QueryText.Length > 0)
                    _ = ViewModel.SearchAsync(debounce: false);
            };
            menu.Items.Add(item);
        }
        menu.Opening += Menu_Opening;
        menu.Closed += Menu_Closed;
        menu.ShowAt(providerButton);
    }

    private void AddMenu_Opening(object? sender, object args)
    {
        ViewModel.IsMenuOpen = true;
        addMenu.Items.Clear();
        foreach (SearchTermDefinition definition in ViewModel.Draft.AvailableTerms)
        {
            var item = new MenuFlyoutItem
            {
                Text = definition.Type switch
                {
                    SearchTermType.ExactPhrase => "Exact",
                    SearchTermType.ExcludedWord => "Exclude",
                    SearchTermType.Creator => "By a creator",
                    SearchTermType.LongerThan => "Longer than",
                    SearchTermType.ShorterThan => "Shorter than",
                    SearchTermType.Before => "Before",
                    SearchTermType.After => "After",
                    _ => definition.Label
                },
                Icon = new FontIcon
                {
                    Glyph = new SearchTerm(definition, string.Empty).Glyph,
                    FontFamily = definition.Type is SearchTermType.ExactPhrase or SearchTermType.ExcludedWord
                        ? new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Symbol")
                        : new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons")
                },
                Tag = definition.Type
            };
            item.Click += AddTerm_Click;
            addMenu.Items.Add(item);
        }
    }

    private void AddTerm_Click(object sender, RoutedEventArgs args)
    {
        SearchTermType type = (SearchTermType)((FrameworkElement)sender).Tag;
        string value = type switch
        {
            SearchTermType.LongerThan or SearchTermType.ShorterThan => "10m",
            SearchTermType.Before or SearchTermType.After => DateTimeOffset.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => string.Empty
        };
        SearchTerm term = ViewModel.Draft.Add(type, value);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (termItems.ContainerFromItem(term) is { } container && FindDescendant<Button>(container, "termButton") is { } button)
                ShowTermEditor(button, term);
        });
    }

    private void Term_Click(object sender, RoutedEventArgs args) =>
        ShowTermEditor((FrameworkElement)sender, (SearchTerm)((FrameworkElement)sender).Tag);

    private void RemoveTerm_Click(object sender, RoutedEventArgs args) =>
        ViewModel.Draft.Remove((SearchTerm)((FrameworkElement)sender).Tag);

    private void ShowTermEditor(FrameworkElement target, SearchTerm term)
    {
        FlyoutBase flyout = term.Definition.Kind switch
        {
            SearchTermKind.Length => ChoiceOf(term, ("1 minute", "1m"), ("5 minutes", "5m"), ("10 minutes", "10m"), ("30 minutes", "30m"), ("1 hour", "1h"), ("2 hours", "2h")),
            SearchTermKind.Date => DayFor(term),
            _ => TextFor(term)
        };
        flyout.Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft;
        flyout.Opening += Menu_Opening;
        flyout.Closed += Menu_Closed;
        flyout.ShowAt(target);
    }

    private static MenuFlyout ChoiceOf(SearchTerm term, params (string Name, string Value)[] choices)
    {
        var menu = new MenuFlyout();
        foreach ((string name, string value) in choices)
        {
            ToggleMenuFlyoutItem item = ChoiceMenu.Single(name, value, "\uE916", term.Value == value);
            item.Click += (_, _) =>
            {
                item.IsChecked = true;
                term.Value = value;
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private static Flyout DayFor(SearchTerm term)
    {
        var calendar = new CalendarView { SelectionMode = CalendarViewSelectionMode.Single };
        var flyout = new Flyout { Content = calendar };
        if (DateTimeOffset.TryParse(term.Value.Length == 4 ? $"{term.Value}-01-01" : term.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset day))
            calendar.SetDisplayDate(day);
        calendar.SelectedDatesChanged += (_, picked) =>
        {
            if (picked.AddedDates.Count == 0)
                return;
            term.Value = picked.AddedDates[0].LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            flyout.Hide();
        };
        return flyout;
    }

    private Flyout TextFor(SearchTerm term)
    {
        var box = new TextBox { Text = term.Value, Width = 240, PlaceholderText = term.DisplayName, IsSpellCheckEnabled = false };
        var flyout = new Flyout { Content = box };
        box.KeyDown += (_, key) =>
        {
            if (key.Key == VirtualKey.Enter)
                flyout.Hide();
        };
        flyout.Opened += (_, _) => box.SelectAll();
        flyout.Closed += (_, _) =>
        {
            if (box.Text.Trim().Length == 0)
                ViewModel.Draft.Remove(term);
            else
                term.Value = box.Text.Trim();
        };
        return flyout;
    }

    private void SortMenu_Opening(object? sender, object args)
    {
        ViewModel.IsMenuOpen = true;
        sortMenu.Items.Clear();
        sortMenu.Items.Add(ChoiceMenu.Header("Rank results by"));
        foreach ((string label, SearchSort value, string glyph) in RankingChoices)
        {
            ToggleMenuFlyoutItem item = ChoiceMenu.Single(label, value, glyph, ViewModel.Sort == value);
            item.Click += Sort_Click;
            sortMenu.Items.Add(item);
        }

        sortMenu.Items.Add(new MenuFlyoutSeparator());
        sortMenu.Items.Add(ChoiceMenu.Header("Arrange results"));
        foreach ((string label, SearchArrangement value, string glyph) in ArrangementChoices)
        {
            ToggleMenuFlyoutItem item = ChoiceMenu.Single(label, value, glyph, ViewModel.Arrangement == value);
            item.Click += Sort_Click;
            sortMenu.Items.Add(item);
        }
    }

    private void Sort_Click(object sender, RoutedEventArgs args)
    {
        ((ToggleMenuFlyoutItem)sender).IsChecked = true;
        object choice = ((FrameworkElement)sender).Tag;
        if (choice is SearchSort sort)
            ViewModel.Sort = sort;
        else
            ViewModel.Arrangement = (SearchArrangement)choice;
    }

    private void Menu_Opening(object? sender, object args) => ViewModel.IsMenuOpen = true;

    private void Menu_Closed(object? sender, object args) => ViewModel.IsMenuOpen = false;
}
