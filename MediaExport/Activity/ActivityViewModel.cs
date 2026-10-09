// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using MediaExport.Core.Exporting;
using MediaExport.Core.Settings;

namespace MediaExport.Activity;

/// <summary>Owns queued, running, and completed exports for the application session.</summary>
public sealed partial class ActivityViewModel : ObservableObject
{
    private readonly IMediaDownloader _downloader;
    private readonly SettingsStore _settings;

    public ActivityViewModel(IMediaDownloader downloader, SettingsStore settings)
    {
        _downloader = downloader;
        _settings = settings;
    }
}
