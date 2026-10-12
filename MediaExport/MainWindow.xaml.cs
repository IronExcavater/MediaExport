// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Controls;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Settings;
using MediaExport.Export;
using MediaExport.Search;
using MediaExport.Settings;
using MediaExport.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Graphics;

namespace MediaExport;

public sealed partial class MainWindow : Window
{
    // Maps each navigation item's Tag to the page it opens.
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Search"] = typeof(SearchPage),
        ["Export"] = typeof(ExportPage),
        ["Activity"] = typeof(ActivityPage)
    };

    // How long the window must keep its size before the app name slides, so it doesn't flicker mid-resize.
    private static readonly TimeSpan ResizeDebounce = TimeSpan.FromMilliseconds(400);

    private readonly SettingsStore _settings = App.Current.Services.GetRequiredService<SettingsStore>();
    private readonly DispatcherQueueTimer _resizeDebounceTimer;
    private bool _isAppNameHidden;

    /// <summary>The search state shared by the title bar and Search page.</summary>
    public SearchViewModel Search { get; } = App.Current.Services.GetRequiredService<SearchViewModel>();

    private readonly ExportViewModel _export = App.Current.Services.GetRequiredService<ExportViewModel>();

    public MainWindow()
    {
        InitializeComponent();
        navView.IsExpanded = _settings.Current.IsPaneOpen;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        // The window icon has to be given as a file path; it can't come from Icons.xaml.
        AppWindow.SetIcon("Assets/App/Square44x44Logo.ico");

        RestoreWindowSize();
        RestoreSearch();

        // Whatever opens media, the Export page is where it shows.
        _export.Opened += (_, _) => GoTo("Export");
        RestoreRecentlyViewed();
        Closed += (_, _) =>
        {
            _settings.Current.LastSearch = Search.FullText;
            SaveWindowSize();
        };

        _resizeDebounceTimer = DispatcherQueue.CreateTimer();
        _resizeDebounceTimer.Interval = ResizeDebounce;
        _resizeDebounceTimer.IsRepeating = false;
        _resizeDebounceTimer.Tick += (_, _) => UpdateAppName();
    }

    // The minimum size and the caption buttons' width are in screen pixels, so both are converted by display scale.
    private void Root_Loaded(object sender, RoutedEventArgs args)
    {
        var scale = root.XamlRoot.RasterizationScale;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(WindowSizes.MinWidth * scale);
            presenter.PreferredMinimumHeight = (int)(WindowSizes.MinHeight * scale);
        }

        // TitleBar reserves room for the caption buttons using their width in screen pixels as if it
        // were view pixels, so above 100% display scale it reserves too much and leaves a gap beside
        // them (microsoft-ui-xaml#10344). A negative right margin takes the
        // excess back.
        // The app name slides between no width and its full width. A storyboard can't animate from
        // MaxWidth's default, infinity, so the full width is set as a number.
        appName.MaxWidth = appNameText.ActualWidth;
        showAppNameWidth.To = appNameText.ActualWidth;

        double reservedWidth = AppWindow.TitleBar.RightInset;
        var neededWidth = reservedWidth / scale;
        titleBar.Margin = new Thickness(0, 0, neededWidth - reservedWidth, 0);
    }

    // The app opens on the search it was closed with, or on what is popular. The results are usually remembered
    // too, so they show at once.
    private void RestoreSearch()
    {
        Search.LoadRecentSearches(_settings.Current.RecentSearches, _settings.Current.PinnedSearches);
        Search.RecentSearches.CollectionChanged += (_, _) =>
        {
            var pinned = Search.RecentSearches.Where(recent => recent.IsPinned);
            var unpinned = Search.RecentSearches.Where(recent => !recent.IsPinned);
            _settings.Current.PinnedSearches = pinned.Select(recent => recent.FullText).ToList();
            _settings.Current.RecentSearches = unpinned.Select(recent => recent.FullText).ToList();
            _settings.Save();
        };

        var lastSearch = _settings.Current.LastSearch;
        if (!string.IsNullOrWhiteSpace(lastSearch))
            Search.Replace(lastSearch);

        Search.SearchCommand.Execute(null);
    }

    private void RestoreRecentlyViewed()
    {
        var settings = _settings.Current;
        var unpinned = settings.RecentlyViewed.Select(media => new ViewedMedia(media));
        var pinned = settings.PinnedViewed.Select(media => new ViewedMedia(media));
        _export.RecentlyViewed.Load(unpinned, pinned);
        ShowRecentlyViewed();
        navView.IsCompactChanged += (_, _) => ShowViewedSection();

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

        ShowViewedSection();
    }

    // A compact pane has no room for the section, which fades and slides away with its heading.
    private void ShowViewedSection()
    {
        var visibility = navView.IsCompact ? Visibility.Collapsed : Visibility.Visible;
        viewedHeader.Visibility = visibility;
        foreach (var row in GetViewedRows())
            row.Visibility = visibility;
    }

    private List<NavigationViewItem> GetViewedRows()
    {
        var items = navView.MenuItems.OfType<NavigationViewItem>();
        return items.Where(item => item.Tag is ViewedTag or ViewedMedia).ToList();
    }

    private const double RowEndInset = -8;

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
        var icon = new IconView { Width = 16, Height = 16, Icon = provider?.Icon };
        var title = new TextBlock
        {
            Text = viewed.Media.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        // A pinned one keeps a pin at its end. The remove button takes that place while the pointer is over the
        // row, and takes no room otherwise, so the title has the row's width.
        var pinMark = new FontIcon
        {
            Glyph = "\uE840",
            FontSize = 12,
            Margin = new Thickness(0, 0, 6, 0),
            Visibility = viewed.IsPinned ? Visibility.Visible : Visibility.Collapsed
        };
        var removeButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 10 },
            Style = (Style)Application.Current.Resources["RowIconButtonStyle"],
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(removeButton, "Remove");
        ToolTipService.SetToolTip(removeButton, "Remove");
        removeButton.Click += (_, _) => _export.RecentlyViewed.Remove(viewed);

        // The negative margin puts the button in the row's corner, past the padding a pane item has on its right.
        var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, RowEndInset, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(title, 1);
        Grid.SetColumn(pinMark, 2);
        Grid.SetColumn(removeButton, 2);
        row.Children.Add(icon);
        row.Children.Add(title);
        row.Children.Add(pinMark);
        row.Children.Add(removeButton);

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
        item.PointerEntered += (_, _) => ShowRemoveButton(isShown: true);
        item.PointerExited += (_, _) => ShowRemoveButton(isShown: false);
        AutomationProperties.SetName(item, viewed.Media.Title);
        ToolTipService.SetToolTip(item, viewed.Media.Title);
        return item;

        void ShowRemoveButton(bool isShown)
        {
            var showsPin = viewed.IsPinned && !isShown;
            removeButton.Visibility = isShown ? Visibility.Visible : Visibility.Collapsed;
            pinMark.Visibility = showsPin ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is ViewedMedia viewed)
            _export.Open(viewed.Media);
    }

    // Windows doesn't remember the size of a WinUI window between runs, so the app does.
    private void RestoreWindowSize()
    {
        var settings = _settings.Current;
        if (settings.WindowWidth is not null && settings.WindowHeight is not null)
            AppWindow.Resize(new SizeInt32(settings.WindowWidth.Value, settings.WindowHeight.Value));

        if (settings.IsWindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Maximize();
    }

    private void SaveWindowSize()
    {
        var settings = _settings.Current;
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        var state = presenter?.State;
        settings.IsWindowMaximized = state == OverlappedPresenterState.Maximized;

        // A maximised or minimised window keeps the size saved before, which is the size it returns to.
        if (state == OverlappedPresenterState.Restored)
        {
            settings.WindowWidth = AppWindow.Size.Width;
            settings.WindowHeight = AppWindow.Size.Height;
        }

        _settings.Save();
    }

    /// <summary>Navigates to a page by its key in <see cref="Pages"/>.</summary>
    public void GoTo(string page)
    {
        foreach (var item in navView.MenuItems)
        {
            if (item is NavigationViewItem { Tag: string tag } && tag == page)
                navView.SelectedItem = item;
        }
    }

    private void SearchInput_Submitted(object? sender, EventArgs args)
    {
        GoTo("Search");
        Search.CommitSearchCommand.Execute(null);
    }

    private void SearchInput_ResultChosen(object? sender, MediaSearchResult media) => _export.Open(media);

    // A link that can't be opened is explained under the field it was entered in.
    private async void SearchInput_LinkSubmitted(object? sender, Uri link)
    {
        // A link to a creator becomes a search for them. Any other is opened, or said to be one that can't be.
        searchInput.Message = null;
        var isCreator = Search.GetLinkKind(link) == LinkKind.Creator;
        searchInput.Message = isCreator ? await Search.SearchCreatorAsync(link) : await _export.OpenLinkAsync(link);
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        searchInput.FocusQueryBox();
        args.Handled = true;
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        navView.TogglePane();
        _settings.Current.IsPaneOpen = navView.IsExpanded;
        _settings.Save();
    }

    private void TitleBar_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer.Start();
    }

    // Slides the app name behind the logo on a compact window, giving its space to the search field.
    private void UpdateAppName()
    {
        var isCompact = titleBar.ActualWidth < WindowSizes.Compact;
        if (isCompact == _isAppNameHidden)
            return;

        _isAppNameHidden = isCompact;
        var slide = isCompact ? hideAppName : showAppName;
        slide.Begin();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs args) => GoTo("Search");

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type? pageType = null;
        if (args.IsSettingsSelected)
            pageType = typeof(SettingsPage);
        else if (args.SelectedItemContainer?.Tag is string tag)
            pageType = Pages.GetValueOrDefault(tag);

        // The Search page shows the results itself, so the field's own list of them stays shut there.
        searchInput.IsResultsListEnabled = pageType != typeof(SearchPage);

        var isAlreadyShown = navFrame.CurrentSourcePageType == pageType;
        if (pageType is not null && !isAlreadyShown)
            navFrame.Navigate(pageType);
    }

    public void ApplyTheme(ElementTheme theme) => root.RequestedTheme = theme;
}
