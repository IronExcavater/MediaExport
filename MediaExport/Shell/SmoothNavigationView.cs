// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.System;

namespace MediaExport.Shell;

/// <summary>A navigation view whose pane slides between two widths, resizing the page beside it as it goes.</summary>
/// <remarks>
/// NavigationView's own collapse snaps the page to its new width before the pane has finished moving
/// (microsoft-ui-xaml#9370). This workaround keeps the pane open and animates its width instead, so the pane
/// and the page move together.
/// <para>
/// When the control is too narrow for an expanded pane (see <see cref="AutoCollapseWidth"/>), the pane can
/// still be opened with <see cref="TogglePane"/>: it then opens over the page, as an overlay that closes
/// when the page is clicked, an item is chosen or Escape is pressed. The pane is widened as usual, but the
/// page is held where it was (see <see cref="PlacePage"/>) instead of being pushed aside and resized.
/// </para>
/// </remarks>
public sealed partial class SmoothNavigationView : NavigationView
{
    // The pane's width animation: when it started, how long it lasts, and the widths it runs between.
    // _startTime is null while the pane is still.
    private TimeSpan? _startTime;
    private TimeSpan _duration;
    private double _startLength;
    private double _targetLength;

    // Whether the pane is open over the page. Only possible while the control is too narrow.
    private bool _isOverlayOpen;

    // Whether the page is being held in place under the pane. True from an overlay opening until it has
    // finished closing, so the page doesn't move while the pane shrinks back.
    private bool _isPaneOverPage;

    public SmoothNavigationView()
    {
        // As far as NavigationView knows the pane is always open; only OpenPaneLength changes.
        PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        IsPaneOpen = true;
        IsPaneToggleButtonVisible = false;
        IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        OpenPaneLength = ExpandedPaneLength;

        Loaded += (_, _) => UpdateToolTips();
        SizeChanged += (_, _) => UpdatePane();

        // The ways an overlay is dismissed. The handlers are added with handledEventsToo, because controls
        // on the page mark the presses and keys they use as handled.
        ItemInvoked += (_, _) => CloseOverlay();
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
    }

    /// <summary>Whether the user wants the pane expanded. It still collapses below <see cref="AutoCollapseWidth"/>.</summary>
    public bool IsExpanded
    {
        get;
        set { field = value; UpdatePane(); }
    } = true;

    /// <summary>The control width below which the pane collapses by itself. 0 turns this off.</summary>
    public double AutoCollapseWidth
    {
        get;
        set { field = value; UpdatePane(); }
    }

    /// <summary>Whether the pane shows icons only right now, by the user's choice or for lack of room.</summary>
    public bool IsCompact { get; private set; }

    private bool IsTooNarrow => ActualWidth < AutoCollapseWidth;

    public event EventHandler? IsCompactChanged;

    /// <summary>The pane's width when it shows icons and names.</summary>
    public double ExpandedPaneLength
    {
        get;
        set { field = value; UpdatePane(); }
    } = 240;

    /// <summary>The pane's width when it shows icons only. Below about 50 the selection highlight is clipped.</summary>
    public double CollapsedPaneLength
    {
        get;
        set { field = value; UpdatePane(); }
    } = 50;

    /// <summary>How long a full expand takes.</summary>
    public TimeSpan ExpandDuration { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a full collapse takes.</summary>
    public TimeSpan CollapseDuration { get; set; } = TimeSpan.FromMilliseconds(180);

    /// <summary>
    /// What the pane toggle button does. With enough room it expands or collapses the pane, changing
    /// <see cref="IsExpanded"/>. Without, it opens or closes the pane as an overlay and leaves
    /// <see cref="IsExpanded"/> alone.
    /// </summary>
    public void TogglePane()
    {
        if (IsTooNarrow)
            _isOverlayOpen = !_isOverlayOpen;
        else
            IsExpanded = !IsExpanded;

        UpdatePane();
    }

    private void CloseOverlay()
    {
        if (!_isOverlayOpen)
            return;

        _isOverlayOpen = false;
        UpdatePane();
    }

    // A press on the page, to the right of the pane, dismisses the overlay.
    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var x = args.GetCurrentPoint(this).Position.X;
        var isOnPage = x > OpenPaneLength;
        if (isOnPage)
            CloseOverlay();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
            CloseOverlay();
    }

