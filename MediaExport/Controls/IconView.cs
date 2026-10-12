// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;

namespace MediaExport.Controls;

/// <summary>Shows an icon from Resources/Icons.xaml by its resource key, for a key only known at run time.</summary>
public sealed partial class IconView : Grid
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(string),
        typeof(IconView),
        new PropertyMetadata(null, (view, _) => ((IconView)view).Refresh()));

    private readonly ImageIcon _icon = new();

    // Lies behind the icon and holds its shadow, which is drawn by the composition layer.
    private readonly Border _shadowHost = new();

    public IconView()
    {
        Children.Add(_shadowHost);
        Children.Add(_icon);

        // The shadow is as large as the icon, which has no size until it is laid out.
        _icon.SizeChanged += (_, _) => UpdateShadow();
    }

    /// <summary>Whether the icon casts a shadow in its own shape, which shows its edge over another icon.</summary>
    public bool HasShadow
    {
        get;
        set
        {
            field = value;
            UpdateShadow();
        }
    }

    /// <summary>The icon's resource key, e.g. <c>YouTubeIcon</c>. An unknown key shows nothing.</summary>
    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Makes an icon element from a resource key. Returns <c>null</c> if there is no icon with that key.</summary>
    public static ImageIcon? Create(string? key)
    {
        var style = FindStyle(key);
        return style is null ? null : new ImageIcon { Style = style };
    }

    private static Style? FindStyle(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        var isFound = Application.Current.Resources.TryGetValue(key, out var resource);
        return isFound ? resource as Style : null;
    }

    private void UpdateShadow()
    {
        if (!HasShadow || _icon.ActualSize == Vector2.Zero)
        {
            ElementCompositionPreview.SetElementChildVisual(_shadowHost, null);
            return;
        }

        // The shadow takes its shape from the icon as it is drawn, so it is right whenever the picture arrives.
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        var drawnIcon = compositor.CreateVisualSurface();
        drawnIcon.SourceVisual = ElementCompositionPreview.GetElementVisual(_icon);
        drawnIcon.SourceSize = _icon.ActualSize;

        // It falls to the left, onto the icon underneath.
        var shadow = compositor.CreateDropShadow();
        shadow.Mask = compositor.CreateSurfaceBrush(drawnIcon);
        shadow.BlurRadius = 2;
        shadow.Opacity = 0.8f;
        shadow.Offset = new Vector3(-1, 0, 0);

        var shadowVisual = compositor.CreateSpriteVisual();
        shadowVisual.Size = _icon.ActualSize;
        shadowVisual.Shadow = shadow;
        ElementCompositionPreview.SetElementChildVisual(_shadowHost, shadowVisual);
    }

    private void Refresh()
    {
        var style = FindStyle(Icon);
        if (style is null)
        {
            _icon.ClearValue(StyleProperty);
            Visibility = Visibility.Collapsed;
            return;
        }

        _icon.Style = style;
        Visibility = Visibility.Visible;
    }
}
