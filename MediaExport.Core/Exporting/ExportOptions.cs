// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Exporting;

/// <param name="Format">Output file extension, e.g. <c>mp4</c> or <c>mp3</c>.</param>
/// <param name="MaxHeight">Preferred maximum video height; <c>null</c> for the best available.</param>
public sealed record ExportOptions(MediaKind Kind, string Format, int? MaxHeight, string OutputFolder)
{
    /// <summary>Audio bitrate in kbps; <c>null</c> for the best available.</summary>
    public int? AudioBitrate { get; init; }

    public bool EmbedMetadata { get; init; } = true;
    public bool EmbedThumbnail { get; init; } = true;
    public bool EmbedChapters { get; init; } = true;

    /// <summary>Comma-separated subtitle languages, e.g. <c>en,fr</c>; <c>null</c> for no subtitles.</summary>
    public string? SubtitleLanguages { get; init; }
    public bool IncludeAutomaticSubtitles { get; init; }

    public TimeSpan? TrimStart { get; init; }
    public TimeSpan? TrimEnd { get; init; }

    /// <summary>yt-dlp output template without the extension.</summary>
    public string FileNameTemplate { get; init; } = "%(title)s";
}

public sealed record ExportProgress(double Percent, string? Speed, string? Eta, bool IsProcessing);

/// <summary>What can be exported from a piece of media, or why nothing can.</summary>
public sealed record MediaInspection(bool IsExportable, string? Problem, bool HasVideo, int? MaxHeight)
{
    public static MediaInspection Unavailable(string problem) => new(false, problem, false, null);
}

public interface IMediaDownloader
{
    Task<MediaInspection> InspectAsync(string sourceUrl, CancellationToken cancellationToken = default);

    /// <returns>The full path of the exported file.</returns>
    Task<string> DownloadAsync(
        string sourceUrl,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
