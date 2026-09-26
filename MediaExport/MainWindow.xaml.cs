// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Settings;
using MediaExport.Export;
using MediaExport.Search;
using MediaExport.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.System;

namespace MediaExport;

public sealed partial class MainWindow : Window
{
    private const double CompactPaneLength = 48;
    private const double OpenPaneLength = 240;
    private static readonly TimeSpan PaneAnimationLength = TimeSpan.FromMilliseconds(200);

    private readonly SettingsStore _settings = App.Current.Services.GetRequiredService<SettingsStore>();
    private readonly ExportViewModel _export = App.Current.Services.GetRequiredService<ExportViewModel>();
    private DateTime _paneAnimationStart;
    private double _paneFrom;
    private double _paneTo;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureTitleBar();
        RestorePlacement();
        AppWindow.SetIcon("Assets/App/Square44x44Logo.ico");
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

    public SearchViewModel Search { get; } = App.Current.Services.GetRequiredService<SearchViewModel>();
    public ActivityViewModel Activity { get; } = App.Current.Services.GetRequiredService<ActivityViewModel>();
    public SettingsViewModel Settings { get; } = App.Current.Services.GetRequiredService<SettingsViewModel>();

    /// <summary>Shows <paramref name="media"/> on the Export page, whichever page asked.</summary>
    public void Open(MediaSearchResult media, MediaDetails? details = null)
    {
        details ??= Search.TakeLinkDetails(media);
        Search.Remember(media);
        _export.Open(media, details);
        GoTo(0);
    }

    /// <summary>Opens a pasted link; if it can't be opened, the search box says why.</summary>
    public async Task OpenLinkAsync(string url)
    {
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
        GoTo(0);
    }

    public void GoToActivity() => GoTo(1);

    public void FocusSearch() => searchBox.Focus(FocusState.Keyboard);

    private void GoTo(int index) => navView.SelectedItem = navView.MenuItems[index];

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (((ComboBox)sender).SelectedItem is IMediaSearchProvider provider)
            Search.SelectedProvider = provider;
    }

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

    // The title bar's caption-sized title is easy to miss; body size reads as the app's name.
    private void TitleBar_Loaded(object sender, RoutedEventArgs args)
    {
        if (FindDescendant<TextBlock>(titleBar, "PART_TitleText") is { } title)
            title.Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"];
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

    // NavigationView snaps its content to the new width while the pane animates (microsoft-ui-xaml#9370, closed
    // as won't fix). Keeping the pane "open" and easing its width moves and resizes the page with it, frame by frame.
    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        _settings.Current.IsPaneOpen = !_settings.Current.IsPaneOpen;
        _paneFrom = navView.OpenPaneLength;
        _paneTo = _settings.Current.IsPaneOpen ? OpenPaneLength : CompactPaneLength;
        _paneAnimationStart = DateTime.Now;
        _settings.Save();
        UpdatePaneToolTips();

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= AnimatePane;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += AnimatePane;
    }

    private void AnimatePane(object? sender, object args)
    {
        double progress = Math.Min(1, (DateTime.Now - _paneAnimationStart) / PaneAnimationLength);
        double eased = 1 - Math.Pow(1 - progress, 3); // Decelerates, like Fluent's own motion.
        navView.OpenPaneLength = _paneFrom + (_paneTo - _paneFrom) * eased;
        if (progress >= 1)
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= AnimatePane;
    }

    // With the pane narrowed its labels are hidden, so the icons say what they are on hover instead.
    private void UpdatePaneToolTips()
    {
        bool compact = !_settings.Current.IsPaneOpen;
        foreach (object item in navView.MenuItems)
        {
            if (item is NavigationViewItem navigationItem)
                ToolTipService.SetToolTip(navigationItem, compact ? navigationItem.Content : null);
        }

        if (navView.SettingsItem is NavigationViewItem settings)
            ToolTipService.SetToolTip(settings, compact ? "Settings" : null);
    }

    private void ProviderBox_DropDownOpened(object? sender, object args) => Search.IsSourceListOpen = true;

    private void ProviderBox_DropDownClosed(object? sender, object args)
    {
        Search.IsSourceListOpen = false;
        searchBox.Focus(FocusState.Programmatic);
    }

    // The box only shows its clear button while focused; this keeps it whenever there's text to clear.
    private void SearchBox_Loaded(object sender, RoutedEventArgs args)
    {
        if (FindDescendant<TextBox>(searchBox) is not { } textBox)
            return;

        void ShowClearButton()
        {
            if (textBox.FocusState == FocusState.Unfocused)
                VisualStateManager.GoToState(textBox, textBox.Text.Length > 0 ? "ButtonVisible" : "ButtonCollapsed", false);
        }

        // Queued, so it runs after the box's own focus handling has hidden the button.
        textBox.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ShowClearButton);
        textBox.TextChanged += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ShowClearButton);
        ShowClearButton();
    }

    private static T? FindDescendant<T>(DependencyObject parent, string? name = null) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && (name is null || match.Name == name))
                return match;
            if (FindDescendant<T>(child, name) is { } nested)
                return nested;
        }

        return null;
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FocusSearch();
        args.Handled = true;
    }

    private void GoTo_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        GoTo(sender.Key == VirtualKey.Number1 ? 0 : 1);
        args.Handled = true;
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs args) => Search.ShowSuggestions();

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason is not AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        Search.SearchText = sender.Text;
        _ = Search.SearchAsync();
    }

    // Only a click or Enter opens something; arrowing through suggestions shouldn't load each one.
    // Enter without a suggestion, or on a message ("No results", an error), searches straight away.
    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is SearchSuggestion { Media: { } media })
        {
            Open(media);
            return;
        }

        Search.SearchText = sender.Text;
        _ = Search.SearchAsync(debounce: false);
    }

    private void NavView_Loaded(object sender, RoutedEventArgs args)
    {
        UpdatePaneToolTips();
        GoTo(0);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type? pageType = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : ((args.SelectedItemContainer as NavigationViewItem)?.Tag as string) switch
            {
                "Export" => typeof(ExportPage),
                "Activity" => typeof(ActivityPage),
                _ => null
            };

        if (pageType is not null && navFrame.CurrentSourcePageType != pageType)
            navFrame.Navigate(pageType);
    }
}
