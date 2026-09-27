// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Exporting;

/// <summary>A part of the media to keep, from <see cref="Start"/> up to <see cref="End"/>.</summary>
// Not readonly: XAML's type information for the timeline needs settable members.
public record struct Clip(TimeSpan Start, TimeSpan End)
{
    public readonly TimeSpan Length => End - Start;
}

/// <param name="Format">The file type: a container such as <c>mp4</c> with video, or an audio format such as <c>mp3</c> without.</param>
public sealed record ExportOptions(string Format, string OutputFolder)
{
    /// <summary>With video, the file is a video (with or without sound); without, it's audio only.</summary>
    public bool IncludeVideo { get; init; } = true;

    public bool IncludeAudio { get; init; } = true;

    /// <summary>Preferred maximum video height; <c>null</c> for the best available.</summary>
    public int? MaxHeight { get; init; }

    /// <summary>Audio bitrate in kbps; <c>null</c> for the best available.</summary>
    public int? AudioBitrate { get; init; }

    public bool EmbedMetadata { get; init; } = true;
    public bool EmbedThumbnail { get; init; } = true;
    public bool EmbedChapters { get; init; } = true;

    /// <summary>Subtitle language codes to embed, e.g. <c>en</c>; empty for none.</summary>
    public IReadOnlyList<string> SubtitleLanguages { get; init; } = [];

    /// <summary>Parts to keep; empty keeps all of it.</summary>
    public IReadOnlyList<Clip> Clips { get; init; } = [];

    /// <summary>With several clips: one file of them all, rather than a file each.</summary>
    public bool JoinClips { get; init; } = true;

    /// <summary>Cuts sponsor segments, self-promotion and interaction reminders (YouTube, via SponsorBlock).</summary>
    public bool RemoveSponsors { get; init; }

    /// <summary>A browser to borrow sign-in cookies from, for media that needs an account; <c>null</c> for none.</summary>
    public string? CookiesFromBrowser { get; init; }

    /// <summary>Tries without the browser's sign-in first, and uses it only if the site asks for one.</summary>
    public bool SignInOnlyWhenNeeded { get; init; }

    /// <summary>yt-dlp output template without the extension.</summary>
    public string FileNameTemplate { get; init; } = "%(title)s";

    public bool IsAudioOnly => !IncludeVideo;
}

/// <param name="Step">What's happening when there's no percentage to show, e.g. "Cutting clip 2 of 3".</param>
public sealed record ExportProgress(double Percent, string? Speed, string? Eta, bool IsProcessing, string? Step = null);

public interface IMediaDownloader
{
    Task<MediaDetails> InspectAsync(string sourceUrl, CancellationToken cancellationToken = default);

    /// <returns>The full paths of the exported files.</returns>
    Task<IReadOnlyList<string>> DownloadAsync(
        string sourceUrl,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
