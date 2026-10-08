// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MediaExport.Media.Controls;

/// <summary>A quiet media-card placeholder shown while the first search results are arriving.</summary>
public sealed partial class MediaSkeletonView : UserControl
{
    public MediaSkeletonView() => InitializeComponent();

    private void View_Loaded(object sender, RoutedEventArgs args) => pulse.Begin();

    private void View_Unloaded(object sender, RoutedEventArgs args) => pulse.Stop();
}
