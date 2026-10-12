// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Tooling;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Media.Ytdlp;

/// <summary>Searches and downloads media through yt-dlp.</summary>
public sealed partial class YtdlpService(ToolchainService tools) : IMediaDownloader
{
    private const string Executable = "yt-dlp";
    private const string CouldNotStart = "yt-dlp couldn't be started. Try installing it again in Settings.";

    /// <summary>Reads a search without downloading anything.</summary>
    /// <param name="target">What yt-dlp should read, such as <c>ytsearch10:cats</c> or a page's URL.</param>
    /// <param name="skip">How many entries to pass over before the first one that is read.</param>
    /// <param name="limit">How many entries to read, so a long search page isn't read to its end.</param>
    /// <exception cref="InvalidOperationException">yt-dlp isn't installed, or reported an error.</exception>
    public async Task<IReadOnlyList<YtdlpSearchResult>> SearchAsync(
        string target,
        int skip,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(target))
            return [];

        var json = await RunAsync(GetSearchArguments(target, skip, limit), cancellationToken);
        return ReadSearchResults(json);
    }

    /// <summary>Reads what a link is to, without downloading it.</summary>
    /// <returns>The media, or <c>null</c> if the link is to several items or to none.</returns>
    /// <exception cref="InvalidOperationException">yt-dlp isn't installed, or reported an error.</exception>
    public async Task<YtdlpSearchResult?> ReadAsync(string link, CancellationToken cancellationToken = default)
    {
        // A link to a video that is also in a playlist is read as the video.
        string[] arguments =
        [
            "--dump-single-json", "--no-playlist",
            "--quiet", "--no-warnings", "--encoding", "utf-8",
            link
        ];
        var json = await RunAsync(arguments, cancellationToken);
        return ReadMedia(json);
    }

    /// <summary>Reads whose page a link to a channel or an artist is.</summary>
    /// <returns>Their name, or <c>null</c> if the page doesn't say.</returns>
    /// <exception cref="InvalidOperationException">yt-dlp isn't installed, or reported an error.</exception>
    public async Task<string?> ReadCreatorAsync(string link, CancellationToken cancellationToken = default)
    {
        // The page says whose it is before any of its entries, so one entry is all that is asked for.
        string[] arguments =
        [
            "--dump-single-json", "--flat-playlist", "--playlist-end", "1",
            "--quiet", "--no-warnings", "--encoding", "utf-8",
            link
        ];
        var json = await RunAsync(arguments, cancellationToken);
        return ReadCreator(json);
    }

    public Task<IReadOnlyList<string>> DownloadAsync(
        string sourceUrl,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    internal static IReadOnlyList<string> GetSearchArguments(string target, int skip, int limit) =>
    [
        // One JSON document listing the entries, without opening each entry's own page.
        "--dump-single-json", "--flat-playlist",
        // yt-dlp counts entries from 1.
        "--playlist-start", (skip + 1).ToString(CultureInfo.InvariantCulture),
        "--playlist-end", (skip + limit).ToString(CultureInfo.InvariantCulture),
        "--quiet", "--no-warnings", "--encoding", "utf-8",
        // YouTube's results only say roughly how old a video is ("3 years ago"); this turns that into a date.
        "--extractor-args", "youtubetab:approximate_date",
        target
    ];

    internal static IReadOnlyList<YtdlpSearchResult> ReadSearchResults(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var hasEntries = root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array;
        if (!hasEntries)
            return [];

        return entries.EnumerateArray().Select(ReadEntry).OfType<YtdlpSearchResult>().ToList();
    }

    internal static string? ReadCreator(string json)
    {
        using var document = JsonDocument.Parse(json);
        var page = document.RootElement;
        if (page.ValueKind != JsonValueKind.Object)
            return null;

        return GetString(page, "channel") ?? GetString(page, "uploader") ?? GetString(page, "title");
    }

    internal static YtdlpSearchResult? ReadMedia(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // yt-dlp writes "null" for a link it found nothing at.
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        // A link to a playlist or a channel is to many items, not to one.
        var isOneItem = GetString(root, "_type") != "playlist";
        return isOneItem ? ReadEntry(root) : null;
    }

    private static YtdlpSearchResult? ReadEntry(JsonElement entry)
    {
        var identity = GetString(entry, "id");
        var title = GetString(entry, "title");
        // A search page also lists playlists and channels (as "tabs"), which aren't media.
        var isTab = GetString(entry, "ie_key") == "YoutubeTab";
        if (identity is null || title is null || isTab)
            return null;

        var duration = GetNumber(entry, "duration");
        var views = GetNumber(entry, "view_count");
        // Falls back to the release time. A live stream has neither, nor a duration or a view count.
        var timestamp = GetNumber(entry, "timestamp") ?? GetNumber(entry, "release_timestamp");
        var creatorLink = GetString(entry, "channel_url") ?? GetString(entry, "uploader_url");
        Uri.TryCreate(creatorLink, UriKind.Absolute, out var creatorUri);
        return new YtdlpSearchResult(
            identity,
            GetString(entry, "webpage_url") ?? GetString(entry, "url"),
            GetThumbnail(entry),
            title,
            GetString(entry, "uploader") ?? GetString(entry, "channel"),
            duration is null ? null : TimeSpan.FromSeconds(duration.Value),
            views is null ? null : (long)views.Value,
            timestamp is null ? null : DateTimeOffset.FromUnixTimeSeconds((long)timestamp.Value),
            creatorUri);
    }

    // Runs yt-dlp and returns what it wrote to standard output.
    private async Task<string> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var path = tools.Locate(Executable)
            ?? throw new InvalidOperationException("yt-dlp isn't installed. Install it in Settings.");

        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Start(startInfo);
        try
        {
            var readingOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var readingErrors = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = await readingOutput;
            if (!string.IsNullOrWhiteSpace(output))
                return output;

            // yt-dlp's last line of errors is the one that says what went wrong.
            var errorText = await readingErrors;
            var errors = errorText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var lastError = errors.LastOrDefault() ?? "yt-dlp didn't return anything.";

            // An error is written as "ERROR: [site] id: what went wrong", of which only the end is for the user.
            var reason = ErrorPrefix().Replace(lastError, string.Empty);
            throw new InvalidOperationException(reason);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    [GeneratedRegex("""^ERROR:\s*(\[[^\]]+\]\s*[^:]+:\s*)?""")]
    private static partial Regex ErrorPrefix();

    private static Process Start(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException(CouldNotStart);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(CouldNotStart, exception);
        }
    }

    // Returns the largest thumbnail, which is the last one because yt-dlp lists them from small to large.
    private static Uri? GetThumbnail(JsonElement entry)
    {
        var hasProperty = entry.TryGetProperty("thumbnails", out var thumbnails);
        var hasThumbnails = hasProperty && thumbnails.ValueKind == JsonValueKind.Array;
        if (!hasThumbnails)
            return null;

        foreach (var thumbnail in thumbnails.EnumerateArray().Reverse())
        {
            if (Uri.TryCreate(GetString(thumbnail, "url"), UriKind.Absolute, out var uri))
                return uri;
        }
        return null;
    }

    // yt-dlp often writes a missing value as null or an empty string rather than leaving it out.
    internal static string? GetString(JsonElement entry, string name)
    {
        var isString = entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String;
        if (!isString)
            return null;

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    internal static double? GetNumber(JsonElement entry, string name)
    {
        var isNumber = entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number;
        return isNumber ? value.GetDouble() : null;
    }
}
