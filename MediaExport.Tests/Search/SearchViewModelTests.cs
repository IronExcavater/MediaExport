// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Search;

namespace MediaExport.Tests.Search;

public class SearchViewModelTests
{
    private static readonly MediaSearchResult Song = new("1", "https://example.test/1", null, "Song", "Artist", null);

    [Test]
    public async Task Search_WhenProviderAndTermsAreSelected_UsesThemAndShowsResults()
    {
        // Arrange
        var first = new FakeProvider("first");
        var second = new FakeProvider("second") { Results = [Song] };
        var search = new SearchViewModel([first, second], new SearchCache(null));
        search.SelectProviders([second]);
        search.Replace("sort:views exact:\"lofi beats\" exclude:live study");

        // Act
        await search.SearchAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(search.Results, Is.EqualTo(new[] { Song }));
            Assert.That(second.Queries, Is.EqualTo(new[] { "study \"lofi beats\" -\"live\"" }));
            Assert.That(second.Sorts, Is.EqualTo(new[] { SearchSort.Views }));
            Assert.That(first.Queries, Is.Empty);
            Assert.That(search.Title, Is.EqualTo("study"));
            Assert.That(search.IsSearching, Is.False);
        });
    }

    [Test]
    public async Task NextPage_WhenFollowingPageIsEmpty_StaysOnLastAvailablePage()
    {
        // Arrange
        var provider = new FakeProvider("first") { Results = [Song], TotalCount = 40 };
        var search = new SearchViewModel([provider], new SearchCache(null)) { Query = "song" };

        // Act
        await search.SearchAsync();
        await search.NextPageCommand.ExecuteAsync(null);
        await search.NextPageCommand.ExecuteAsync(null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(search.Page, Is.EqualTo(2));
            Assert.That(search.Results, Has.Count.EqualTo(20));
            Assert.That(search.HasNextPage, Is.False);
        });
    }

    [Test]
    public async Task CommitSearch_WhenRepeatedAndPinned_KeepsUniqueRecentOrder()
    {
        // Arrange
        var search = new SearchViewModel([new FakeProvider("first")], new SearchCache(null)) { Query = "one" };
        await search.CommitSearchAsync();
        search.Query = "two";
        await search.CommitSearchAsync();

        // Act
        search.RecentSearches.TogglePin(search.RecentSearches[1]);
        search.Query = "one";
        await search.CommitSearchAsync();
        search.Query = "three";
        await search.CommitSearchAsync();

        // Assert
        Assert.That(
            search.RecentSearches.Select(recent => (recent.Words, recent.IsPinned)),
            Is.EqualTo(new[] { ("one", true), ("three", false), ("two", false) }));
    }

    [Test]
    public async Task Search_WhenProviderFails_ShowsErrorAndStopsLoading()
    {
        // Arrange
        var provider = new FakeProvider("first") { Failure = new InvalidOperationException("Offline") };
        var search = new SearchViewModel([provider], new SearchCache(null)) { Query = "song" };

        // Act
        await search.SearchAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(search.Message, Is.EqualTo("Couldn't search first. Offline"));
            Assert.That(search.EmptyStateMessage, Is.EqualTo("Couldn't search first. Offline"));
            Assert.That(search.IsSearching, Is.False);
        });
    }

    [Test]
    public async Task Search_WhenNewRequestStarts_CancelsOldAndKeepsLatestResult()
    {
        // Arrange
        var provider = new FakeProvider("first")
        {
            Pause = new TaskCompletionSource<IReadOnlyList<MediaSearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var search = new SearchViewModel([provider], new SearchCache(null)) { Query = "old" };
        var oldSearch = search.SearchAsync();

        // Act
        provider.Pause = null;
        provider.Results = [Song];
        search.Query = "new";
        await search.SearchAsync();
        await oldSearch;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(provider.Cancelled, Is.EqualTo(new[] { "old" }));
            Assert.That(search.Results, Is.EqualTo(new[] { Song }));
            Assert.That(search.Message, Is.Null);
        });
    }

    private sealed class FakeProvider(string id) : IMediaSearchProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public string Icon => "YouTubeIcon";
        public MediaKind Kind => MediaKind.Video;
        public IReadOnlyList<MediaSearchResult> Results { get; set; } = [];
        public int? TotalCount { get; init; }
        public Exception? Failure { get; init; }
        public TaskCompletionSource<IReadOnlyList<MediaSearchResult>>? Pause { get; set; }
        public List<string> Queries { get; } = [];
        public List<SearchSort> Sorts { get; } = [];
        public List<string> Cancelled { get; } = [];

        public LinkKind GetLinkKind(Uri link) => LinkKind.None;
        public Task<MediaSearchResult?> FindByLinkAsync(Uri link, CancellationToken cancellationToken = default) =>
            Task.FromResult<MediaSearchResult?>(null);
        public Task<IReadOnlyList<MediaSearchResult>> ListByLinkAsync(Uri link, int skip = 0, int limit = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results);
        public Task<string?> FindCreatorAsync(Uri link, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
            string query,
            int skip = 0,
            int limit = 10,
            SearchSort sort = SearchSort.Relevance,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            Sorts.Add(sort);
            if (Failure is not null)
                throw Failure;
            if (Pause is { } pause)
            {
                try
                {
                    return await pause.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Cancelled.Add(query);
                    throw;
                }
            }

            if (TotalCount is null)
                return Results;

            var count = Math.Min(limit, Math.Max(0, TotalCount.Value - skip));
            return Enumerable.Repeat(Results[0], count).ToList();
        }
    }
}
