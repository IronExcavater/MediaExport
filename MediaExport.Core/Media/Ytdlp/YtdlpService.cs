// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Tooling;

namespace MediaExport.Core.Media.Ytdlp;

/// <summary>Searches and downloads media through yt-dlp.</summary>
public sealed class YtdlpService(ToolchainService tools) : IMediaDownloader
{
    public Task<IReadOnlyList<string>> DownloadAsync(
        string sourceUrl,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _ = tools;
        throw new NotImplementedException();
    }
}
