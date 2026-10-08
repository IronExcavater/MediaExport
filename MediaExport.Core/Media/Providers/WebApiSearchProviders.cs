// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Media.Providers;

/// <summary>Sources yt-dlp can download from but not search (or not search well), searched through their web APIs instead.</summary>
public abstract class WebApiSearchProvider(HttpClient http) : IMediaSearchProvider
{
    protected const int ResultCount = 10;

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }

    protected HttpClient Http { get; } = http;

    protected abstract string GetSearchUrl(string query, SearchSort sort);

    /// <summary>The search request; sources that want more than a link (a token, say) add it here.</summary>
    protected virtual Task<HttpRequestMessage> CreateRequestAsync(string query, SearchSort sort, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpRequestMessage(HttpMethod.Get, GetSearchUrl(query, sort)));

    protected abstract IEnumerable<MediaSearchResult> ReadResults(JsonElement root);

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
        string query,
        SearchSort sort = SearchSort.Relevance,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        using HttpRequestMessage request = await CreateRequestAsync(query.Trim(), sort, cancellationToken);
        using JsonDocument document = await GetJsonAsync(request, cancellationToken);
        return await CompleteAsync(ReadResults(document.RootElement).Take(ResultCount).ToArray(), cancellationToken);
    }

    /// <summary>Fills in what the search itself left out, for sources that need a second look to say it.</summary>
    protected virtual Task<IReadOnlyList<MediaSearchResult>> CompleteAsync(IReadOnlyList<MediaSearchResult> results, CancellationToken cancellationToken) =>
        Task.FromResult(results);

    protected async Task<JsonDocument> GetJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    /// <summary>Follows names and positions into nested JSON; nothing (<c>Undefined</c>) if a step is missing.</summary>
    protected static JsonElement At(JsonElement element, params object[] path)
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

    protected static IEnumerable<JsonElement> Items(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];

    // Uploaders' text sometimes arrives HTML-encoded (&#34; for a quote).
    protected static string? Text(JsonElement element, string name) =>
        At(element, name) is { ValueKind: JsonValueKind.String } value ? WebUtility.HtmlDecode(value.GetString()) : null;

    // Some fields come as a list when there are several (e.g. an Internet Archive item's creators).
    protected static string? FirstText(JsonElement element, string name) =>
        At(element, name, 0) is { ValueKind: JsonValueKind.String } first ? WebUtility.HtmlDecode(first.GetString()) : Text(element, name);

    protected static double? Number(JsonElement element, string name) =>
        At(element, name) is { ValueKind: JsonValueKind.Number } value ? value.GetDouble() : null;

    protected static long? Count(JsonElement element, string name) => (long?)Number(element, name);

    protected static TimeSpan? Seconds(JsonElement element, string name) =>
        Number(element, name) is { } seconds ? TimeSpan.FromSeconds(seconds) : null;

    protected static Uri? Link(string? url) => Uri.TryCreate(url, UriKind.Absolute, out Uri? link) ? link : null;

    /// <summary>A date as sites write them out: <c>2018-03-07T01:33:16Z</c>.</summary>
    protected static DateTimeOffset? Date(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date) ? date : null;

    /// <summary>A date counted in seconds since 1970.</summary>
    protected static DateTimeOffset? Date(double? seconds) =>
        seconds is { } time ? DateTimeOffset.FromUnixTimeSeconds((long)time) : null;

    /// <summary>A length written as a clock reads: <c>4:09</c>, <c>1:02:03</c>, or all in minutes (<c>125:30</c>).</summary>
    protected static TimeSpan? Clock(string? text)
    {
        double seconds = 0;
        foreach (string part in (text ?? "").Split(':'))
        {
            if (!int.TryParse(part, out int number))
                return null;
            seconds = seconds * 60 + number;
        }

        return TimeSpan.FromSeconds(seconds);
    }
}

/// <summary>Songs, searched the way YouTube Music's own page does; they play and export as YouTube videos.</summary>
public sealed class YouTubeMusicSearchProvider(HttpClient http) : WebApiSearchProvider(http)
{
    // Asks for songs only, leaving out albums, artists, playlists and videos.
    private const string SongsOnly = "EgWKAQIIAWoKEAkQBRAKEAMQBA%3D%3D";

