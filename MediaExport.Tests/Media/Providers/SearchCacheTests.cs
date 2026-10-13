// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;

namespace MediaExport.Tests.Media.Providers;

public sealed class SearchCacheTests
{
    private static readonly IReadOnlyList<MediaSearchResult> Found =
    [
        new("id", "https://example.com/id", new Uri("https://example.com/id.jpg"), "Title", "Creator", TimeSpan.FromSeconds(90))
    ];

    [Test]
    public void A_stored_search_is_found_again_whatever_its_case_and_survives_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"search-cache-{Guid.NewGuid():N}.json");
        try
        {
            var cache = new SearchCache(path);
            cache.Store("youtube", "Lofi Beats", 1, Found);
            var restarted = new SearchCache(path);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cache.Find("youtube", "lofi beats", 1), Is.EqualTo(Found));
                Assert.That(restarted.Find("youtube", "lofi beats", 1), Is.EqualTo(Found));
                Assert.That(cache.Find("soundcloud", "lofi beats", 1), Is.Null);
                Assert.That(cache.Find("youtube", "other", 1), Is.Null);
                Assert.That(cache.Find("youtube", "lofi beats", 2), Is.Null);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void The_oldest_first_pages_are_dropped_once_there_are_too_many()
    {
        var cache = new SearchCache(null);
        for (var number = 0; number <= 50; number++)
            cache.Store("youtube", $"query {number}", 1, Found);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cache.Find("youtube", "query 0", 1), Is.Null);
            Assert.That(cache.Find("youtube", "query 50", 1), Is.EqualTo(Found));
        }
    }

    [Test]
    public void Paging_deep_into_a_search_keeps_its_first_page()
    {
        var cache = new SearchCache(null);
        for (var page = 1; page <= 32; page++)
            cache.Store("youtube", "song", page, Found);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cache.Find("youtube", "song", 1), Is.EqualTo(Found));
            Assert.That(cache.Find("youtube", "song", 2), Is.Null);
            Assert.That(cache.Find("youtube", "song", 32), Is.EqualTo(Found));
        }
    }
}
