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
    public void Store_WhenCacheIsReopenedAndFilled_PersistsRecentEntries()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), $"search-cache-{Guid.NewGuid():N}.json");

        try
        {
            var cache = new SearchCache(path);
            cache.Store("youtube", "Lofi Beats", 1, Found);
            var reopened = new SearchCache(path);
            for (var number = 0; number <= 50; number++)
                cache.Store("youtube", $"query {number}", 1, Found);

            // Act
            var persisted = reopened.Find("youtube", "lofi beats", 1);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(persisted, Is.EqualTo(Found));
                Assert.That(cache.Find("soundcloud", "lofi beats", 1), Is.Null);
                Assert.That(cache.Find("youtube", "query 0", 1), Is.Null);
                Assert.That(cache.Find("youtube", "query 50", 1), Is.EqualTo(Found));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }
}