    public override string Id => "youtubemusic";
    public override string DisplayName => "YouTube Music";
    public override string Icon => "youtube-music";

    protected override string GetSearchUrl(string query, SearchSort sort) => "https://music.youtube.com/youtubei/v1/search?prettyPrint=false";

    protected override async Task<HttpRequestMessage> CreateRequestAsync(string query, SearchSort sort, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = await base.CreateRequestAsync(query, sort, cancellationToken);
        request.Method = HttpMethod.Post;
        request.Content = new StringContent(
            $$$"""{"context":{"client":{"clientName":"WEB_REMIX","clientVersion":"1.20240101.01.00"}},"params":"{{{SongsOnly}}}","query":"{{{JavaScriptEncoder.Default.Encode(query)}}}"}""",
            Encoding.UTF8,
            "application/json");
        return request;
    }

    protected override IEnumerable<MediaSearchResult> ReadResults(JsonElement root)
    {
        JsonElement sections = At(root, "contents", "tabbedSearchResultsRenderer", "tabs", 0, "tabRenderer", "content", "sectionListRenderer", "contents");
        foreach (JsonElement item in Items(sections).SelectMany(section => Items(At(section, "musicShelfRenderer", "contents"))))
        {
            JsonElement song = At(item, "musicResponsiveListItemRenderer");
            if (Text(At(song, "playlistItemData"), "videoId") is not { } id || Text(Runs(song, 0).FirstOrDefault(), "text") is not { } title)
                continue;

            // Under the title: the artists, then the album, then the length, with a dot between each.
            JsonElement[] byline = Runs(song, 1).ToArray();
            JsonElement[] artists = byline.TakeWhile(run => Text(run, "text") != " • ").ToArray();
            string url = $"https://music.youtube.com/watch?v={Uri.EscapeDataString(id)}";
            yield return new MediaSearchResult(
                id,
                url,
                ReadCover(song),
                title,
                artists.Length > 0 ? string.Concat(artists.Select(run => Text(run, "text"))) : null,
                Clock(Text(byline.LastOrDefault(), "text")),
                MediaKind.Audio,
                Text(At(artists.FirstOrDefault(), "navigationEndpoint", "browseEndpoint"), "browseId") is { } channel ? $"https://music.youtube.com/channel/{channel}" : null,
                EmbedLinks.For(Id, id, url),
                Id,
                ReadPlays(Text(Runs(song, 2).FirstOrDefault(), "text")));
        }
    }

    private static IEnumerable<JsonElement> Runs(JsonElement song, int column) =>
        Items(At(song, "flexColumns", column, "musicResponsiveListItemFlexColumnRenderer", "text", "runs"));

    // Covers are offered small (…=w120-h120-…); the size is part of the address, so a bigger one is asked for.
    private static Uri? ReadCover(JsonElement song) =>
        Link(Regex.Replace(Text(Items(At(song, "thumbnail", "musicThumbnailRenderer", "thumbnail", "thumbnails")).LastOrDefault(), "url") ?? "", @"=w\d+-h\d+", "=w480-h480"));

    // Plays are written short: "1.9B plays".
    internal static long? ReadPlays(string? text)
    {
        Match plays = Regex.Match(text ?? "", @"^([\d.]+)([KMB]?) ");
        if (!plays.Success || !double.TryParse(plays.Groups[1].Value, CultureInfo.InvariantCulture, out double number))
            return null;

        return (long)(number * plays.Groups[2].Value switch { "K" => 1e3, "M" => 1e6, "B" => 1e9, _ => 1 });
    }
}

public sealed class MixcloudSearchProvider(HttpClient http) : WebApiSearchProvider(http)
{
    public override string Id => "mixcloud";
    public override string DisplayName => "Mixcloud";
    public override string Icon => "mixcloud";

