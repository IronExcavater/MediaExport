// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MediaExport.Controls;

/// <summary>
/// Shows an icon from Resources/Icons.xaml, chosen by its resource key. Useful when the key is only known at
/// run time (a search provider's icon, say); a fixed icon can use the style directly:
/// <c>&lt;ImageIcon Style="{StaticResource YouTubeIcon}" /&gt;</c>
/// </summary>
public sealed partial class IconView : Grid
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(string),
        typeof(IconView),
        new PropertyMetadata(null, (view, _) => ((IconView)view).Refresh()));

    private readonly ImageIcon _icon = new();

    public IconView()
    {
        Children.Add(_icon);
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
