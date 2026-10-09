// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaExport.Shell;

/// <summary>A navigation view whose pane slides between two widths, resizing the page beside it as it goes.</summary>
/// <remarks>
/// NavigationView's own collapse snaps the page to its new width before the pane has finished moving
/// (microsoft-ui-xaml#9370). This control keeps the pane open and animates its width instead, so the pane
/// and the page move together.
/// </remarks>
public sealed partial class SmoothNavigationView : NavigationView
{
    // The animation in progress.
    private TimeSpan? _startTime;
    private TimeSpan _duration;
    private double _startLength;
    private double _targetLength;

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

    /// <summary>Whether the pane is collapsed right now, by the user's choice or for lack of room.</summary>
    public bool IsCompact { get; private set; }

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

    // Decides whether the pane should be compact and, if its width has to change, starts animating to it.
    // Called whenever a property or the control's size changes.
    private void UpdatePane()
    {
        bool isTooNarrow = ActualWidth < AutoCollapseWidth;
        bool isCompact = !IsExpanded || isTooNarrow;
        if (isCompact != IsCompact)
        {
            IsCompact = isCompact;
            UpdateToolTips();
            IsCompactChanged?.Invoke(this, EventArgs.Empty);
        }

        double targetLength = isCompact ? CollapsedPaneLength : ExpandedPaneLength;
        if (targetLength == _targetLength)
            return;

        _targetLength = targetLength;
        if (!IsLoaded)
        {
            // Not on screen yet, so there is nothing to animate.
            OpenPaneLength = targetLength;
            return;
        }

        // An animation interrupted partway has less distance left, so it gets the same share of the time.
        double fullDistance = Math.Abs(ExpandedPaneLength - CollapsedPaneLength);
        double distance = Math.Abs(targetLength - OpenPaneLength);
        TimeSpan fullDuration = isCompact ? CollapseDuration : ExpandDuration;
        _duration = fullDistance > 0 ? fullDuration * Math.Min(1, distance / fullDistance) : TimeSpan.Zero;
        _startLength = OpenPaneLength;
        _startTime = null;

        CompositionTarget.Rendering -= OnRendering;
        CompositionTarget.Rendering += OnRendering;
    }

    // Moves the pane one step. Runs once per frame until the animation finishes.
    private void OnRendering(object? sender, object args)
    {
        TimeSpan now = ((RenderingEventArgs)args).RenderingTime;
        _startTime ??= now;

        TimeSpan elapsed = now - _startTime.Value;
        double progress = _duration > TimeSpan.Zero ? Math.Min(1, elapsed / _duration) : 1;
        // Ease out: fast at first, slowing to a stop.
        double eased = 1 - Math.Pow(1 - progress, 4);
        OpenPaneLength = _startLength + (_targetLength - _startLength) * eased;

        if (progress >= 1)
            CompositionTarget.Rendering -= OnRendering;
    }

    // A compact pane hides the items' names, so each item shows its name as a tooltip instead.
    private void UpdateToolTips()
    {
        var entries = new List<object?>();
        entries.AddRange(MenuItems);
        entries.AddRange(FooterMenuItems);
        entries.Add(SettingsItem);

        // Headers and separators are skipped, as is the settings item before it has been created.
        foreach (NavigationViewItem item in entries.OfType<NavigationViewItem>())
        {
            // An item whose content is a control rather than text is named by its accessible name.
            string name = item.Content as string ?? AutomationProperties.GetName(item);
            if (name.Length == 0)
                name = "Settings";

            ToolTipService.SetToolTip(item, IsCompact ? name : null);
        }
    }
}