    // Its search has one order, and doesn't say how often a show's been played.
    protected override string GetSearchUrl(string query, SearchSort sort) =>
        $"https://api.mixcloud.com/search/?q={Uri.EscapeDataString(query)}&type=cloudcast&limit={ResultCount}";

    protected override IEnumerable<MediaSearchResult> ReadResults(JsonElement root)
    {
        foreach (JsonElement show in Items(At(root, "data")))
        {
            if (Text(show, "key") is not { } id || Text(show, "url") is not { } url)
                continue;

            JsonElement user = At(show, "user");
            yield return new MediaSearchResult(
                id,
                url,
                Link(Text(At(show, "pictures"), "640wx640h")),
                Text(show, "name") ?? url,
                Text(user, "name"),
                Seconds(show, "audio_length"),
                MediaKind.Audio,
                Text(user, "url"),
                EmbedLinks.For(Id, id, url),
                Id,
                Published: Date(Text(show, "created_time")));
        }
    }
}

/// <summary>Vimeo's search wants a token; this uses the short-lived one its site gives every visitor.</summary>
public sealed class VimeoSearchProvider(HttpClient http) : WebApiSearchProvider(http)
{
    // Tokens last 15 minutes.
    private static readonly TimeSpan TokenLife = TimeSpan.FromMinutes(10);

    private string? _token;
    private DateTime _tokenTime;

    public override string Id => "vimeo";
    public override string DisplayName => "Vimeo";
    public override string Icon => "vimeo";

    protected override string GetSearchUrl(string query, SearchSort sort) =>
        $"https://api.vimeo.com/videos?query={Uri.EscapeDataString(query)}&per_page={ResultCount}"
        + $"&sort={(sort == SearchSort.Popularity ? "plays" : "relevant")}&direction=desc"
        + "&fields=uri,name,link,duration,created_time,stats.plays,pictures.sizes.link,pictures.sizes.width,user.name,user.link";

    protected override async Task<HttpRequestMessage> CreateRequestAsync(string query, SearchSort sort, CancellationToken cancellationToken)
    {
        if (_token is null || DateTime.UtcNow - _tokenTime > TokenLife)
        {
            // Only handed out to requests that look like the site's own.
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "https://vimeo.com/_next/jwt");
            tokenRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
            using JsonDocument document = await GetJsonAsync(tokenRequest, cancellationToken);
            _token = Text(document.RootElement, "token") ?? throw new InvalidOperationException("Vimeo didn't allow a search.");
            _tokenTime = DateTime.UtcNow;
        }

        HttpRequestMessage request = await base.CreateRequestAsync(query, sort, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("jwt", _token);
        return request;
    }

    protected override IEnumerable<MediaSearchResult> ReadResults(JsonElement root)
    {
        foreach (JsonElement video in Items(At(root, "data")))
        {
            // Its id is the end of its uri: /videos/1084537.
            if (Text(video, "uri")?.Split('/')[^1] is not { Length: > 0 } id || Text(video, "link") is not { } url)
                continue;

            // Sizes run from small to large: the first one wide enough for a result's picture, or else the largest.
            JsonElement[] pictures = Items(At(video, "pictures", "sizes")).ToArray();
            JsonElement picture = pictures.Where(size => Number(size, "width") >= 480).Append(pictures.LastOrDefault()).First();
            JsonElement user = At(video, "user");
            yield return new MediaSearchResult(
                id,
                url,
                Link(Text(picture, "link")),
                Text(video, "name") ?? url,
                Text(user, "name"),
                Seconds(video, "duration"),
                MediaKind.Video,
                Text(user, "link"),
                EmbedLinks.For(Id, id, url),
                Id,
                Count(At(video, "stats"), "plays"),
                Date(Text(video, "created_time")));
        }
    }
}

public sealed class DailymotionSearchProvider(HttpClient http) : WebApiSearchProvider(http)
{
    public override string Id => "dailymotion";
    public override string DisplayName => "Dailymotion";
    public override string Icon => "dailymotion";

