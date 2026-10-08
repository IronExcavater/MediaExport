// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using MediaExport.Activity;
using MediaExport.Core.Settings;

namespace MediaExport.Export;

/// <summary>The media that's open and how it's to be exported. Exporting hands the job to <see cref="ActivityViewModel"/>.</summary>
public sealed partial class ExportViewModel : ObservableObject
{
    private readonly ActivityViewModel _activity;
    private readonly SettingsStore _settings;

    public ExportViewModel(ActivityViewModel activity, SettingsStore settings)
    {
        _activity = activity;
        _settings = settings;
    }
}
