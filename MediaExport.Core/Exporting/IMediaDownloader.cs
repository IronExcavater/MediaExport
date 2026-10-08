// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Exporting;

/// <summary>How one export should come out. Each choice the export page offers becomes a property here.</summary>
/// <param name="Format">The file type, e.g. <c>mp4</c> or <c>mp3</c>.</param>
public sealed record ExportOptions(string Format, string OutputFolder);

/// <param name="Percent">From 0 to 100.</param>
public sealed record ExportProgress(double Percent, string? Speed, string? Eta);

/// <summary>
/// What turns a link into files. View models ask for this rather than for yt-dlp itself,
/// so they can be tested with a stand-in that downloads nothing.
/// </summary>
public interface IMediaDownloader
{
    /// <returns>The full paths of the exported files.</returns>
    Task<IReadOnlyList<string>> DownloadAsync(
        string sourceUrl,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
