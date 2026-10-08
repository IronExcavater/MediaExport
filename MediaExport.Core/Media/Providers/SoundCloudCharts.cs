// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Media.Providers;

/// <summary>Reads SoundCloud's public web chart with the same short-lived client id used by its site.</summary>
public sealed class SoundCloudCharts(HttpClient http)
{
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private string? _clientId;

    public async Task<(IReadOnlyList<SoundCloudChartEntry> Entries, bool HasMore)> GetAsync(
        int offset,
        int take,
        CancellationToken cancellationToken = default)
    {
        offset = Math.Max(0, offset);
        take = Math.Max(1, take);
        using JsonDocument document = await GetChartAsync(offset, take, retry: true, cancellationToken);
        SoundCloudChartEntry[] entries = ReadEntries(document.RootElement).Take(take).ToArray();
        bool hasMore = At(document.RootElement, "next_href").ValueKind == JsonValueKind.String || entries.Length == take;
        return (entries, hasMore);
    }

    private async Task<JsonDocument> GetChartAsync(int offset, int take, bool retry, CancellationToken cancellationToken)
    {
        string clientId = await ClientIdAsync(cancellationToken);
        string url = "https://api-v2.soundcloud.com/charts?kind=trending&genre=soundcloud%3Agenres%3Aall-music"
            + $"&limit={take}&offset={offset}&linked_partitioning=1&client_id={Uri.EscapeDataString(clientId)}";
        using HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
        if (retry && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _clientId = null;
            return await GetChartAsync(offset, take, retry: false, cancellationToken);
        }

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<string> ClientIdAsync(CancellationToken cancellationToken)
    {
        if (_clientId is not null)
            return _clientId;

        await _clientLock.WaitAsync(cancellationToken);
        try
        {
            if (_clientId is not null)
                return _clientId;

            string home = await http.GetStringAsync("https://soundcloud.com/", cancellationToken);
            string[] scripts = Regex.Matches(home, "<script[^>]+src=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)
                .Select(match => match.Groups[1].Value)
                .Reverse()
                .ToArray();
            foreach (string script in scripts)
            {
                string code = await http.GetStringAsync(new Uri(new Uri("https://soundcloud.com/"), script), cancellationToken);
                Match id = Regex.Match(code, "client_id\\s*:\\s*[\"']([0-9A-Za-z]{32})[\"']");
                if (id.Success)
                    return _clientId = id.Groups[1].Value;
            }

            throw new InvalidOperationException("SoundCloud didn't provide a web client id.");
        }
        finally
        {
            _clientLock.Release();
        }
    }

    private static IEnumerable<SoundCloudChartEntry> ReadEntries(JsonElement root)
    {
        foreach (JsonElement item in Items(At(root, "collection")))
        {
            JsonElement track = At(item, "track");
            string? id = Value(track, "id");
            string? url = Text(track, "permalink_url");
            if (id is null || url is null)
                continue;

            JsonElement user = At(track, "user");
            string? artwork = Text(track, "artwork_url");
            if (artwork is not null)
                artwork = artwork.Replace("-large.", "-t500x500.", StringComparison.Ordinal);
            yield return new SoundCloudChartEntry(
                id,
                url,
                Text(track, "title") ?? url,
                Text(At(track, "publisher_metadata"), "artist") ?? Text(user, "username"),
                Text(user, "permalink_url"),
                Link(artwork),
                Number(track, "duration") is { } milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : null,
                (long?)Number(track, "playback_count"),
                Date(Text(track, "display_date")) ?? Date(Text(track, "release_date")));
        }
    }

    private static JsonElement At(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement child) ? child : default;

    private static IEnumerable<JsonElement> Items(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];

    private static string? Text(JsonElement element, string name) =>
        At(element, name) is { ValueKind: JsonValueKind.String } value ? WebUtility.HtmlDecode(value.GetString()) : null;

    private static string? Value(JsonElement element, string name) => At(element, name) switch
    {
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
        _ => null
    };

    private static double? Number(JsonElement element, string name) =>
        At(element, name) is { ValueKind: JsonValueKind.Number } value ? value.GetDouble() : null;

    private static Uri? Link(string? text) => Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ? uri : null;

    private static DateTimeOffset? Date(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date) ? date : null;
}

public sealed record SoundCloudChartEntry(
    string Id,
    string Url,
    string Title,
    string? Creator,
    string? CreatorUrl,
    Uri? Thumbnail,
    TimeSpan? Duration,
    long? Views,
    DateTimeOffset? Published);
