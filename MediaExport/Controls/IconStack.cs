// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Linq;

namespace MediaExport.Controls;

/// <summary>Shows several icons in the room of about one, each lying over the start of the one after it.</summary>
public sealed partial class IconStack : StackPanel
{
    private const double IconSize = 16;

    public IconStack()
    {
        Orientation = Orientation.Horizontal;
        Spacing = -IconSize / 3;
    }

    /// <summary>The icons' resource keys, from the one on top to the one underneath.</summary>
    public IEnumerable<string>? Icons
    {
        get;
        set
        {
            field = value;
            Children.Clear();
            var icons = value?.ToList() ?? [];
            foreach (var icon in icons)
            {
                // The shadow of an icon that lies over another shows where its edge is. The last lies over none.
                var index = Children.Count;
                var isOverAnother = index < icons.Count - 1;
                var view = new IconView { Width = IconSize, Height = IconSize, Icon = icon, HasShadow = isOverAnother };

                // A panel draws later children over earlier ones, so the order is turned round.
                Canvas.SetZIndex(view, -index);
                Children.Add(view);
            }
        }
    }
}
