// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Media.Controls;

public sealed partial class CompactMediaView : MediaViewBase
{
    public CompactMediaView()
    {
        InitializeComponent();
    }

    // Audio artwork is square; video thumbnails are 16:9.
    public double ThumbnailWidth => IsAudio ? 56 : 100;

    protected override void RefreshMediaBindings() => Bindings.Update();
}
