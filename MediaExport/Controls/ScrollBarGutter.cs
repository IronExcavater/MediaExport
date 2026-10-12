// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MediaExport.Controls;

/// <summary>When room is kept beside scrolling content for its scroll bar.</summary>
public enum ScrollBarGutterMode
{
    /// <summary>No room is kept, and the scroll bar lies over the content's edge.</summary>
    None,

    /// <summary>Room is kept only while there is something to scroll.</summary>
    Auto,

    /// <summary>Room is always kept, so the content doesn't move when it starts or stops scrolling.</summary>
    Stable
}

/// <summary>
/// Keeps the content of a ScrollViewer, or of a list, clear of its scroll bar, as CSS's <c>scrollbar-gutter</c> does:
/// <c>&lt;ScrollViewer controls:ScrollBarGutter.Mode="Auto" /&gt;</c>.
/// </summary>
public static class ScrollBarGutter
{
    // How wide a vertical scroll bar is while the pointer is over it.
    private const double Width = 12;

    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "Mode",
        typeof(ScrollBarGutterMode),
        typeof(ScrollBarGutter),
        new PropertyMetadata(ScrollBarGutterMode.None, OnModeChanged));

    public static ScrollBarGutterMode GetMode(FrameworkElement element) =>
        (ScrollBarGutterMode)element.GetValue(ModeProperty);

    public static void SetMode(FrameworkElement element, ScrollBarGutterMode value) =>
        element.SetValue(ModeProperty, value);

    private static void OnModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        // A list's scroll viewer is in its template, which is only there once the list has loaded.
        var element = (FrameworkElement)sender;
        if (element.IsLoaded)
            Attach(element);
        else
            element.Loaded += (_, _) => Attach(element);
    }

    private static void Attach(FrameworkElement element)
    {
        var scrollViewer = element as ScrollViewer ?? FindScrollViewer(element);
        if (scrollViewer is null)
            return;

        // The gutter comes and goes as the content starts and stops being taller than its view.
        scrollViewer.RegisterPropertyChangedCallback(
            ScrollViewer.ScrollableHeightProperty,
            (_, _) => Update(element, scrollViewer));
        Update(element, scrollViewer);
    }

    private static void Update(FrameworkElement element, ScrollViewer scrollViewer)
    {
        var mode = GetMode(element);
        var isScrollable = scrollViewer.ScrollableHeight > 0;
        var hasGutter = mode == ScrollBarGutterMode.Stable || (mode == ScrollBarGutterMode.Auto && isScrollable);

        var padding = scrollViewer.Padding;
        padding.Right = hasGutter ? Width : 0;
        scrollViewer.Padding = padding;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            var found = child as ScrollViewer ?? FindScrollViewer(child);
            if (found is not null)
                return found;
        }
        return null;
    }
}
