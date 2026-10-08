// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Tooling;

namespace MediaExport.Core.Media.Ytdlp;

/// <summary>Everything done by running yt-dlp: downloading, and searching the sources it can search.</summary>
public sealed class YtdlpService(ToolchainService tools) : IMediaDownloader
{
    public Task<IReadOnlyList<string>> DownloadAsync(
        string sourceUrl,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _ = tools; // Where yt-dlp and ffmpeg are: tools.Locate("yt-dlp.exe").
        throw new NotImplementedException();
    }
}
