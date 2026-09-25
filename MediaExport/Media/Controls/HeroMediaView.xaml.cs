// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System;

namespace MediaExport.Media.Controls;

public sealed partial class HeroMediaView : MediaViewBase
{
    public event EventHandler? PreviewRequested;

    public HeroMediaView()
    {
        InitializeComponent();
    }

    protected override void RefreshMediaBindings() => Bindings.Update();

    private async void ThumbnailButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        if (Media?.EmbedUri is not null && PreviewRequested is not null)
        {
            PreviewRequested(this, EventArgs.Empty);
            return;
        }

        await OpenSourceAsync();
    }
}
