// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace MediaExport.Controls;

/// <summary>Which edges an <see cref="EdgeFade"/> fades.</summary>
public enum EdgeFadeMode
{
    /// <summary>The edges with content scrolled out of view beyond them.</summary>
    Scrolled,
    Start,
    End,
    Both
}

/// <summary>
/// Fades out the edges of the element inside it:
/// <code>
/// &lt;controls:EdgeFade&gt;
///     &lt;ListView ... /&gt;
/// &lt;/controls:EdgeFade&gt;
/// </code>
/// </summary>
[ContentProperty(Name = nameof(Child))]
public sealed partial class EdgeFade : Grid
{
    // Layout rounds to fractions of a pixel, so less than this doesn't count as scrollable.
    private const double ScrollTolerance = 1;

    private readonly Rectangle _gradient = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false
    };
    private readonly OpacityMask _mask;

    // The child and its ScrollViewer, while this control is listening to them.
    private FrameworkElement? _child;
    private ScrollViewer? _scroller;

    public EdgeFade()
    {
        Children.Add(_gradient);
        _mask = new OpacityMask(host: this, mask: _gradient);

        // Listening only while loaded means neither element keeps the other alive.
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    /// <summary>The element to fade. In <see cref="EdgeFadeMode.Scrolled"/> mode it must be, or contain, a ScrollViewer.</summary>
    public FrameworkElement? Child
    {
        get;
        set
        {
            if (field is not null)
                Children.Remove(field);

            field = value;
            // Under the gradient, which is only there to be the mask's shape and is never seen.
            if (value is not null)
                Children.Insert(0, value);

            Attach();
        }
    }

    public EdgeFadeMode Mode
    {
        get;
        set { field = value; Attach(); }
    }

    /// <summary><c>Vertical</c> fades the top and bottom edges, <c>Horizontal</c> the left and right.</summary>
    public Orientation Orientation
    {
        get;
        set { field = value; Update(); }
    } = Orientation.Vertical;

    /// <summary>How far the fade reaches in from an edge.</summary>
    public double FadeLength
    {
        get;
        set { field = value; Update(); }
    } = 16;

    private void Attach()
    {
        Detach();
        if (!IsLoaded || Child is null)
            return;

        _child = Child;
        _child.SizeChanged += Child_SizeChanged;
        _mask.Target = _child;

        if (Mode == EdgeFadeMode.Scrolled)
        {
            _scroller = _child as ScrollViewer ?? FindScroller(_child);
            if (_scroller is null)
            {
                // A list's ScrollViewer is in its template, which doesn't exist until the list has loaded.
                _child.Loaded += Child_Loaded;
                return;
            }

            _scroller.ViewChanged += Scroller_ViewChanged;
            if (_scroller.Content is FrameworkElement content)
                content.SizeChanged += Child_SizeChanged;
        }

        Update();
    }

    private void Detach()
    {
        _mask.Target = null;
        if (_child is not null)
        {
            _child.SizeChanged -= Child_SizeChanged;
            _child.Loaded -= Child_Loaded;
        }
        if (_scroller is not null)
        {
            _scroller.ViewChanged -= Scroller_ViewChanged;
            if (_scroller.Content is FrameworkElement content)
                content.SizeChanged -= Child_SizeChanged;
        }
        _child = null;
        _scroller = null;
    }

    private void Child_Loaded(object sender, RoutedEventArgs args) => Attach();

    private void Child_SizeChanged(object sender, SizeChangedEventArgs args) => Update();

    private void Scroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) => Update();

    private void Update()
    {
        if (_child is null)
            return;

        var isVertical = Orientation == Orientation.Vertical;
        var length = isVertical ? _child.ActualHeight : _child.ActualWidth;
        (var fadeStart, var fadeEnd) = FindEdgesToFade(isVertical);

        var hasRoom = length > FadeLength * 2;
        var isVisible = _child.Visibility == Visibility.Visible;
        _mask.IsApplied = (fadeStart || fadeEnd) && hasRoom && isVisible;
        if (!_mask.IsApplied)
            return;

        // The gradient and the masked copy sit exactly over the child, wherever its margin has put it.
        var corner = _child.ActualOffset;
        _mask.Offset = new Point(corner.X, corner.Y);
        _gradient.Margin = new Thickness(corner.X, corner.Y, 0, 0);
        _gradient.Width = _child.ActualWidth;
        _gradient.Height = _child.ActualHeight;

        // Opaque in the middle, transparent at each edge that fades.
        var opaque = Color.FromArgb(255, 0, 0, 0);
        var transparent = Color.FromArgb(0, 0, 0, 0);
        var fadeFraction = FadeLength / length;
        _gradient.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = isVertical ? new Point(0, 1) : new Point(1, 0),
            GradientStops =
            {
                new GradientStop { Offset = 0, Color = fadeStart ? transparent : opaque },
                new GradientStop { Offset = fadeFraction, Color = opaque },
                new GradientStop { Offset = 1 - fadeFraction, Color = opaque },
                new GradientStop { Offset = 1, Color = fadeEnd ? transparent : opaque }
            }
        };
    }

    private (bool Start, bool End) FindEdgesToFade(bool isVertical) => Mode switch
    {
        EdgeFadeMode.Start => (true, false),
        EdgeFadeMode.End => (false, true),
        EdgeFadeMode.Both => (true, true),
        _ => FindScrolledEdges(isVertical)
    };

    private (bool Start, bool End) FindScrolledEdges(bool isVertical)
    {
        if (_scroller is null)
            return (false, false);

        var extent = isVertical ? _scroller.ExtentHeight : _scroller.ExtentWidth;
        var viewport = isVertical ? _scroller.ViewportHeight : _scroller.ViewportWidth;
        var offset = isVertical ? _scroller.VerticalOffset : _scroller.HorizontalOffset;

        var hiddenBefore = offset;
        var hiddenAfter = extent - viewport - offset;
        return (hiddenBefore > ScrollTolerance, hiddenAfter > ScrollTolerance);
    }

    private static ScrollViewer? FindScroller(DependencyObject parent)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            var found = child as ScrollViewer ?? FindScroller(child);
            if (found is not null)
                return found;
        }
        return null;
    }
}
