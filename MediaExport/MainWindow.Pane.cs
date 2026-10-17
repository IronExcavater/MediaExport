// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Controls;
using MediaExport.Export;
using MediaExport.Export.Controls;
using MediaExport.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace MediaExport;

// What the navigation pane shows besides its pages: the labels that slide away on a compact pane, the recently
// viewed media, and the Activity item's badge.
public sealed partial class MainWindow
{
    // The pages' labels, which are text blocks of the window's own so that they can be given a transition.
    private readonly List<TextBlock> _paneLabels = [];

    private void RestoreRecentlyViewed()
    {
        var settings = _settings.Current;
        var unpinned = settings.RecentlyViewed.Select(media => new ViewedMedia(media));
        var pinned = settings.PinnedViewed.Select(media => new ViewedMedia(media));
        _export.RecentlyViewed.Load(unpinned, pinned);
        ShowRecentlyViewed();
        navView.IsCompactChanged += (_, _) => ShowPaneText();
        navView.PaneTransitionCompleted += (_, _) => ShowToolsWarning();

        _export.RecentlyViewed.CollectionChanged += (_, _) =>
        {
            var viewed = _export.RecentlyViewed;
            settings.PinnedViewed = viewed.Where(item => item.IsPinned).Select(item => item.Media).ToList();
            settings.RecentlyViewed = viewed.Where(item => !item.IsPinned).Select(item => item.Media).ToList();
            _settings.Save();

            // The list may be changing because one of its items in the pane was chosen, so the pane's items are
            // replaced once that has been dealt with.
            DispatcherQueue.TryEnqueue(ShowRecentlyViewed);
        };
    }

    // Lists the recently viewed media in the pane, under their heading. Each is opened again when it is chosen,
    // and is pinned or removed from its right-click menu.
    private void ShowRecentlyViewed()
    {
        var items = navView.MenuItems;
        foreach (var row in GetViewedRows())
            items.Remove(row);

        var viewed = _export.RecentlyViewed;
        foreach (var media in viewed)
            items.Add(CreateViewedItem(media));

        if (viewed.Count == 0)
            items.Add(CreateViewedItem(content: "Nothing viewed yet", ViewedTag.Empty, isEnabled: false));

        ShowPaneText();
    }

    // A compact pane has room for icons only. The pages' labels and the recently viewed section fade and slide
    // away as it narrows, and back as it widens.
    private void ShowPaneText()
    {
        var visibility = navView.IsCompact ? Visibility.Collapsed : Visibility.Visible;
        foreach (var label in _paneLabels)
            label.Visibility = visibility;

        viewedHeader.Visibility = visibility;
        foreach (var row in GetViewedRows())
            row.Visibility = visibility;

        ShowActivityBadge();
        ShowToolsWarning();
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SettingsViewModel.HasMissingTools)
            or nameof(SettingsViewModel.AreToolsBusy))
        {
            ShowToolsWarning();
        }
    }

    private void ShowToolsWarning()
    {
        toolsWarning.Visibility = Settings.HasMissingTools ? Visibility.Visible : Visibility.Collapsed;

        // Keep the caution visible in a compact pane while its explanatory content follows the same transition
        // as the other pane labels.
        // IsCompact changes before the pane begins animating. Waiting until it reaches its expanded width keeps
        // this multi-line content from wrapping into a tall strip while that width is still growing.
        var hasExpandedWidth = navView.OpenPaneLength >= navView.ExpandedPaneLength;
        var detailsVisibility = !navView.IsCompact && hasExpandedWidth
            ? Visibility.Visible
            : Visibility.Collapsed;
        toolsWarningDetails.Visibility = detailsVisibility;
        installMissingToolsButton.Visibility = detailsVisibility;
    }

    // The Activity item's badge is at the end of its row. A compact pane has no row, so there the badge takes
    // the icon's place for as long as it shows.
    private void ShowActivityBadge()
    {
        var isCompact = navView.IsCompact;
        var badgeStyle = isCompact ? "CompactProgressBadgeStyle" : "ProgressBadgeStyle";
        activityBadge.Style = (Style)root.Resources[badgeStyle];

        var isBadgeOverIcon = isCompact && Activity.HasActive;
        activityIcon.Opacity = isBadgeOverIcon ? 0 : 1;
    }

    // The badge shows only while exports are waiting or running, which changes whether it hides the icon.
    private void Activity_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ActivityViewModel.HasActive))
            ShowActivityBadge();
    }

    private void AddPaneLabels()
    {
        var pageItems = navView.MenuItems.Append(navView.SettingsItem).OfType<NavigationViewItem>();
        foreach (var item in pageItems)
        {
            if (item.Content is not string name)
                continue;

            // The item's content fills the row and the label centres itself in it. Were the content centred
            // instead, it would shrink to nothing as the label collapsed and move to the row's middle, taking
            // the label, which is still drawn while it fades, half a line down with it.
            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            item.VerticalContentAlignment = VerticalAlignment.Stretch;
            VisibilityTransition.SetSlide(label, true);

            // The name is still what a screen reader says, and what the item's tooltip is in a compact pane.
            AutomationProperties.SetName(item, name);
            item.Content = label;
            _paneLabels.Add(label);
        }
    }

    private List<NavigationViewItem> GetViewedRows()
    {
        var items = navView.MenuItems.OfType<NavigationViewItem>();
        return items.Where(item => item.Tag is ViewedTag or ViewedMedia).ToList();
    }

    // What marks the row that stands in for the list while it is empty.
    private enum ViewedTag
    {
        Empty
    }

    // A row in the recently viewed list, which is a little shorter than the pane's other items.
    private static NavigationViewItem CreateViewedItem(object content, object tag, bool isEnabled = true)
    {
        var item = new NavigationViewItem
        {
            Content = content,
            Tag = tag,
            IsEnabled = isEnabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            // Choosing it opens the media on the Export page, which is the item that then shows as selected.
            SelectsOnInvoked = false
        };
        item.Resources["NavigationViewItemOnLeftMinHeight"] = 30.0;
        VisibilityTransition.SetSlide(item, true);
        return item;
    }

    private NavigationViewItem CreateViewedItem(ViewedMedia viewed)
    {
        var provider = Search.Providers.FirstOrDefault(candidate => candidate.Id == viewed.Media.ProviderId);
        var row = new ViewedMediaRow(viewed, provider?.Icon);
        row.RemoveRequested += (_, _) => _export.RecentlyViewed.Remove(viewed);

        var pin = new MenuFlyoutItem
        {
            Text = viewed.IsPinned ? "Unpin" : "Pin",
            Icon = new FontIcon { Glyph = viewed.IsPinned ? "\uE77A" : "\uE718" }
        };
        pin.Click += (_, _) => _export.RecentlyViewed.TogglePin(viewed);

        var remove = new MenuFlyoutItem { Text = "Remove", Icon = new FontIcon { Glyph = "\uE711" } };
        remove.Click += (_, _) => _export.RecentlyViewed.Remove(viewed);

        var item = CreateViewedItem(row, viewed);
        item.ContextFlyout = new MenuFlyout { Items = { pin, remove } };
        item.PointerEntered += (_, _) => row.IsPointerOver = true;
        item.PointerExited += (_, _) => row.IsPointerOver = false;
        AutomationProperties.SetName(item, viewed.Media.Title);
        ToolTipService.SetToolTip(item, viewed.Media.Title);
        return item;
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is ViewedMedia viewed)
            _export.Open(viewed.Media);
    }
}
