// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Controls;

namespace MediaExport.Media.Controls;

// Play, seek and volume only. The cast button is always shown and has no property to hide it;
// collapsing it doesn't stick (width-based dropout re-shows it), so it's removed from the bar.
public sealed partial class AudioTransportControls : MediaTransportControls
{
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("MediaControlsCommandBar") is CommandBar bar
            && GetTemplateChild("CastButton") is ICommandBarElement cast)
        {
            bar.PrimaryCommands.Remove(cast);
        }
    }
}
