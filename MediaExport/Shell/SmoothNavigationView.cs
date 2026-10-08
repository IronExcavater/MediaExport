// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;

namespace MediaExport.Shell;

/// <summary>
/// A navigation view whose pane eases between wide and narrow, the page beside it resizing with it.
/// NavigationView's own collapse snaps the page to its new width while the pane animates
/// (microsoft-ui-xaml#9370, closed as won't fix). Here the pane stays "open" the whole time and only
/// its width changes, a little each frame, so the page is laid out again at every step.
/// <code>
/// &lt;shell:SmoothNavigationView IsExpanded="True" ExpandedPaneLength="240" CollapsedPaneLength="56"
///                             ExpandDuration="0:0:0.25" CollapseDuration="0:0:0.18" /&gt;
/// </code>
/// </summary>
public sealed partial class SmoothNavigationView : NavigationView
{
    public static readonly DependencyProperty IsExpandedProperty = Register(nameof(IsExpanded), true);
    public static readonly DependencyProperty ExpandedPaneLengthProperty = Register(nameof(ExpandedPaneLength), 240d);
    public static readonly DependencyProperty CollapsedPaneLengthProperty = Register(nameof(CollapsedPaneLength), 56d);
    public static readonly DependencyProperty ExpandDurationProperty = Register(nameof(ExpandDuration), TimeSpan.FromMilliseconds(250));
    public static readonly DependencyProperty CollapseDurationProperty = Register(nameof(CollapseDuration), TimeSpan.FromMilliseconds(180));

    private TimeSpan? _start;
    private TimeSpan _duration;
    private double _from;
    private double _to;

    public SmoothNavigationView()
    {
        // Always "open": narrowing is done by OpenPaneLength alone.
        PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        IsPaneOpen = true;
        IsPaneToggleButtonVisible = false;
        IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        OpenPaneLength = ExpandedPaneLength;
        Loaded += (_, _) => LabelItems();
    }

    /// <summary>Whether the pane shows its items' names, or only their icons.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>The pane's width showing icons and names.</summary>
    public double ExpandedPaneLength
    {
        get => (double)GetValue(ExpandedPaneLengthProperty);
        set => SetValue(ExpandedPaneLengthProperty, value);
    }

    /// <summary>
    /// The pane's width showing icons only. At the 48 a navigation view narrows to by itself,
    /// the highlight of an "open" item runs a few pixels past the pane's edge.
    /// </summary>
    public double CollapsedPaneLength
    {
        get => (double)GetValue(CollapsedPaneLengthProperty);
        set => SetValue(CollapsedPaneLengthProperty, value);
    }

    /// <summary>How long the pane takes to open all the way.</summary>
    public TimeSpan ExpandDuration
    {
        get => (TimeSpan)GetValue(ExpandDurationProperty);
        set => SetValue(ExpandDurationProperty, value);
    }

    /// <summary>How long the pane takes to narrow all the way. Shorter than opening by default: it's getting out of the way.</summary>
    public TimeSpan CollapseDuration
    {
        get => (TimeSpan)GetValue(CollapseDurationProperty);
        set => SetValue(CollapseDurationProperty, value);
    }

    // Any of the properties changing moves the pane to where it should now be.
    private static DependencyProperty Register<T>(string name, T defaultValue) => DependencyProperty.Register(
        name, typeof(T), typeof(SmoothNavigationView),
        new PropertyMetadata(defaultValue, (view, _) => ((SmoothNavigationView)view).MoveToLength()));

    private void MoveToLength()
    {
        _to = IsExpanded ? ExpandedPaneLength : CollapsedPaneLength;
        LabelItems();
        if (!IsLoaded)
        {
            OpenPaneLength = _to; // Nothing's on screen yet to animate.
            return;
        }

        // Partway through an earlier move only the rest of the distance is left, so it takes that much less time.
        _from = OpenPaneLength;
        double whole = Math.Abs(ExpandedPaneLength - CollapsedPaneLength);
        _duration = whole > 0 ? (IsExpanded ? ExpandDuration : CollapseDuration) * Math.Min(1, Math.Abs(_to - _from) / whole) : TimeSpan.Zero;
        _start = null;
        CompositionTarget.Rendering -= Step;
        CompositionTarget.Rendering += Step;
    }

    // Timed by the frames themselves, so every frame drawn gets its own step.
    private void Step(object? sender, object args)
    {
        TimeSpan now = ((RenderingEventArgs)args).RenderingTime;
        _start ??= now;
        double progress = _duration > TimeSpan.Zero ? Math.Min(1, (now - _start.Value) / _duration) : 1;
        double eased = 1 - Math.Pow(1 - progress, 4); // Fluent's decelerate: quick to start, gentle to land.
        OpenPaneLength = _from + (_to - _from) * eased;
        if (progress >= 1)
            CompositionTarget.Rendering -= Step;
    }

    // Narrowed, the items' names are hidden, so each says what it is on hover instead.
    private void LabelItems()
    {
        foreach (NavigationViewItem item in MenuItems.Append(SettingsItem).OfType<NavigationViewItem>())
            ToolTipService.SetToolTip(item, IsExpanded ? null : item.Content ?? "Settings");
    }
}