    // Decides how wide the pane should be and, if that has changed, starts animating to it.
    // Called whenever a property or the control's size changes.
    private void UpdatePane()
    {
        // With room for the pane beside the page, an overlay is no longer needed.
        if (!IsTooNarrow)
            _isOverlayOpen = false;

        var isCollapsed = !IsExpanded || IsTooNarrow;
        var isCompact = isCollapsed && !_isOverlayOpen;
        if (isCompact != IsCompact)
        {
            IsCompact = isCompact;
            UpdateToolTips();
            IsCompactChanged?.Invoke(this, EventArgs.Empty);
        }

        // An overlay holds the page in place from the moment it opens. An expanded pane must not: the page
        // belongs beside it. A closing overlay keeps holding the page until OnRendering has finished.
        if (_isOverlayOpen)
            _isPaneOverPage = true;
        else if (!isCompact)
            _isPaneOverPage = false;

        var targetLength = isCompact ? CollapsedPaneLength : ExpandedPaneLength;
        if (targetLength == _targetLength)
        {
            PlacePage();
            return;
        }

        _targetLength = targetLength;
        if (!IsLoaded)
        {
            // Not on screen yet, so there is nothing to animate.
            OpenPaneLength = targetLength;
            PlacePage();
            return;
        }

        // An animation interrupted partway has less distance left, so it gets the same share of the time.
        var fullDistance = Math.Abs(ExpandedPaneLength - CollapsedPaneLength);
        var distance = Math.Abs(targetLength - OpenPaneLength);
        var fullDuration = isCompact ? CollapseDuration : ExpandDuration;
        _duration = fullDistance > 0 ? fullDuration * Math.Min(1, distance / fullDistance) : TimeSpan.Zero;
        _startLength = OpenPaneLength;
        _startTime = null;

        CompositionTarget.Rendering -= OnRendering;
        CompositionTarget.Rendering += OnRendering;
    }

    // Moves the pane one step. Runs once per frame until the animation finishes.
    private void OnRendering(object? sender, object args)
    {
        var now = ((RenderingEventArgs)args).RenderingTime;
        _startTime ??= now;

        var elapsed = now - _startTime.Value;
        var progress = _duration > TimeSpan.Zero ? Math.Min(1, elapsed / _duration) : 1;
        // Eases out, so the pane moves fast at first and slows to a stop.
        var eased = 1 - Math.Pow(1 - progress, 4);
        OpenPaneLength = _startLength + (_targetLength - _startLength) * eased;

        var isFinished = progress >= 1;
        if (isFinished)
        {
            CompositionTarget.Rendering -= OnRendering;
            if (!_isOverlayOpen)
                _isPaneOverPage = false;
        }

        PlacePage();
    }

    // NavigationView lays the page out in the space to the right of the pane, so a wider pane normally
    // pushes the page aside and makes it narrower. For an overlay the page is pulled back by the extra
    // width, with a negative margin, so it stays exactly where it is beside a collapsed pane. The part of
    // the page that ends up under the pane is clipped away by NavigationView.
    private void PlacePage()
    {
        if (Content is not FrameworkElement page)
            return;

        var extraWidth = _isPaneOverPage ? OpenPaneLength - CollapsedPaneLength : 0;
        page.Margin = new Thickness(-Math.Max(0, extraWidth), 0, 0, 0);
    }

    // A compact pane hides the items' names, so each item shows its name as a tooltip instead.
    private void UpdateToolTips()
    {
        var entries = new List<object?>();
        entries.AddRange(MenuItems);
        entries.AddRange(FooterMenuItems);
        entries.Add(SettingsItem);

        // Headers and separators are skipped, as is the settings item before it has been created.
        foreach (var item in entries.OfType<NavigationViewItem>())
        {
            // An item whose content is a control rather than text is named by its accessible name.
            var name = item.Content as string ?? AutomationProperties.GetName(item);
            if (name.Length == 0)
                name = "Settings";

            ToolTipService.SetToolTip(item, IsCompact ? name : null);
        }
    }
}