    protected override string GetSearchUrl(string query, SearchSort sort) =>
        $"https://api.dailymotion.com/videos?search={Uri.EscapeDataString(query)}&limit={ResultCount}"
        + $"&sort={(sort == SearchSort.Popularity ? "visited" : "relevance")}"
        + "&fields=id,title,duration,thumbnail_480_url,owner.screenname,owner.url,url,views_total,created_time";

    protected override IEnumerable<MediaSearchResult> ReadResults(JsonElement root)
    {
        foreach (JsonElement video in Items(At(root, "list")))
        {
            if (Text(video, "id") is not { } id || Text(video, "url") is not { } url)
                continue;

            yield return new MediaSearchResult(
                id,
                url,
                Link(Text(video, "thumbnail_480_url")),
                Text(video, "title") ?? url,
                Text(video, "owner.screenname"),
                Seconds(video, "duration"),
                MediaKind.Video,
                Text(video, "owner.url"),
                EmbedLinks.For(Id, id, url),
                Id,
                Count(video, "views_total"),
                Date(Number(video, "created_time")));
        }
    }
}

/// <summary>Bilibili's search turns away visitors it hasn't seen; it's first asked for the id it gives new ones.</summary>
public sealed class BilibiliSearchProvider(HttpClient http) : WebApiSearchProvider(http)
{
    private string? _visitor;

    public override string Id => "bilibili";
    public override string DisplayName => "Bilibili";
    public override string Icon => "bilibili";

    protected override string GetSearchUrl(string query, SearchSort sort) =>
        $"https://api.bilibili.com/x/web-interface/search/type?search_type=video&keyword={Uri.EscapeDataString(query)}&page_size={ResultCount}"
        + $"&order={(sort == SearchSort.Popularity ? "click" : "totalrank")}";

    protected override async Task<HttpRequestMessage> CreateRequestAsync(string query, SearchSort sort, CancellationToken cancellationToken)
    {
        if (_visitor is null)
        {
            using var visit = new HttpRequestMessage(HttpMethod.Get, "https://api.bilibili.com/x/frontend/finger/spi");
            using JsonDocument document = await GetJsonAsync(visit, cancellationToken);
            _visitor = Text(At(document.RootElement, "data"), "b_3") ?? throw new InvalidOperationException("Bilibili didn't allow a search.");
        }

        HttpRequestMessage request = await base.CreateRequestAsync(query, sort, cancellationToken);
        request.Headers.Add("Cookie", $"buvid3={_visitor}");
        request.Headers.Referrer = new Uri("https://www.bilibili.com/");
        return request;
    }

    protected override IEnumerable<MediaSearchResult> ReadResults(JsonElement root)
    {
        foreach (JsonElement video in Items(At(root, "data", "result")))
        {
            if (Text(video, "bvid") is not { Length: > 0 } id)
                continue;

            string url = $"https://www.bilibili.com/video/{Uri.EscapeDataString(id)}";
            yield return new MediaSearchResult(
                id,
                url,
                // Pictures come without a scheme: //i1.hdslb.com/…
                Link("https:" + Text(video, "pic")),
                // The words searched for are marked up in the title: <em class="keyword">…</em>
                WebUtility.HtmlDecode(Regex.Replace(Text(video, "title") ?? url, "<[^>]+>", "")),
                Text(video, "author"),
                Clock(Text(video, "duration")),
                MediaKind.Video,
                Count(video, "mid") is { } uploader ? $"https://space.bilibili.com/{uploader}" : null,
                EmbedLinks.For(Id, id, url),
                Id,
                Count(video, "play"),
                Date(Number(video, "pubdate")));
        }
    }
}

/// <summary>
/// Searched through Niconico's snapshot of its videos. That says who made each one only by number, so the
/// makers' names are looked up afterwards, and remembered.
/// </summary>
public sealed class NiconicoSearchProvider(HttpClient http) : WebApiSearchProvider(http)
{
    private const string UserPage = "https://www.nicovideo.jp/user/";

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> _names = new();

    public override string Id => "niconico";
    public override string DisplayName => "Niconico";
    public override string Icon => "niconico";

