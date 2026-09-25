// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Media.Controls;

public sealed partial class CompactMediaView : MediaViewBase
{
    public CompactMediaView()
    {
        InitializeComponent();
    }

    protected override void RefreshMediaBindings() => Bindings.Update();
}
