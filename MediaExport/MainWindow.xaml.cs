// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Media.Ytdlp;
using MediaExport.Core.Settings;
using MediaExport.Export;
using MediaExport.Search;
using MediaExport.Services;
using MediaExport.Settings;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;

namespace MediaExport;

public sealed partial class MainWindow : Window
{
    private const double CompactPaneLength = 56;
    private const double OpenPaneLength = 240;
    private const int PaneRecentCount = 6;
    private const int PageCount = 3;
    private static readonly TimeSpan PaneOpenDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PaneCloseDuration = TimeSpan.FromMilliseconds(180);

    private readonly SettingsStore _settings;
    private readonly ExportViewModel _export;
    private TimeSpan? _paneAnimationStart;
    private TimeSpan _paneDuration;
    private double _paneFrom;
    private double _paneTo;
    private bool _recentsQueued;

    public MainWindow(SettingsStore settings, AppViewModels viewModels)
    {
        _settings = settings;
        _export = viewModels.Export;
        Search = viewModels.Search;
        Results = viewModels.Results;
        Activity = viewModels.Activity;
        Settings = viewModels.Settings;

        InitializeComponent();
        ConfigureTitleBar();
        RestorePlacement();
        ShowRecentsInPane();
        Search.Recents.CollectionChanged += (_, _) => QueueRecentsInPane();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "App", "Square44x44Logo.ico"));
        AppWindow.Closing += (_, _) => SavePlacement();

        var openSettings = new KeyboardAccelerator { Key = (VirtualKey)188, Modifiers = VirtualKeyModifiers.Control }; // Ctrl+,
        openSettings.Invoked += (_, args) =>
        {
            navView.SelectedItem = navView.SettingsItem;
            args.Handled = true;
        };
        navView.Loaded += (_, _) => ((UIElement)navView.SettingsItem).KeyboardAccelerators.Add(openSettings);

        navView.OpenPaneLength = _settings.Current.IsPaneOpen ? OpenPaneLength : CompactPaneLength;
        ApplyTheme();
        Settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.Theme))
                ApplyTheme();
        };
    }

    public SearchViewModel Search { get; }
    public ResultsViewModel Results { get; }
    public ActivityViewModel Activity { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>Shows <paramref name="media"/> on the Export page, whichever page asked.</summary>
    public void Open(MediaSearchResult media, MediaDetails? details = null)
    {
        details ??= Search.TakeLinkDetails(media);
        Search.Remember(media);
        _export.Open(media, details);
        GoTo("Export");
    }

    /// <summary>Opens a pasted link; if it can't be opened, the search box says why.</summary>
    public async Task OpenLinkAsync(string url)
    {
        if (YtdlpService.IsYouTubePlaylist(url))
        {
            Search.Replace(url);
            ShowResults();
            return;
        }

        Search.IsSearching = true;
        try
        {
            Open(await Search.OpenLinkAsync(url));
        }
        catch (Exception)
        {
            Search.SearchText = url;
            FocusSearch();
            await Search.SearchAsync(debounce: false);
        }
        finally
        {
            Search.IsSearching = false;
        }
    }

    public void ExportAgain(ExportJob job)
    {
        Search.Remember(job.Media);
        _export.OpenAgain(job);
        GoTo("Export");
    }

    public void GoToActivity() => GoTo("Activity");

    private void GoTo(string page) =>
        navView.SelectedItem = navView.MenuItems.OfType<NavigationViewItem>().First(item => item.Tag as string == page);

    private void ApplyTheme()
    {
        (root.RequestedTheme, AppWindow.TitleBar.PreferredTheme) = Settings.Theme switch
        {
            "Light" => (ElementTheme.Light, TitleBarTheme.Light),
            "Dark" => (ElementTheme.Dark, TitleBarTheme.Dark),
            _ => (ElementTheme.Default, TitleBarTheme.UseDefaultAppMode)
        };
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private void Root_Loaded(object sender, RoutedEventArgs args)
    {
        // Below this the export page's two columns and the title bar search stop fitting.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            double scale = root.XamlRoot.RasterizationScale;
            presenter.PreferredMinimumWidth = (int)(860 * scale);
            presenter.PreferredMinimumHeight = (int)(600 * scale);
        }
    }

    // A link dragged in from a browser (its address bar or a link on the page) opens like a pasted one.
    private void Root_DragOver(object sender, DragEventArgs args)
    {
        if (args.DataView.Contains(StandardDataFormats.WebLink) || args.DataView.Contains(StandardDataFormats.Text))
        {
            args.AcceptedOperation = DataPackageOperation.Link;
            args.DragUIOverride.Caption = "Open";
        }
    }

    private async void Root_Drop(object sender, DragEventArgs args)
    {
        string? text = args.DataView.Contains(StandardDataFormats.WebLink) ? (await args.DataView.GetWebLinkAsync()).AbsoluteUri
            : args.DataView.Contains(StandardDataFormats.Text) ? (await args.DataView.GetTextAsync()).Trim()
            : null;
        if (text is not null && SearchViewModel.IsLink(text))
            await OpenLinkAsync(text);
    }

    private void RestorePlacement()
    {
        if (_settings.Current.Window is not { } placement)
            return;

        var bounds = new RectInt32(placement.X, placement.Y, placement.Width, placement.Height);
        // Only if it's still on a screen; a monitor may have been unplugged since.
        if (DisplayArea.GetFromRect(bounds, DisplayAreaFallback.None) is null)
            return;

        AppWindow.MoveAndResize(bounds);
        if (placement.IsMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Maximize();
    }

    private void SavePlacement()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter || presenter.State == OverlappedPresenterState.Minimized)
            return;

        bool maximized = presenter.State == OverlappedPresenterState.Maximized;
        if (maximized)
            presenter.Restore(); // So the saved size is the one it goes back to.

        _settings.Current.Window = new WindowPlacement(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height, maximized);
        _settings.Save();
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        _settings.Current.IsPaneOpen = !_settings.Current.IsPaneOpen;
        _paneFrom = navView.OpenPaneLength;
        _paneTo = _settings.Current.IsPaneOpen ? OpenPaneLength : CompactPaneLength;
        _paneDuration = (_settings.Current.IsPaneOpen ? PaneOpenDuration : PaneCloseDuration)
            * (Math.Abs(_paneTo - _paneFrom) / (OpenPaneLength - CompactPaneLength));
        _paneAnimationStart = null;
        _settings.Save();
        UpdatePaneItems();
        ShowRecentsInPane();

        CompositionTarget.Rendering -= AnimatePane;
        CompositionTarget.Rendering += AnimatePane;
    }

    // The pane stays logically open while its width animates, so the frame is resized instead of being wrapped or clipped.
    private void AnimatePane(object? sender, object args)
    {
        TimeSpan now = ((RenderingEventArgs)args).RenderingTime;
        _paneAnimationStart ??= now;
        double progress = _paneDuration > TimeSpan.Zero ? Math.Min(1, (now - _paneAnimationStart.Value) / _paneDuration) : 1;
        double eased = 1 - Math.Pow(1 - progress, 4);
        navView.OpenPaneLength = _paneFrom + (_paneTo - _paneFrom) * eased;
        if (progress >= 1)
            CompositionTarget.Rendering -= AnimatePane;
    }

    // With the pane narrowed its labels are hidden, so the icons say what they are on hover instead.
    private void UpdatePaneItems()
    {
        bool compact = !_settings.Current.IsPaneOpen;
        foreach (NavigationViewItem item in navView.MenuItems.OfType<NavigationViewItem>().Append(navView.SettingsItem as NavigationViewItem).OfType<NavigationViewItem>())
            ToolTipService.SetToolTip(item, compact ? item.Content ?? "Settings" : null);
    }

    private void GoTo_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        GoTo(sender.Key switch { VirtualKey.Number1 => "Export", VirtualKey.Number2 => "Search", _ => "Activity" });
        args.Handled = true;
    }

    private void NavView_Loaded(object sender, RoutedEventArgs args)
    {
        UpdatePaneItems();
        GoTo("Export");
    }

    // A pick changes the recents a few times over (moved up, the oldest dropped); the pane is rebuilt once after.
    private void QueueRecentsInPane()
    {
        if (_recentsQueued)
            return;

        _recentsQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _recentsQueued = false;
            ShowRecentsInPane();
        });
    }

    // The pane's spare room lists the latest picks under the pages; narrowed, there's no room to say what they are, so they go.
    private void ShowRecentsInPane()
    {
        while (navView.MenuItems.Count > PageCount)
            navView.MenuItems.RemoveAt(PageCount);
        if (Search.Recents.Count == 0 || !_settings.Current.IsPaneOpen)
            return;

        navView.MenuItems.Add(new NavigationViewItemHeader { Content = "Recent" });
        foreach (MediaSearchResult media in Search.Recents.Take(PaneRecentCount))
        {
            var item = new NavigationViewItem
            {
                Content = media.Title,
                Tag = media,
                Icon = new ImageIcon { Source = media.ThumbnailUri is { } thumbnail ? new BitmapImage(thumbnail) { DecodePixelWidth = 64 } : null }
            };
            ToolTipService.SetToolTip(item, media.Title);
            navView.MenuItems.Add(item);
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if ((args.SelectedItemContainer as NavigationViewItem)?.Tag is MediaSearchResult media)
        {
            // Opening it selects the Export page instead, which can't happen mid-selection.
            DispatcherQueue.TryEnqueue(() => Open(media));
            return;
        }

        Type? pageType = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : ((args.SelectedItemContainer as NavigationViewItem)?.Tag as string) switch
            {
                "Export" => typeof(ExportPage),
                "Search" => typeof(SearchPage),
                "Activity" => typeof(ActivityPage),
                _ => null
            };

        if (pageType is null || navFrame.CurrentSourcePageType == pageType)
            return;

        Search.IsPageSearch = pageType == typeof(SearchPage);
        navFrame.Navigate(pageType);
    }
}
