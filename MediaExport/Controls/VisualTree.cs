// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MediaExport.Controls;

/// <summary>Finds elements by their place in the visual tree.</summary>
public static class VisualTree
{
    /// <summary>Whether an element is the ancestor itself or somewhere inside it.</summary>
    public static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        while (element is not null)
        {
            if (element == ancestor)
                return true;

            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    /// <summary>The first element of a type inside another, such as a part of its template; <c>null</c> if there is none.</summary>
    public static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            var found = child as T ?? FindDescendant<T>(child);
            if (found is not null)
                return found;
        }
        return null;
    }
}
