// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Tests.Media;

public sealed class YtdlpServiceTests
{
    [Test]
    public void ReadSearchResults_maps_a_ytdlp_search_response()
    {
        const string json = """
            {"entries":[{"id":"abc123","ie_key":"Youtube","title":"Result","uploader":"Channel","duration":125,"thumbnails":[{"url":"https://example.test/small.jpg"},{"url":"https://example.test/large.jpg"}]}]}
            """;

        IReadOnlyList<MediaSearchResult> results = YtdlpService.ReadSearchResults(json);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(results[0].SourceUrl, Is.EqualTo("https://www.youtube.com/watch?v=abc123"));
            Assert.That(results[0].Creator, Is.EqualTo("Channel"));
            Assert.That(results[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(125)));
            Assert.That(results[0].ThumbnailUri, Is.EqualTo(new Uri("https://example.test/large.jpg")));
        });
    }

    [Test]
    public void ReadSearchResults_builds_a_watch_url_for_a_flat_youtube_result()
    {
        const string json = """
            {"entries":[{"id":"abc123","ie_key":"Youtube","url":"https://www.youtube.com/channel/example","title":"Result"}]}
            """;

        Assert.That(YtdlpService.ReadSearchResults(json).Single().SourceUrl, Is.EqualTo("https://www.youtube.com/watch?v=abc123"));
    }

    [Test]
    public void ReadSearchResults_skips_entries_without_a_usable_source_url()
    {
        const string json = """
            {"entries":[{"id":"abc123","title":"Missing URL"}]}
            """;

        Assert.That(YtdlpService.ReadSearchResults(json), Is.Empty);
    }
}
