// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Media.Ytdlp;
using MediaExport.Core.Settings;
using MediaExport.Search;
using MediaExport.Tests.Media;

namespace MediaExport.Tests.Search;

public sealed class ResultsViewModelTests
{
    [Test]
    public void Everywhere_reasserts_its_active_state_when_every_source_is_already_picked()
    {
        var provider = new FakeProvider("youtube");
        var results = new ResultsViewModel([provider, new FakeProvider("soundcloud")], CreateSearch(provider));
        int changes = 0;
        results.PropertyChanged += (_, args) => changes += args.PropertyName == nameof(results.AreAllPicked) ? 1 : 0;

        results.PickAllCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(results.AreAllPicked, Is.True);
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Empty_states_are_short_titles()
    {
        var provider = new FakeProvider("youtube");
        SearchViewModel search = CreateSearch(provider);
        var results = new ResultsViewModel([provider, new FakeProvider("soundcloud")], search);

        Assert.Multiple(() =>
        {
            Assert.That(results.Summary, Is.EqualTo("Search for media"));
            Assert.That(results.EmptyTitle, Is.Null);
        });

        await results.SearchAsync("nothing");
        Assert.That(results.EmptyTitle, Is.EqualTo("No matches for “nothing”"));

        results.Sources[0].IsPicked = false;
        results.Sources.Single(source => source.IsPicked).IsPicked = false;
        await results.SearchAsync("nothing");
        Assert.That(results.EmptyTitle, Is.EqualTo("Pick a source to keep searching"));
    }

    [Test]
    public async Task Failure_is_a_concise_empty_title()
    {
        var provider = new FakeProvider("youtube") { Failure = new InvalidOperationException("Offline") };
        var results = new ResultsViewModel([provider], CreateSearch(provider));

        await results.SearchAsync("query");

        Assert.That(results.EmptyTitle, Is.EqualTo("Search hit a snag. Offline"));
    }

    [Test]
    public async Task Skeletons_are_shown_only_while_the_first_results_are_loading()
    {
        var provider = new PausedProvider("youtube");
        var results = new ResultsViewModel([provider], CreateSearch(provider));

        Task search = results.SearchAsync("song");
        await provider.Searched.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(results.IsShowingSkeletons, Is.True);

        provider.Complete([Result(1, "youtube")]);
        await search;

        Assert.Multiple(() =>
        {
            Assert.That(results.Results, Has.Count.EqualTo(1));
            Assert.That(results.IsShowingSkeletons, Is.False);
        });
    }

    [Test]
    public async Task Entering_a_link_cancels_and_clears_a_pending_page_search()
    {
        var provider = new PausedProvider("youtube");
        SearchViewModel search = CreateSearch(provider);
        search.IsPageSearch = true;
        var results = new ResultsViewModel([provider], search);

        Task pending = results.SearchAsync("song");
        await provider.Searched.Task.WaitAsync(TimeSpan.FromSeconds(1));

        search.SearchText = "https://youtube.test/watch?v=1";
        provider.Complete([Result(1, "youtube")]);
        await pending;

        Assert.Multiple(() =>
        {
            Assert.That(results.Query, Is.Empty);
            Assert.That(results.Results, Is.Empty);
            Assert.That(results.IsShowingSkeletons, Is.False);
        });
    }

    [Test]
    public async Task Cancelled_search_cannot_replace_a_newer_empty_title()
    {
        var provider = new FakeProvider("youtube");
        var results = new ResultsViewModel([provider], CreateSearch(provider));

        Task oldSearch = results.SearchAsync("old", afterPause: true);
        Task newSearch = results.SearchAsync("new");
        await Task.WhenAll(oldSearch, newSearch);

        Assert.Multiple(() =>
        {
            Assert.That(results.Query, Is.EqualTo("new"));
            Assert.That(results.EmptyTitle, Is.EqualTo("No matches for “new”"));
            Assert.That(results.IsSearching, Is.False);
        });
    }

    [Test]
    public async Task Opening_the_next_page_fetches_more_results_from_the_provider()
    {
        var provider = new FakeProvider("youtube")
        {
            Results = Enumerable.Range(1, 30).Select(index => Result(index, "youtube")).ToArray()
        };
        var results = new ResultsViewModel([provider], CreateSearch(provider));

        await results.SearchAsync("song");
        string[] firstPage = results.Results.Select(result => result.Identity).ToArray();
        results.NextPageCommand.Execute(null);
        for (int attempt = 0; provider.Queries.Count < 2 && attempt < 100; attempt++)
            await Task.Delay(10);
        string[] secondPage = results.Results.Select(result => result.Identity).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(firstPage, Has.Length.EqualTo(24));
            Assert.That(secondPage, Has.Length.EqualTo(6));
            Assert.That(firstPage.Intersect(secondPage), Is.Empty);
            Assert.That(provider.Queries, Is.EqualTo(new[] { "song", "song" }), "page two is a new provider request");
            Assert.That(provider.PageRequests, Is.EqualTo(new[] { (0, 24), (24, 24) }));
        });
    }

    [Test]
    public async Task Completing_a_page_notifies_the_ui_that_another_page_is_available()
    {
        var provider = new FakeProvider("youtube")
        {
            Results = Enumerable.Range(1, 30).Select(index => Result(index, "youtube")).ToArray()
        };
        var results = new ResultsViewModel([provider], CreateSearch(provider));
        bool notifiedAsPageable = false;
        results.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(results.HasPages) && results.HasPages)
                notifiedAsPageable = true;
        };

        await results.SearchAsync("song");

        Assert.Multiple(() =>
        {
            Assert.That(notifiedAsPageable, Is.True);
            Assert.That(results.NextPageCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public async Task A_remote_duplicate_is_not_repeated_on_a_later_page()
    {
        var provider = new DuplicatePageProvider();
        var results = new ResultsViewModel([provider], CreateSearch(provider));

        await results.SearchAsync("song");
        await results.NextPageCommand.ExecuteAsync(null);

        Assert.That(results.Results.Select(result => result.Identity), Is.EqualTo(new[] { "25" }));
    }

    [Test]
    public async Task Returning_to_a_completed_page_does_not_search_the_provider_again()
    {
        var provider = new FakeProvider("youtube")
        {
            Results = Enumerable.Range(1, 30).Select(index => Result(index, "youtube")).ToArray()
        };
        var results = new ResultsViewModel([provider], CreateSearch(provider));

        await results.SearchAsync("song");
        await results.NextPageCommand.ExecuteAsync(null);
        await results.PreviousPageCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(results.Page, Is.EqualTo(1));
            Assert.That(results.Results.Select(result => result.Identity), Is.EqualTo(Enumerable.Range(1, 24).Select(index => index.ToString())));
            Assert.That(provider.PageRequests, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Clearing_all_sources_clears_the_previous_pager_session()
    {
        var provider = new FakeProvider("youtube")
        {
            Results = Enumerable.Range(1, 30).Select(index => Result(index, "youtube")).ToArray()
        };
        var other = new FakeProvider("soundcloud")
        {
            Results = Enumerable.Range(101, 30).Select(index => Result(index, "soundcloud")).ToArray()
        };
        var results = new ResultsViewModel([provider, other], CreateSearch(provider, other));

        await results.SearchAsync("song");
        Assert.That(results.HasPages, Is.True);

        results.Sources[0].IsPicked = false;
        results.Sources.Single(source => source.IsPicked).IsPicked = false;
        for (int attempt = 0; results.EmptyTitle != "Pick a source to keep searching" && attempt < 100; attempt++)
            await Task.Delay(10);

        Assert.Multiple(() =>
        {
            Assert.That(results.HasPages, Is.False);
            Assert.That(results.CanGoForward, Is.False);
            Assert.That(results.PageChoices, Is.Empty);
        });
    }

    [Test]
    public async Task A_partial_page_is_visible_but_retried_instead_of_cached()
    {
        var quick = new FakeProvider("youtube") { Results = [Result(1, "youtube")] };
        var broken = new FakeProvider("soundcloud") { Failure = new InvalidOperationException("Offline") };
        var results = new ResultsViewModel([quick, broken], CreateSearch(quick, broken));

        await results.SearchAsync("song");
        Assert.That(results.Results.Select(result => result.Identity), Is.EqualTo(new[] { "1" }));

        await results.SearchAsync("song");

        Assert.Multiple(() =>
        {
            Assert.That(quick.PageRequests, Has.Count.EqualTo(2), "an incomplete page is retried");
            Assert.That(broken.PageRequests, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Page_results_are_shown_as_each_provider_answers()
    {
        var quick = new FakeProvider("youtube") { Results = [Result(1, "youtube")] };
        var slow = new PausedProvider("soundcloud");
        var results = new ResultsViewModel([quick, slow], CreateSearch(quick, slow));

        Task search = results.SearchAsync("song");
        await slow.Searched.Task.WaitAsync(TimeSpan.FromSeconds(1));
        for (int attempt = 0; results.Results.Count == 0 && attempt < 100; attempt++)
            await Task.Delay(10);

        Assert.That(results.Results.Select(result => result.Identity), Is.EqualTo(new[] { "1" }), "the quick source is visible while another source is pending");

        slow.Complete([Result(2, "soundcloud")]);
        await search;
        Assert.That(results.Results.Select(result => result.Identity), Is.EquivalentTo(new[] { "1", "2" }));
    }

    [Test]
    public async Task A_non_pageable_provider_is_not_repeated_on_page_two()
    {
        var pageable = new FakeProvider("youtube")
        {
            Results = Enumerable.Range(1, 30).Select(index => Result(index, "youtube")).ToArray()
        };
        var firstPageOnly = new FakeProvider("youtubemusic")
        {
            SupportsPaging = false,
            Results = Enumerable.Range(101, 30).Select(index => Result(index, "youtubemusic")).ToArray()
        };
        var results = new ResultsViewModel([pageable, firstPageOnly], CreateSearch(pageable, firstPageOnly));

        await results.SearchAsync("song");
        await results.NextPageCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(pageable.PageRequests, Has.Count.EqualTo(2));
            Assert.That(firstPageOnly.PageRequests, Is.EqualTo(new[] { (0, 12) }), "page two must not fetch the same first remote page again");
        });
    }

    [Test]
    public async Task Default_results_use_the_providers_popular_feed_without_a_hidden_query()
    {
        var provider = new FakeProvider("youtube") { PopularResults = [Result(1, "youtube")] };
        var results = new ResultsViewModel([provider], CreateSearch(provider));

        await results.LoadPopularAsync();
        await results.LoadPopularAsync();

        Assert.Multiple(() =>
        {
            Assert.That(results.Query, Is.Empty);
            Assert.That(results.Summary, Is.EqualTo("Search for media"));
            Assert.That(results.Results, Has.Count.EqualTo(1));
            Assert.That(provider.Queries, Is.Empty, "the default feed never invents a query such as technology");
            Assert.That(provider.PopularRequests, Is.EqualTo(new[] { (0, 24) }), "the completed popular page is cached");
        });
    }

    [Test]
    public async Task Unsupported_popular_feeds_do_not_mask_failure_of_every_supported_feed()
    {
        var unsupported = new FakeProvider("youtube") { SupportsPopular = false };
        var broken = new FakeProvider("soundcloud") { PopularFailure = new InvalidOperationException("Offline") };
        var results = new ResultsViewModel([unsupported, broken], CreateSearch(unsupported, broken));

        await results.LoadPopularAsync();

        Assert.Multiple(() =>
        {
            Assert.That(results.EmptyTitle, Is.EqualTo("Popular picks are taking a break. Offline"));
            Assert.That(unsupported.PopularRequests, Is.Empty);
            Assert.That(broken.PopularRequests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task A_cancelled_partial_result_is_not_cached_as_a_complete_search()
    {
        var quick = new FakeProvider("youtube") { Results = [Result(1, "youtube")] };
        var slow = new CancellableProvider("soundcloud", Result(2, "soundcloud"));
        var results = new ResultsViewModel([quick, slow], CreateSearch(quick, slow));

        Task interrupted = results.SearchAsync("song");
        await quick.Searched.Task.WaitAsync(TimeSpan.FromSeconds(1));
        for (int attempt = 0; results.Results.Count == 0 && attempt < 100; attempt++)
            await Task.Delay(1);
        await results.SearchAsync(string.Empty);
        await interrupted;
        await results.SearchAsync("song");

        Assert.Multiple(() =>
        {
            Assert.That(quick.Queries, Has.Count.EqualTo(2));
            Assert.That(slow.Calls, Is.EqualTo(2));
            Assert.That(results.Results.Select(result => result.Identity), Is.EquivalentTo(new[] { "1", "2" }));
        });
    }

    [Test]
    public async Task Changing_sort_during_typing_keeps_the_new_query()
    {
        var provider = new FakeProvider("youtube") { Results = [Result(1, "youtube")] };
        SearchViewModel search = CreateSearch(provider);
        search.IsPageSearch = true;
        var results = new ResultsViewModel([provider], search);
        await results.SearchAsync("old");

        search.SearchText = "new";
        Task pending = results.SearchAsync(search.QueryText, afterPause: true);
        search.Sort = SearchSort.Popularity;
        await pending;
        for (int attempt = 0; results.IsSearching && attempt < 100; attempt++)
            await Task.Delay(10);

        Assert.Multiple(() =>
        {
            Assert.That(results.Query, Is.EqualTo("new"));
            Assert.That(provider.Queries.Last(), Is.EqualTo("new"));
        });
    }

    [Test]
    public async Task Changing_sort_keeps_submitted_link_results()
    {
        var linked = Result(1, "youtube");
        var provider = new FakeProvider("youtube");
        var search = new SearchViewModel(
            [provider],
            new SettingsStore(null),
            (_, _) => Task.FromResult(new OpenedLink([linked], null)))
        {
            IsPageSearch = true
        };
        var results = new ResultsViewModel([provider], search);
        const string link = "https://youtube.test/watch?v=1";

        search.SearchText = link;
        await results.SearchAsync(link);
        search.Sort = SearchSort.Popularity;

        Assert.Multiple(() =>
        {
            Assert.That(results.Query, Is.EqualTo(link));
            Assert.That(results.Results, Is.EqualTo(new[] { linked }));
        });
    }

    private static MediaSearchResult Result(int index, string provider) =>
        new(index.ToString(), $"https://{provider}.test/{index}", null, $"Song {index}", null, null, ProviderId: provider);

    private static SearchViewModel CreateSearch(params IMediaSearchProvider[] providers) =>
        new(providers, YtdlpServiceTests.CreateService(), new SettingsStore(null));

    private sealed class FakeProvider(string id) : IMediaSearchProvider
    {
        public string Id => id;
        public string DisplayName => id == "youtube" ? "YouTube" : "SoundCloud";
        public string Icon => id;
        public bool SupportsPaging { get; init; } = true;
        public bool SupportsPopular { get; init; } = true;
        public Exception? Failure { get; init; }
        public Exception? PopularFailure { get; init; }
        public IReadOnlyList<MediaSearchResult> Results { get; init; } = [];
        public IReadOnlyList<MediaSearchResult> PopularResults { get; init; } = [];
        public List<string> Queries { get; } = [];
        public List<(int Offset, int Take)> PageRequests { get; } = [];
        public List<(int Offset, int Take)> PopularRequests { get; } = [];
        public TaskCompletionSource Searched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, SearchSort sort = SearchSort.Relevance, CancellationToken cancellationToken = default, int limit = 10)
        {
            Queries.Add(query);
            Searched.TrySetResult();
            return Failure is null ? Task.FromResult(Results) : Task.FromException<IReadOnlyList<MediaSearchResult>>(Failure);
        }

        public Task<SearchResultPage> SearchAsync(string query, SearchSort sort, int offset, int take, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            PageRequests.Add((offset, take));
            Searched.TrySetResult();
            return Failure is null
                ? Task.FromResult(Page(Results, offset, take))
                : Task.FromException<SearchResultPage>(Failure);
        }

        public Task<SearchResultPage> PopularAsync(int offset, int take, CancellationToken cancellationToken = default)
        {
            PopularRequests.Add((offset, take));
            return PopularFailure is null
                ? Task.FromResult(Page(PopularResults, offset, take))
                : Task.FromException<SearchResultPage>(PopularFailure);
        }

        private static SearchResultPage Page(IReadOnlyList<MediaSearchResult> results, int offset, int take) =>
            new(results.Skip(offset).Take(take).ToArray(), results.Count > offset + take);
    }

    private sealed class CancellableProvider(string id, MediaSearchResult result) : IMediaSearchProvider
    {
        public string Id => id;
        public string DisplayName => "SoundCloud";
        public string Icon => id;
        public int Calls { get; private set; }

        public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, SearchSort sort = SearchSort.Relevance, CancellationToken cancellationToken = default, int limit = 10)
        {
            Calls++;
            if (Calls == 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [result];
        }
    }

    private sealed class PausedProvider(string id) : IMediaSearchProvider
    {
        private readonly TaskCompletionSource<IReadOnlyList<MediaSearchResult>> _results = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => id;
        public string DisplayName => "YouTube";
        public string Icon => id;
        public TaskCompletionSource Searched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(IReadOnlyList<MediaSearchResult> results) => _results.SetResult(results);

        public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, SearchSort sort = SearchSort.Relevance, CancellationToken cancellationToken = default, int limit = 10)
        {
            Searched.TrySetResult();
            return await _results.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class DuplicatePageProvider : IMediaSearchProvider
    {
        public string Id => "youtube";
        public string DisplayName => "YouTube";
        public string Icon => "youtube";
        public bool SupportsPaging => true;

        public Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, SearchSort sort = SearchSort.Relevance, CancellationToken cancellationToken = default, int limit = 10) =>
            Task.FromResult<IReadOnlyList<MediaSearchResult>>([]);

        public Task<SearchResultPage> SearchAsync(string query, SearchSort sort, int offset, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult(offset == 0
                ? new SearchResultPage(Enumerable.Range(1, 24).Select(index => Result(index, Id)).ToArray(), true)
                : new SearchResultPage([Result(1, Id), Result(25, Id)], false));
    }
}