    // It can't order by relevance, so that gives the most watched matches too.
    protected override string GetSearchUrl(string query, SearchSort sort) =>
        $"https://snapshot.search.nicovideo.jp/api/v2/snapshot/video/contents/search?q={Uri.EscapeDataString(query)}"
        + "&targets=title,description,tags&fields=contentId,title,lengthSeconds,thumbnailUrl,viewCounter,startTime,userId"
        + $"&_sort=-viewCounter&_limit={ResultCount}&_context=MediaExport";

    protected override IEnumerable<MediaSearchResult> ReadResults(JsonElement root)
    {
        foreach (JsonElement video in Items(At(root, "data")))
        {
            if (Text(video, "contentId") is not { } id)
                continue;

            string url = $"https://www.nicovideo.jp/watch/{Uri.EscapeDataString(id)}";
            yield return new MediaSearchResult(
                id,
                url,
                Link(Text(video, "thumbnailUrl")),
                // Titles arrive encoded twice over (&amp;#039; for an apostrophe).
                WebUtility.HtmlDecode(Text(video, "title")) ?? url,
                null,
                Seconds(video, "lengthSeconds"),
                MediaKind.Video,
                Count(video, "userId") is { } user ? $"{UserPage}{user}" : null,
                EmbedLinks.For(Id, id, url),
                Id,
                Count(video, "viewCounter"),
                Date(Text(video, "startTime")));
        }
    }

    protected override async Task<IReadOnlyList<MediaSearchResult>> CompleteAsync(IReadOnlyList<MediaSearchResult> results, CancellationToken cancellationToken) =>
        await Task.WhenAll(results.Select(async result =>
            result.CreatorUrl?[UserPage.Length..] is { } user ? result with { Creator = await NameAsync(user, cancellationToken) } : result));

    // A name that can't be found just leaves the result without one.
    private async Task<string?> NameAsync(string user, CancellationToken cancellationToken)
    {
        if (_names.TryGetValue(user, out string? known))
            return known;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://nvapi.nicovideo.jp/v1/users/{user}");
            request.Headers.Add("X-Frontend-Id", "6");
            request.Headers.Add("X-Frontend-Version", "0");
            using JsonDocument document = await GetJsonAsync(request, cancellationToken);
            return _names[user] = Text(At(document.RootElement, "data", "user"), "nickname");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return null;
        }
    }
}

public sealed class InternetArchiveSearchProvider(HttpClient http) : WebApiSearchProvider(http)
{
    public override string Id => "internetarchive";
    public override string DisplayName => "Internet Archive";
    public override string Icon => "internet-archive";

    protected override string GetSearchUrl(string query, SearchSort sort) =>
        $"https://archive.org/advancedsearch.php?q={Uri.EscapeDataString($"({query}) AND mediatype:(movies OR audio)")}"
        + $"&fl[]=identifier&fl[]=title&fl[]=creator&fl[]=mediatype&fl[]=downloads&fl[]=date&fl[]=publicdate&rows={ResultCount}&output=json"
        + (sort == SearchSort.Popularity ? "&sort[]=downloads+desc" : "");

    protected override IEnumerable<MediaSearchResult> ReadResults(JsonElement root)
    {
        foreach (JsonElement item in Items(At(root, "response", "docs")))
        {
            if (Text(item, "identifier") is not { } id)
                continue;

            string url = $"https://archive.org/details/{Uri.EscapeDataString(id)}";
            string? creator = FirstText(item, "creator");
            yield return new MediaSearchResult(
                id,
                url,
                new Uri($"https://archive.org/services/img/{Uri.EscapeDataString(id)}"),
                FirstText(item, "title") ?? id,
                creator,
                null,
                Text(item, "mediatype") == "audio" ? MediaKind.Audio : MediaKind.Video,
                creator is null ? null : $"https://archive.org/search?query=creator%3A%22{Uri.EscapeDataString(creator)}%22",
                EmbedLinks.For(Id, id, url),
                Id,
                Count(item, "downloads"),
                // When the thing itself is from, if the item says; otherwise when it was put up.
                Date(Text(item, "date")) ?? Date(Text(item, "publicdate")));
        }
    }
}
