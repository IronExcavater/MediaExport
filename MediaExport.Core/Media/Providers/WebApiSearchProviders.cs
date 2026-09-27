// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace MediaExport.Core.Media.Providers;

/// <summary>Sources yt-dlp can download from but not search, searched through their public web APIs instead.</summary>
public abstract class WebApiSearchProvider(HttpClient http) : IMediaSearchProvider
{
    protected const int ResultCount = 10;

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }

    protected abstract string GetSearchUrl(string query);

    protected abstract IEnumerable<MediaSearchResult> ReadResults(JsonElement root);

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        using JsonDocument document = await http.GetFromJsonAsync<JsonDocument>(GetSearchUrl(query.Trim()), cancellationToken)
            ?? throw new InvalidOperationException("The search returned nothing.");
        return ReadResults(document.RootElement).Take(ResultCount).ToArray();
    }

    protected static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // Some fields come as a list when there are several (e.g. an Internet Archive item's creators).
    protected static string? FirstText(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0
            ? value[0].GetString()
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
