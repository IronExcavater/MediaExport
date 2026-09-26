// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Exporting;

/// <param name="Format">Output file extension, e.g. <c>mp4</c> or <c>mp3</c>.</param>
/// <param name="MaxHeight">Preferred maximum video height; <c>null</c> for the best available.</param>
public sealed record ExportOptions(MediaKind Kind, string Format, int? MaxHeight, string OutputFolder);

public sealed record ExportProgress(double Percent, string? Speed, string? Eta, bool IsProcessing);

public interface IMediaDownloader
{
    /// <summary>Resolves a directly playable audio stream for previewing <paramref name="sourceUrl"/>.</summary>
    Task<Uri?> GetAudioStreamAsync(string sourceUrl, CancellationToken cancellationToken = default);

    /// <returns>The full path of the exported file.</returns>
    Task<string> DownloadAsync(
        string sourceUrl,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
