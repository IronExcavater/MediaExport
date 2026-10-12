// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Ytdlp;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Media.Providers;

/// <summary>Reads SoundCloud's trending chart from the API its website uses, because yt-dlp can't list the chart.</summary>
/// <remarks>The API asks for the website's client id, which is found in the site's scripts and is replaced now and then.</remarks>
public sealed partial class SoundCloudCharts(HttpClient http)
{
    private string? _clientId;

    /// <exception cref="InvalidOperationException">SoundCloud refused, or its website has changed.</exception>
    public async Task<IReadOnlyList<YtdlpSearchResult>> ReadAsync(
        int skip,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var response = await GetChartAsync(skip, limit, cancellationToken);
        var isRefused = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
        if (isRefused)
        {
            // The client id has been replaced since it was read, so it is read again and the chart asked for once more.
            response.Dispose();
            _clientId = null;
            response = await GetChartAsync(skip, limit, cancellationToken);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                throw new InvalidOperationException($"SoundCloud's chart couldn't be read ({status}).");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ReadTracks(json);
        }
    }

    private async Task<HttpResponseMessage> GetChartAsync(int skip, int limit, CancellationToken cancellationToken)
    {
        var clientId = await GetClientIdAsync(cancellationToken);
        var chart = "https://api-v2.soundcloud.com/charts?kind=trending&genre=soundcloud%3Agenres%3Aall-music"
            + $"&limit={limit}&offset={skip}&client_id={Uri.EscapeDataString(clientId)}";
        return await http.GetAsync(chart, cancellationToken);
    }

    private async Task<string> GetClientIdAsync(CancellationToken cancellationToken)
    {
        if (_clientId is not null)
            return _clientId;

        // The id is in one of the site's scripts, usually the last.
        var home = await http.GetStringAsync("https://soundcloud.com/", cancellationToken);
        var scripts = ScriptSource().Matches(home).Select(match => match.Groups[1].Value).Reverse();
        foreach (var script in scripts)
        {
            var code = await http.GetStringAsync(script, cancellationToken);
            var id = ClientId().Match(code);
            if (id.Success)
                return _clientId = id.Groups[1].Value;
        }

        throw new InvalidOperationException("SoundCloud's website didn't say how to read its chart.");
    }

    internal static IReadOnlyList<YtdlpSearchResult> ReadTracks(string json)
    {
        using var document = JsonDocument.Parse(json);
        var hasCollection = document.RootElement.TryGetProperty("collection", out var collection);
        var hasTracks = hasCollection && collection.ValueKind == JsonValueKind.Array;
        if (!hasTracks)
            return [];

        var results = new List<YtdlpSearchResult>();
        foreach (var item in collection.EnumerateArray())
        {
            var hasTrack = item.TryGetProperty("track", out var track) && track.TryGetProperty("id", out _);
            if (!hasTrack)
                continue;

            var title = YtdlpService.GetString(track, "title");
            var page = YtdlpService.GetString(track, "permalink_url");
            if (title is null || page is null)
                continue;

            var hasUser = track.TryGetProperty("user", out var user);
            var creator = hasUser ? YtdlpService.GetString(user, "username") : null;
            var creatorLink = hasUser ? YtdlpService.GetString(user, "permalink_url") : null;
            Uri.TryCreate(creatorLink, UriKind.Absolute, out var creatorUri);

            // The artwork's address names its size, and the chart gives the smallest.
            var artwork = YtdlpService.GetString(track, "artwork_url")?.Replace("-large.", "-t500x500.");
            Uri.TryCreate(artwork, UriKind.Absolute, out var thumbnailUri);

            var milliseconds = YtdlpService.GetNumber(track, "duration");
            var plays = YtdlpService.GetNumber(track, "playback_count");
            var shownDate = YtdlpService.GetString(track, "display_date");
            var hasDate = DateTimeOffset.TryParse(
                shownDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var published);

            results.Add(new YtdlpSearchResult(
                track.GetProperty("id").GetRawText(),
                page,
                thumbnailUri,
                title,
                creator,
                milliseconds is null ? null : TimeSpan.FromMilliseconds(milliseconds.Value),
                plays is null ? null : (long)plays.Value,
                hasDate ? published : null,
                creatorUri));
        }
        return results;
    }

    [GeneratedRegex("""<script[^>]+src=["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptSource();

    [GeneratedRegex("""client_id\s*:\s*["']([0-9A-Za-z]{32})["']""")]
    private static partial Regex ClientId();
}
