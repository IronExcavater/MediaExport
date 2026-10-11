// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Tooling;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MediaExport.Core.Media.Ytdlp;

/// <summary>Searches and downloads media through yt-dlp.</summary>
public sealed class YtdlpService(ToolchainService tools) : IMediaDownloader
{
    private const string Executable = "yt-dlp";

    /// <summary>Reads a search without downloading anything.</summary>
    /// <param name="target">What yt-dlp should read: a search such as <c>ytsearch10:cats</c>, or a page's URL.</param>
    /// <param name="skip">How many entries to pass over before the first one that is read.</param>
    /// <param name="limit">How many entries to read, so a long search page isn't read to its end.</param>
    /// <exception cref="InvalidOperationException">yt-dlp isn't installed, or reported an error.</exception>
    public async Task<IReadOnlyList<YtdlpSearchResult>> SearchAsync(string target, int skip, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(target))
            return [];

        var json = await RunAsync(GetSearchArguments(target, skip, limit), cancellationToken);
        return ReadSearchResults(json);
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

        var results = new List<YtdlpSearchResult>();
        foreach (var entry in entries.EnumerateArray())
        {
            var identity = GetString(entry, "id");
            var title = GetString(entry, "title");
            // A search page also lists playlists and channels (as "tabs"), which aren't media.
            var isTab = GetString(entry, "ie_key") == "YoutubeTab";
            if (identity is null || title is null || isTab)
                continue;

            var duration = GetNumber(entry, "duration");
            var views = GetNumber(entry, "view_count");
            // A result with no upload time may still say when it was released. A live stream has neither, nor
            // a duration or a view count.
            var timestamp = GetNumber(entry, "timestamp") ?? GetNumber(entry, "release_timestamp");
            results.Add(new YtdlpSearchResult(
                identity,
                GetString(entry, "webpage_url") ?? GetString(entry, "url"),
                GetThumbnail(entry),
                title,
                GetString(entry, "uploader") ?? GetString(entry, "channel"),
                duration is null ? null : TimeSpan.FromSeconds(duration.Value),
                views is null ? null : (long)views.Value,
                timestamp is null ? null : DateTimeOffset.FromUnixTimeSeconds((long)timestamp.Value)));
        }
        return results;
    }

    // Runs yt-dlp and returns what it wrote to standard output.
    private async Task<string> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var path = tools.Locate(Executable)
            ?? throw new InvalidOperationException("yt-dlp isn't installed.");

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
            var errors = (await readingErrors).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            throw new InvalidOperationException(errors.LastOrDefault() ?? "yt-dlp returned nothing.");
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static Process Start(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException("yt-dlp couldn't be started.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("yt-dlp couldn't be started.", exception);
        }
    }

    // Returns the largest thumbnail, which is the last one because yt-dlp lists them from small to large.
    private static Uri? GetThumbnail(JsonElement entry)
    {
        var hasThumbnails = entry.TryGetProperty("thumbnails", out var thumbnails) && thumbnails.ValueKind == JsonValueKind.Array;
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
    private static string? GetString(JsonElement entry, string name)
    {
        var isString = entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String;
        if (!isString)
            return null;

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static double? GetNumber(JsonElement entry, string name)
    {
        var isNumber = entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number;
        return isNumber ? value.GetDouble() : null;
    }
}
