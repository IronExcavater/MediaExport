// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using ManuHub.Ytdlp.NET;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YtdlpClient = ManuHub.Ytdlp.NET.Ytdlp;

namespace MediaExport.Core.Media.Ytdlp;

public sealed class YtdlpService : IMediaDownloader
{
    private const string Executable = "yt-dlp.exe";
    private const string OutputMarker = "__MEDIA_EXPORT_FILE__:";
    private const string DrmProblem = "It's protected by DRM, so it can't be exported.";

    public async Task<IReadOnlyList<YtdlpSearchResult>> SearchAsync(
        string searchTarget,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTarget))
            return [];

        cancellationToken.ThrowIfCancellationRequested();

        var errors = new ErrorLog();
        string? json = await new YtdlpClient(Executable, errors).GetMetadataRawAsync(searchTarget, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException(errors.Last ?? "The search returned nothing.");

        return ReadSearchResults(json);
    }

    public async Task<MediaInspection> InspectAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        var errors = new ErrorLog();
        Metadata? metadata = await new YtdlpClient(Executable, errors).GetMetadataAsync(sourceUrl, cancellationToken);
        if (metadata is null)
            return MediaInspection.Unavailable(errors.Last ?? "This media couldn't be read.");

        FormatMetadata[] formats = (metadata.Formats ?? []).Where(format => format.HasDrm != true).ToArray();
        if (formats.Length == 0)
            return MediaInspection.Unavailable(DrmProblem);

        return new MediaInspection(
            true,
            null,
            formats.Any(format => format.Vcodec is not null and not "none"),
            formats.Max(format => format.Height));
    }

    public async Task<string> DownloadAsync(
        string sourceUrl,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.OutputFolder);

        var errors = new ErrorLog();
        YtdlpClient download = new YtdlpClient(Executable, errors)
            .WithOutputFolder(options.OutputFolder)
            .WithOutputTemplate($"{options.FileNameTemplate}.%(ext)s")
            .WithWindowsFilenames();

        foreach ((string name, string? value) in GetExportArguments(options))
            download = value is null ? download.AddFlag(name) : download.AddOption(name, value);

        string? outputPath = null;

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

        download.ProgressDownload += OnProgress;
        download.PostProcessingStarted += OnPostProcessing;
        download.OutputMessage += OnOutput;
        try
        {
            await download.DownloadAsync(sourceUrl, cancellationToken);
        }
        finally
        {
            download.ProgressDownload -= OnProgress;
            download.PostProcessingStarted -= OnPostProcessing;
            download.OutputMessage -= OnOutput;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            throw new InvalidOperationException(errors.Last ?? "The export finished without producing a file.");

        return Path.GetFullPath(outputPath);
    }

    internal static IReadOnlyList<(string Name, string? Value)> GetExportArguments(ExportOptions options)
    {
        var arguments = new List<(string, string?)>
        {
            ("--no-playlist", null),
            ("--encoding", "utf-8"), // Otherwise non-ANSI characters are dropped from the printed file path.
            ("--progress", null), // --print implies --quiet, which would hide progress.
            ("--print", $"after_move:{OutputMarker}%(filepath)s")
        };

        if (options.EmbedMetadata)
            arguments.Add(("--embed-metadata", null));

        if (options.TrimStart is not null || options.TrimEnd is not null)
        {
            string start = (options.TrimStart ?? TimeSpan.Zero).TotalSeconds.ToString(CultureInfo.InvariantCulture);
            string end = options.TrimEnd?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "inf";
            arguments.Add(("--download-sections", $"*{start}-{end}"));
            arguments.Add(("--force-keyframes-at-cuts", null)); // Exact cuts rather than the nearest keyframe.
        }

        if (options.Kind == MediaKind.Audio)
        {
            arguments.Add(("-f", "ba/b"));
            arguments.Add(("-x", null));
            arguments.Add(("--audio-format", options.Format));
            arguments.Add(("--audio-quality", options.AudioBitrate is int bitrate ? $"{bitrate}K" : "0"));
            if (options.EmbedThumbnail && options.Format is "mp3" or "m4a" or "opus")
                arguments.Add(("--embed-thumbnail", null));
            return arguments;
        }

        if (options.SubtitleLanguages is { Length: > 0 } languages)
        {
            arguments.Add(("--write-subs", null));
            if (options.IncludeAutomaticSubtitles)
                arguments.Add(("--write-auto-subs", null));
            arguments.Add(("--sub-langs", languages));
            arguments.Add(("--embed-subs", null));
        }

        if (options.EmbedThumbnail && options.Format is "mp4" or "mkv")
            arguments.Add(("--embed-thumbnail", null));

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
        if (options.EmbedChapters)
            arguments.Add(("--embed-chapters", null));
        return arguments;
    }

    /// <summary>Turns <c>ERROR: [soundcloud] 123: This video is DRM protected</c> into a sentence for people.</summary>
    internal static string DescribeError(string message)
    {
        string text = Regex.Replace(message.Trim(), @"^ERROR:\s*(\[[^\]]+\]\s*)?([\w-]+:\s)?", string.Empty);
        return text.Contains("DRM", StringComparison.OrdinalIgnoreCase) ? DrmProblem : text;
    }

    // Ytdlp.NET reports yt-dlp failures only through its logger, so each call gets its own.
    private sealed class ErrorLog : ILogger
    {
        public string? Last { get; private set; }

        public void Log(LogType type, string message)
        {
            if (type == LogType.Error)
                Last = DescribeError(message);
        }
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
