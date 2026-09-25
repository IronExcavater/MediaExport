// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using System.Text.Json;
using YtdlpClient = ManuHub.Ytdlp.NET.Ytdlp;

namespace MediaExport.Core.Media.Ytdlp;

public sealed class YtdlpService
{
    private readonly YtdlpClient _ytdlp = new("yt-dlp.exe");

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
        string searchTarget,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTarget))
            return [];

        string json = await _ytdlp.GetMetadataRawAsync(searchTarget, cancellationToken)
            ?? throw new InvalidOperationException("yt-dlp returned no search metadata.");

        return ReadSearchResults(json);
    }

    internal static IReadOnlyList<MediaSearchResult> ReadSearchResults(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement entries = document.RootElement.TryGetProperty("entries", out JsonElement value)
            ? value
            : document.RootElement;

        if (entries.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<MediaSearchResult>();
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            string? identity = GetString(entry, "id");
            string? sourceUrl = GetSourceUrl(entry, identity);
            string? title = GetString(entry, "title");

            if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(sourceUrl) || string.IsNullOrWhiteSpace(title))
                continue;

            results.Add(new MediaSearchResult(
                identity,
                sourceUrl,
                GetThumbnailUri(entry),
                title,
                GetString(entry, "uploader") ?? GetString(entry, "channel"),
                GetDuration(entry)));
        }

        return results;
    }

    private static string? GetString(JsonElement entry, string propertyName) =>
        entry.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetSourceUrl(JsonElement entry, string? identity)
    {
        string? directUrl = GetString(entry, "webpage_url") ?? GetString(entry, "original_url");
        if (!string.IsNullOrWhiteSpace(directUrl))
            return directUrl;

        if (string.Equals(GetString(entry, "ie_key"), "Youtube", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(identity))
        {
            return $"https://www.youtube.com/watch?v={identity}";
        }

        return GetString(entry, "url");
    }

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
