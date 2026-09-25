// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Ytdlp;
using System.Diagnostics;

namespace MediaExport.Tests.Media;

public sealed class YtdlpServiceTests
{
    [Test]
    public void ReadSearchResults_maps_a_ytdlp_search_response()
    {
        const string json = """
            {"entries":[{"id":"abc123","webpage_url":"https://example.test/media","title":"Result","uploader":"Channel","duration":125,"thumbnails":[{"url":"https://example.test/small.jpg"},{"url":"https://example.test/large.jpg"}]}]}
            """;

        IReadOnlyList<YtdlpSearchResult> results = YtdlpService.ReadSearchResults(json);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(results[0].SourceUrl, Is.EqualTo("https://example.test/media"));
            Assert.That(results[0].Creator, Is.EqualTo("Channel"));
            Assert.That(results[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(125)));
            Assert.That(results[0].ThumbnailUri, Is.EqualTo(new Uri("https://example.test/large.jpg")));
        });
    }

    [Test]
    public void SearchAsync_preserves_a_cancelled_search()
    {
        var service = new YtdlpService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Stopwatch stopwatch = Stopwatch.StartNew();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.SearchAsync("ytsearch1:gan", cancellation.Token));

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
    }
}
