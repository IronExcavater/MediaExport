// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using Microsoft.UI.Xaml;

namespace MediaExport.Media.Controls;

/// <summary>The line under a media result's title: its provider's icon, who made it, its views and its age.</summary>
public sealed partial class MediaByline : MediaView
{
    public MediaByline()
    {
        InitializeComponent();
    }

    // The values that aren't known are hidden along with the dot before them, and a dot only shows when there
    // is something before it to separate its value from.
    protected override void OnMediaChanged(MediaSearchResult? oldMedia, MediaSearchResult? newMedia)
    {
        var hasCreator = !string.IsNullOrEmpty(newMedia?.Creator);
        var hasViews = newMedia?.Views is not null;
        var hasAge = newMedia?.Published is not null;

        viewsText.Visibility = ToVisibility(hasViews);
        viewsSeparator.Visibility = ToVisibility(hasViews && hasCreator);
        ageText.Visibility = ToVisibility(hasAge);
        ageSeparator.Visibility = ToVisibility(hasAge && (hasCreator || hasViews));
    }

    private static Visibility ToVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;
}
