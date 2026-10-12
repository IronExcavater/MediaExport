// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Converters;
using MediaExport.Core.Media.Models;

namespace MediaExport.Media.Controls;

/// <summary>The line under a media result's title: its provider's icon, who made it, its views and its age.</summary>
public sealed partial class MediaByline : MediaView
{
    public MediaByline()
    {
        InitializeComponent();
    }

    // An unknown value is hidden with the dot before it, and a dot only shows after another value.
    protected override void OnMediaChanged(MediaSearchResult? oldMedia, MediaSearchResult? newMedia)
    {
        var hasCreator = !string.IsNullOrEmpty(newMedia?.Creator);
        var hasViews = newMedia?.Views is not null;
        var hasAge = newMedia?.Published is not null;

        viewsText.Visibility = VisibleWhen.True(hasViews);
        viewsSeparator.Visibility = VisibleWhen.True(hasViews && hasCreator);
        ageText.Visibility = VisibleWhen.True(hasAge);
        ageSeparator.Visibility = VisibleWhen.True(hasAge && (hasCreator || hasViews));
    }
}
