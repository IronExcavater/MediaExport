// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Media.Providers;

/// <summary>The regional daily music-video chart that replaced YouTube's former general Trending page.</summary>
public sealed class YouTubeCharts(HttpClient http)
{
    private static readonly TimeSpan CacheLife = TimeSpan.FromMinutes(30);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<YouTubeChartEntry>? _entries;
    private DateTimeOffset _expires;

    public async Task<(IReadOnlyList<YouTubeChartEntry> Entries, bool HasMore)> GetAsync(
        int offset,
        int take,
        CancellationToken cancellationToken = default)
    {
        offset = Math.Max(0, offset);
        take = Math.Max(1, take);
        IReadOnlyList<YouTubeChartEntry> entries = await EntriesAsync(cancellationToken);
        return (entries.Skip(offset).Take(take).ToArray(), entries.Count > offset + take);
    }

    private async Task<IReadOnlyList<YouTubeChartEntry>> EntriesAsync(CancellationToken cancellationToken)
    {
        if (_entries is not null && DateTimeOffset.UtcNow < _expires)
            return _entries;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_entries is not null && DateTimeOffset.UtcNow < _expires)
                return _entries;

            using JsonDocument charts = await BrowseAsync("FEmusic_charts", cancellationToken);
            string playlist = FindChartPlaylist(charts.RootElement)
                ?? throw new InvalidOperationException("YouTube Music didn't return a regional daily chart.");
            using JsonDocument tracks = await BrowseAsync(playlist, cancellationToken);
            _entries = ReadEntries(tracks.RootElement).ToArray();
            _expires = DateTimeOffset.UtcNow + CacheLife;
            return _entries;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<JsonDocument> BrowseAsync(string browseId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    context = new { client = new { clientName = "WEB_REMIX", clientVersion = "1.20240101.01.00" } },
                    browseId
                }),
                Encoding.UTF8,
                "application/json")
        };
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string? FindChartPlaylist(JsonElement root)
    {
        foreach (JsonElement item in Descendants(root, "musicTwoRowItemRenderer"))
        {
            string title = string.Concat(Runs(item, "title").Select(Text));
            string? browseId = String(At(item, "navigationEndpoint", "browseEndpoint"), "browseId");
            if (title.StartsWith("Daily Top Music Videos", StringComparison.OrdinalIgnoreCase)
                && browseId is { Length: > 2 })
                return browseId;
        }

        return null;
    }

    private static IEnumerable<YouTubeChartEntry> ReadEntries(JsonElement root)
    {
        foreach (JsonElement song in Descendants(root, "musicResponsiveListItemRenderer"))
        {
            string? id = String(At(song, "playlistItemData"), "videoId");
            string? title = Text(Runs(song, 0).FirstOrDefault());
            if (id is null || title is null)
                continue;

            JsonElement[] byline = Runs(song, 1).ToArray();
            JsonElement[] artists = byline.TakeWhile(run => Text(run) != " • ").ToArray();
            string? creator = artists.Length == 0 ? null : string.Concat(artists.Select(Text));
            string? channel = String(At(artists.FirstOrDefault(), "navigationEndpoint", "browseEndpoint"), "browseId");
            string? cover = String(Items(At(song, "thumbnail", "musicThumbnailRenderer", "thumbnail", "thumbnails")).LastOrDefault(), "url");

            yield return new YouTubeChartEntry(
                id,
                title,
                creator,
                channel,
                Clock(Text(byline.LastOrDefault())),
                Link(Regex.Replace(cover ?? string.Empty, @"=w\d+-h\d+", "=w480-h480")),
                Plays(Text(Runs(song, 2).FirstOrDefault())));
        }
    }

    private static IEnumerable<JsonElement> Descendants(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(property, out JsonElement match))
                yield return match;

            foreach (JsonProperty child in element.EnumerateObject())
                foreach (JsonElement descendant in Descendants(child.Value, property))
                    yield return descendant;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
                foreach (JsonElement descendant in Descendants(child, property))
                    yield return descendant;
        }
    }

    private static IEnumerable<JsonElement> Runs(JsonElement item, string name) => Items(At(item, name, "runs"));

    private static IEnumerable<JsonElement> Runs(JsonElement song, int column) =>
        Items(At(song, "flexColumns", column, "musicResponsiveListItemFlexColumnRenderer", "text", "runs"));

    private static JsonElement At(JsonElement element, params object[] path)
    {
        foreach (object step in path)
        {
            if (step is string name && element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement child))
                element = child;
            else if (step is int index && element.ValueKind == JsonValueKind.Array && index < element.GetArrayLength())
                element = element[index];
            else
                return default;
        }

        return element;
    }

    private static IEnumerable<JsonElement> Items(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];

    private static string? String(JsonElement element, string name) =>
        At(element, name) is { ValueKind: JsonValueKind.String } value ? WebUtility.HtmlDecode(value.GetString()) : null;

    private static string? Text(JsonElement run) => String(run, "text");

    private static Uri? Link(string? text) => Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ? uri : null;

    private static TimeSpan? Clock(string? text)
    {
        double seconds = 0;
        foreach (string part in (text ?? string.Empty).Split(':'))
        {
            if (!int.TryParse(part, out int number))
                return null;
            seconds = seconds * 60 + number;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static long? Plays(string? text)
    {
        Match plays = Regex.Match(text ?? string.Empty, @"^([\d.]+)([KMB]?) ");
        if (!plays.Success || !double.TryParse(plays.Groups[1].Value, CultureInfo.InvariantCulture, out double number))
            return null;

        return (long)(number * plays.Groups[2].Value switch { "K" => 1e3, "M" => 1e6, "B" => 1e9, _ => 1 });
    }
}

public sealed record YouTubeChartEntry(
    string Id,
    string Title,
    string? Creator,
    string? ChannelId,
    TimeSpan? Duration,
    Uri? Thumbnail,
    long? Views);
