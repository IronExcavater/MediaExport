// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Exporting;

/// <summary>Options for one media export.</summary>
/// <param name="Format">The file type, e.g. <c>mp4</c> or <c>mp3</c>.</param>
public sealed record ExportOptions(string Format, string OutputFolder);

/// <param name="Percent">From 0 to 100.</param>
public sealed record ExportProgress(double Percent, string? Speed, string? Eta);

/// <summary>Exports media to one or more files.</summary>
public interface IMediaDownloader
{
    /// <returns>The full paths of the exported files.</returns>
    Task<IReadOnlyList<string>> DownloadAsync(
        string sourceUrl,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
