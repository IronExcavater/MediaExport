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

/// <param name="Format">Output file extension, e.g. <c>mp4</c> or <c>mp3</c>.</param>
/// <param name="MaxHeight">Preferred maximum video height; <c>null</c> for the best available.</param>
public sealed record ExportOptions(MediaKind Kind, string Format, int? MaxHeight, string OutputFolder)
{
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

    /// <summary>A file per chapter rather than one file.</summary>
    public bool SplitChapters { get; init; }

    /// <summary>Cuts sponsor segments, self-promotion and interaction reminders (YouTube, via SponsorBlock).</summary>
    public bool RemoveSponsors { get; init; }

    /// <summary>A browser to borrow sign-in cookies from, for media that needs an account; <c>null</c> for none.</summary>
    public string? CookiesFromBrowser { get; init; }

    /// <summary>yt-dlp output template without the extension.</summary>
    public string FileNameTemplate { get; init; } = "%(title)s";
}

public sealed record ExportProgress(double Percent, string? Speed, string? Eta, bool IsProcessing);

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
