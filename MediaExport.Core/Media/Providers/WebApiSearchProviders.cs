// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MediaExport.Core.Media.Providers;

/// <summary>Sources yt-dlp can download from but not search, searched through their public web APIs instead.</summary>
public abstract class WebApiSearchProvider(HttpClient http) : IMediaSearchProvider
{
    protected const int ResultCount = 10;

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }

    protected HttpClient Http { get; } = http;

    protected abstract string GetSearchUrl(string query);

    /// <summary>The search request; sources that want more than a link (a token, say) add it here.</summary>
    protected virtual Task<HttpRequestMessage> CreateRequestAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpRequestMessage(HttpMethod.Get, GetSearchUrl(query)));

    protected abstract IEnumerable<MediaSearchResult> ReadResults(JsonElement root);

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        using HttpRequestMessage request = await CreateRequestAsync(query.Trim(), cancellationToken);
        using JsonDocument document = await GetJsonAsync(request, cancellationToken);
        return ReadResults(document.RootElement).Take(ResultCount).ToArray();
    }

    protected async Task<JsonDocument> GetJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    // Uploaders' text sometimes arrives HTML-encoded (&#34; for a quote).
    protected static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? WebUtility.HtmlDecode(value.GetString()) : null;

    // Some fields come as a list when there are several (e.g. an Internet Archive item's creators).
    protected static string? FirstText(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0
            ? WebUtility.HtmlDecode(value[0].GetString())
            : Text(element, name);
}

public sealed class DailymotionSearchProvider(HttpClient http) : WebApiSearchProvider(http)
{
    public override string Id => "dailymotion";
    public override string DisplayName => "Dailymotion";
    public override string Icon => "dailymotion";

    protected override string GetSearchUrl(string query) =>
        $"https://api.dailymotion.com/videos?search={Uri.EscapeDataString(query)}&limit={ResultCount}"
        + "&fields=id,title,duration,thumbnail_480_url,owner.screenname,owner.url,url";

    protected override IEnumerable<MediaSearchResult> ReadResults(JsonElement root)
    {
        if (!root.TryGetProperty("list", out JsonElement list))
            yield break;

        foreach (JsonElement video in list.EnumerateArray())
        {
            if (Text(video, "id") is not { } id || Text(video, "url") is not { } url)
                continue;

            yield return new MediaSearchResult(
                id,
                url,
                Uri.TryCreate(Text(video, "thumbnail_480_url"), UriKind.Absolute, out Uri? thumbnail) ? thumbnail : null,
                Text(video, "title") ?? url,
                Text(video, "owner.screenname"),
                video.TryGetProperty("duration", out JsonElement seconds) && seconds.TryGetDouble(out double length) ? TimeSpan.FromSeconds(length) : null,
                MediaKind.Video,
                Text(video, "owner.url"),
                EmbedLinks.For(Id, id, url),
                Id);
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

    protected override string GetSearchUrl(string query) =>
        $"https://api.vimeo.com/videos?query={Uri.EscapeDataString(query)}&per_page={ResultCount}"
        + "&fields=uri,name,link,duration,pictures.sizes.link,pictures.sizes.width,user.name,user.link";

    protected override async Task<HttpRequestMessage> CreateRequestAsync(string query, CancellationToken cancellationToken)
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

        HttpRequestMessage request = await base.CreateRequestAsync(query, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("jwt", _token);
        return request;
    }

    protected override IEnumerable<MediaSearchResult> ReadResults(JsonElement root)
    {
        if (!root.TryGetProperty("data", out JsonElement data))
            yield break;

        foreach (JsonElement video in data.EnumerateArray())
        {
            // Its id is the end of its uri: /videos/1084537.
            if (Text(video, "uri")?.Split('/')[^1] is not { Length: > 0 } id || Text(video, "link") is not { } url)
                continue;

            bool hasUser = video.TryGetProperty("user", out JsonElement user) && user.ValueKind == JsonValueKind.Object;
            yield return new MediaSearchResult(
                id,
                url,
                ReadThumbnail(video),
                Text(video, "name") ?? url,
                hasUser ? Text(user, "name") : null,
                video.TryGetProperty("duration", out JsonElement seconds) && seconds.TryGetDouble(out double length) ? TimeSpan.FromSeconds(length) : null,
                MediaKind.Video,
                hasUser ? Text(user, "link") : null,
                EmbedLinks.For(Id, id, url),
                Id);
        }
    }

    // Sizes run from small to large: the first one wide enough for a result's picture, or else the largest.
    private static Uri? ReadThumbnail(JsonElement video)
    {
        if (!video.TryGetProperty("pictures", out JsonElement pictures) || pictures.ValueKind != JsonValueKind.Object
            || !pictures.TryGetProperty("sizes", out JsonElement sizes) || sizes.GetArrayLength() == 0)
            return null;

        JsonElement size = sizes.EnumerateArray().FirstOrDefault(size => size.TryGetProperty("width", out JsonElement width) && width.GetInt32() >= 480);
        if (size.ValueKind != JsonValueKind.Object)
            size = sizes[sizes.GetArrayLength() - 1];
        return Uri.TryCreate(Text(size, "link"), UriKind.Absolute, out Uri? thumbnail) ? thumbnail : null;
    }
}

public sealed class InternetArchiveSearchProvider(HttpClient http) : WebApiSearchProvider(http)
{
    public override string Id => "internetarchive";
    public override string DisplayName => "Internet Archive";
    public override string Icon => "internet-archive";

    protected override string GetSearchUrl(string query) =>
        $"https://archive.org/advancedsearch.php?q={Uri.EscapeDataString($"({query}) AND mediatype:(movies OR audio)")}"
        + $"&fl[]=identifier&fl[]=title&fl[]=creator&fl[]=mediatype&rows={ResultCount}&output=json";

    protected override IEnumerable<MediaSearchResult> ReadResults(JsonElement root)
    {
        if (!root.TryGetProperty("response", out JsonElement response) || !response.TryGetProperty("docs", out JsonElement docs))
            yield break;

        foreach (JsonElement item in docs.EnumerateArray())
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
                Id);
        }
    }
}
