// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using MediaExport.Activity;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaExport.Export;

/// <summary>The media that's open and how it's to be exported. Exporting hands the job to <see cref="ActivityViewModel"/>.</summary>
public sealed partial class ExportViewModel : ObservableObject
{
    private readonly ActivityViewModel _activity;
    private readonly SettingsStore _settings;
    private readonly IReadOnlyList<IMediaSearchProvider> _providers;

    // The link being read. A newer one replaces it, and what an older one finds is ignored.
    private CancellationTokenSource? _reading;

    public ExportViewModel(ActivityViewModel activity, SettingsStore settings, IEnumerable<IMediaSearchProvider> providers)
    {
        _activity = activity;
        _settings = settings;
        _providers = providers.ToList();
    }

    /// <summary>The media that is open, or <c>null</c> when there is none or it is still being read from a link.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial MediaSearchResult? Media { get; private set; }

    /// <summary>Whether a link is being read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial bool IsLoading { get; private set; }

    /// <summary>Whether there is media to show, or will be once its link has been read.</summary>
    public bool IsOpen => Media is not null || IsLoading;

    /// <summary>Raised when media is opened or a link starts being read, for the window to show the Export page.</summary>
    public event EventHandler? Opened;

    public void Open(MediaSearchResult media)
    {
        _reading?.Cancel();
        _reading = null;
        IsLoading = false;
        Media = media;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens the media a link is to. The page shows at once, and the media when the link has been read.</summary>
    public async Task OpenLinkAsync(Uri link)
    {
        _reading?.Cancel();
        var reading = new CancellationTokenSource();
        _reading = reading;
        Media = null;
        IsLoading = true;
        Opened?.Invoke(this, EventArgs.Empty);

        try
        {
            var media = await FindAsync(link, reading.Token);
            if (reading == _reading)
                Media = media;
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
        {
            // A link that was replaced by a newer one, or that couldn't be read, opens nothing. The page has
            // nowhere to say why yet.
        }
        finally
        {
            if (reading == _reading)
                IsLoading = false;
        }
    }

    // Each provider says whether the link is to its source, and the one it is to reads it.
    private async Task<MediaSearchResult?> FindAsync(Uri link, CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            var media = await provider.FindByLinkAsync(link, cancellationToken);
            if (media is not null)
                return media;
        }
        return null;
    }
}
