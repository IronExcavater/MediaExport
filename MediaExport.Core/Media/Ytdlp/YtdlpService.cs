// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using ManuHub.Ytdlp.NET;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using System.Text.Json;
using YtdlpClient = ManuHub.Ytdlp.NET.Ytdlp;

namespace MediaExport.Core.Media.Ytdlp;

public sealed class YtdlpService : IMediaDownloader
{
    private const string OutputMarker = "__MEDIA_EXPORT_FILE__:";

    private readonly YtdlpClient _ytdlp = new("yt-dlp.exe");

    public async Task<IReadOnlyList<YtdlpSearchResult>> SearchAsync(
        string searchTarget,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTarget))
            return [];

        cancellationToken.ThrowIfCancellationRequested();

        string? json = await _ytdlp.GetMetadataRawAsync(searchTarget, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("yt-dlp returned no search metadata.");

        return ReadSearchResults(json);
    }

    public async Task<Uri?> GetAudioStreamAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        Metadata? metadata = await _ytdlp.GetMetadataAsync(sourceUrl, cancellationToken);

        // Progressive HTTP streams play directly in MediaPlayer; HLS variants are only a fallback.
        FormatMetadata? stream = (metadata?.Formats ?? [])
            .Where(format => format.Url is not null && format.Acodec is not null and not "none")
            .OrderByDescending(format => format.Protocol is "http" or "https")
            .ThenByDescending(format => format.Abr ?? 0)
            .FirstOrDefault();

        return Uri.TryCreate(stream?.Url, UriKind.Absolute, out Uri? uri) ? uri : null;
    }

    public async Task<string> DownloadAsync(
        string sourceUrl,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.OutputFolder);

        YtdlpClient download = _ytdlp
            .WithOutputFolder(options.OutputFolder)
            .WithOutputTemplate("%(title)s.%(ext)s")
            .WithWindowsFilenames();

        foreach ((string name, string? value) in GetExportArguments(options))
            download = value is null ? download.AddFlag(name) : download.AddOption(name, value);

        string? outputPath = null;
        string? lastError = null;

        void OnProgress(object? sender, DownloadProgressEventArgs args) =>
            progress?.Report(new ExportProgress(
                Math.Clamp(args.Percent, 0, 100),
                string.IsNullOrWhiteSpace(args.Speed) ? null : args.Speed,
                string.IsNullOrWhiteSpace(args.ETA) ? null : args.ETA,
                false));

        void OnPostProcessing(object? sender, string message) =>
            progress?.Report(new ExportProgress(100, null, null, true));

        void OnOutput(object? sender, string line)
        {
            int marker = line.IndexOf(OutputMarker, StringComparison.Ordinal);
            if (marker >= 0)
                outputPath = line[(marker + OutputMarker.Length)..].Trim();
        }

        void OnError(object? sender, string message) => lastError = message;

        download.ProgressDownload += OnProgress;
        download.PostProcessingStarted += OnPostProcessing;
        download.OutputMessage += OnOutput;
        download.ErrorMessage += OnError;
        try
        {
            await download.DownloadAsync(sourceUrl, cancellationToken);
        }
        finally
        {
            download.ProgressDownload -= OnProgress;
            download.PostProcessingStarted -= OnPostProcessing;
            download.OutputMessage -= OnOutput;
            download.ErrorMessage -= OnError;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            throw new InvalidOperationException(lastError ?? "The export finished without producing a file.");

        return Path.GetFullPath(outputPath);
    }

    internal static IReadOnlyList<(string Name, string? Value)> GetExportArguments(ExportOptions options)
    {
        var arguments = new List<(string, string?)>
        {
            ("--no-playlist", null),
            ("--encoding", "utf-8"), // Otherwise non-ANSI characters are dropped from the printed file path.
            ("--progress", null), // --print implies --quiet, which would hide progress.
            ("--print", $"after_move:{OutputMarker}%(filepath)s"),
            ("--embed-metadata", null)
        };

        if (options.Kind == MediaKind.Audio)
        {
            arguments.Add(("-f", "ba/b"));
            arguments.Add(("-x", null));
            arguments.Add(("--audio-format", options.Format));
            arguments.Add(("--audio-quality", "0"));
            if (options.Format is "mp3" or "m4a" or "opus")
                arguments.Add(("--embed-thumbnail", null));
            return arguments;
        }

        // Sorting (rather than filtering) falls back gracefully when the preferred height doesn't exist.
        var sort = new List<string>();
        if (options.MaxHeight is int height)
            sort.Add($"res:{height}");
        if (options.Format is "mp4")
            sort.Add("vcodec:h264,acodec:aac");
        if (options.Format is "webm")
            sort.Add("vcodec:vp9,acodec:opus");

        arguments.Add(("-f", "bv*+ba/b"));
        if (sort.Count > 0)
            arguments.Add(("-S", string.Join(',', sort)));
        arguments.Add(("--merge-output-format", options.Format));
        arguments.Add(("--remux-video", options.Format));
        arguments.Add(("--embed-chapters", null));
        return arguments;
    }

    internal static IReadOnlyList<YtdlpSearchResult> ReadSearchResults(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement entries = document.RootElement.TryGetProperty("entries", out JsonElement value)
            ? value
            : document.RootElement;

        if (entries.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<YtdlpSearchResult>();
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            string? identity = GetString(entry, "id");
            string? title = GetString(entry, "title");

            if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(title))
                continue;

            results.Add(new YtdlpSearchResult(
                identity,
                GetSourceUrl(entry),
                GetThumbnailUri(entry),
                title,
                GetString(entry, "uploader") ?? GetString(entry, "channel"),
                GetDuration(entry),
                GetString(entry, "channel_url") ?? GetString(entry, "uploader_url")));
        }

        return results;
    }

    private static string? GetString(JsonElement entry, string propertyName) =>
        entry.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static string? GetSourceUrl(JsonElement entry) =>
        GetString(entry, "webpage_url") ?? GetString(entry, "original_url") ?? GetString(entry, "url");

    private static Uri? GetThumbnailUri(JsonElement entry)
    {
        if (!entry.TryGetProperty("thumbnails", out JsonElement thumbnails)
            || thumbnails.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement thumbnail in thumbnails.EnumerateArray().Reverse())
        {
            string? url = GetString(thumbnail, "url");
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                return uri;
        }

        return null;
    }

    private static TimeSpan? GetDuration(JsonElement entry)
    {
        if (!entry.TryGetProperty("duration", out JsonElement value) || !value.TryGetDouble(out double seconds))
            return null;

        return TimeSpan.FromSeconds(seconds);
    }
}
