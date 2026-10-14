// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;

namespace MediaExport.Settings;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    void Apply(AppTheme theme);
}
