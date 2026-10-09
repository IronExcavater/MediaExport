// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Shell;

/// <summary>
/// The window sizes at which the layout changes. Every breakpoint in the app uses one of these, from code
/// as <c>WindowSizes.Compact</c> and from XAML as <c>{x:Bind shell:WindowSizes.Compact}</c>.
/// </summary>
public static class WindowSizes
{
    /// <summary>The smallest the window can be made.</summary>
    public const double MinWidth = 640;
    public const double MinHeight = 480;

    /// <summary>Below this width the navigation pane collapses to icons and the app name is hidden.</summary>
    public const double Compact = 840;

    /// <summary>From this width there is room for two columns side by side.</summary>
    public const double Wide = 1080;
}
