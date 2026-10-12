// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MediaExport.Controls;

/// <summary>Answers where an element is in the visual tree.</summary>
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
}
