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
    public async Task Searching_asks_the_chosen_source_and_shows_what_it_found()
    {
        var first = new FakeProvider("first");
        var second = new FakeProvider("second") { Results = [Song] };
        var search = new SearchViewModel([first, second], new SearchCache(null)) { Query = "  song  " };
        search.SelectProviders([second]);

        await search.SearchAsync();

        Assert.Multiple(() =>
        {
            Assert.That(search.Results, Is.EqualTo(new[] { Song }));
            Assert.That(second.Queries, Is.EqualTo(new[] { "song" }), "asked without the spaces around it");
            Assert.That(first.Queries, Is.Empty);
            Assert.That(search.IsSearching, Is.False);
            Assert.That(search.Message, Is.Null);
        });
    }

    [Test]
    public void The_first_source_is_chosen_to_begin_with()
    {
        var first = new FakeProvider("first");

        Assert.Multiple(() =>
        {
            Assert.That(new SearchViewModel([first, new FakeProvider("second")], new SearchCache(null)).SelectedProviders, Is.EqualTo(new[] { first }));
            Assert.That(new SearchViewModel([], new SearchCache(null)).SelectedProviders, Is.Empty);
        });
    }

    [Test]
    public void Replacing_and_clearing_the_query_normalizes_the_editable_text()
    {
        var search = new SearchViewModel([new FakeProvider("first")], new SearchCache(null));

        search.Replace("  night song  ");
        var copied = search.FullText;
        search.Replace("");

        Assert.Multiple(() =>
        {
            Assert.That(copied, Is.EqualTo("source:first night song"), "the provider is copied as a term");
            Assert.That(search.Query, Is.Empty);
            Assert.That(search.FullText, Is.EqualTo("source:first"));
        });
    }

    [Test]
    public async Task Next_stops_at_the_last_page_when_the_page_after_it_is_empty()
    {
        var provider = new FakeProvider("first") { Results = [Song], TotalCount = 40 };
        var search = new SearchViewModel([provider], new SearchCache(null)) { Query = "song" };

        await search.SearchAsync();
        await search.NextPageCommand.ExecuteAsync(null);
        var couldGoOnFromTheSecondPage = search.HasNextPage;
        await search.NextPageCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(couldGoOnFromTheSecondPage, Is.True, "a full page may have another after it");
            Assert.That(search.Page, Is.EqualTo(2), "the third page was empty, so the second is shown again");
            Assert.That(search.Results, Has.Count.EqualTo(20));
            Assert.That(search.HasNextPage, Is.False);
        });
    }

    [Test]
    public void Ends_of_a_range_written_by_themselves_become_one_range_term()
    {
        var search = new SearchViewModel([new FakeProvider("first")], new SearchCache(null));

        search.ResolveTerms("longer:3m shorter:10m after:2024 ");

        Assert.That(search.Terms, Is.EqualTo(new[] { new SearchTerm("duration", "3m..10m"), new SearchTerm("published", "2024..") }));
    }

    [Test]
    public async Task Only_a_search_that_is_committed_to_becomes_a_recent_search()
    {
        var search = new SearchViewModel([new FakeProvider("first")], new SearchCache(null));

        search.Query = "typed";
        await search.SearchAsync();
        search.Query = "one";
        await search.CommitSearchAsync();
        search.Query = "two";
        await search.CommitSearchAsync();
        search.Query = "One";
        await search.CommitSearchAsync();
        search.Query = "  ";
        await search.CommitSearchAsync();

        var recents = search.RecentSearches.Select(recent => recent.FullText);
        Assert.Multiple(() =>
        {
            Assert.That(recents, Is.EqualTo(new[] { "source:first One", "source:first two" }), "newest first, and one done again isn't listed twice");
            Assert.That(search.RecentSearches[0].Words, Is.EqualTo("One"));
            Assert.That(search.RecentSearches[0].ProviderIcons, Is.EqualTo(new[] { "YouTubeIcon" }));
        });
    }

    [Test]
    public async Task Several_sources_are_written_in_one_term_and_their_results_are_mixed()
    {
        var one = Song with { Identity = "one" };
        var two = Song with { Identity = "two" };
        var first = new FakeProvider("first") { Results = [one, one] };
        var second = new FakeProvider("second") { Results = [two, two] };
        var third = new FakeProvider("third");
        var search = new SearchViewModel([first, second, third], new SearchCache(null)) { Query = "song" };

        search.SelectProviders([first, second]);
        var written = search.FullText;
        search.Replace("source:second,third song");
        var chosenFromText = search.SelectedProviders.ToList();
        search.Replace(written);
        await search.SearchAsync();
        search.SelectProviders([second]);
        search.SelectProviders([]);

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.EqualTo("source:first,second song"));
            Assert.That(chosenFromText, Is.EqualTo(new[] { second, third }));
            Assert.That(search.Results, Is.EqualTo(new[] { one, two, one, two }), "dealt out in turn");
            Assert.That(search.SelectedProviders, Is.EqualTo(new[] { second }), "the last source can't be dropped");
        });
    }

    [Test]
    public async Task The_ranking_is_written_as_a_term_and_passed_to_the_source()
    {
        var provider = new FakeProvider("first") { Results = [Song] };
        var search = new SearchViewModel([provider], new SearchCache(null));

        search.Replace("sort:views song");
        await search.SearchAsync();

        Assert.Multiple(() =>
        {
            Assert.That(search.Sort, Is.EqualTo(SearchSort.Views));
            Assert.That(search.Query, Is.EqualTo("song"));
            Assert.That(search.FullText, Is.EqualTo("source:first sort:views song"));
            Assert.That(provider.Sorts, Is.EqualTo(new[] { SearchSort.Views }));
        });
    }

    [Test]
    public void A_search_has_one_author_and_a_new_one_takes_its_place()
    {
        var search = new SearchViewModel([new FakeProvider("first")], new SearchCache(null));

        search.ResolveTerms("author:first exact:one author:second ");

        Assert.That(search.Terms, Is.EqualTo(new[] { new SearchTerm("exact", "one"), new SearchTerm("author", "second") }));
    }

    [Test]
    public async Task Terms_written_in_the_text_become_terms_and_narrow_the_search()
    {
        var provider = new FakeProvider("first");
        var search = new SearchViewModel([provider], new SearchCache(null));

        var hadTerms = search.ResolveTerms("study exact:\"lofi beats\" exclude:live ");
        var hadNone = search.ResolveTerms("plain words ");
        await search.SearchAsync();

        Assert.Multiple(() =>
        {
            Assert.That(hadTerms, Is.True);
            Assert.That(hadNone, Is.False, "text without terms is left for the user to keep typing");
            Assert.That(search.Query, Is.EqualTo("study"));
            Assert.That(search.Terms, Is.EqualTo(new[] { new SearchTerm("exact", "lofi beats"), new SearchTerm("exclude", "live") }));
            Assert.That(search.FullText, Is.EqualTo("source:first exact:\"lofi beats\" exclude:live study"));
            Assert.That(provider.Queries, Is.EqualTo(new[] { "study \"lofi beats\" -\"live\"" }));
        });
    }

    [Test]
    public void A_pasted_search_chooses_its_provider_and_keeps_only_the_words()
    {
        var first = new FakeProvider("first");
        var second = new FakeProvider("second");
        var search = new SearchViewModel([first, second], new SearchCache(null));

        search.Replace("source:second night song source:nobody");

        Assert.Multiple(() =>
        {
            Assert.That(search.SelectedProviders, Is.EqualTo(new[] { second }));
            Assert.That(search.Query, Is.EqualTo("night song source:nobody"), "a provider that doesn't exist is left as words");
        });
    }

    [Test]
    public async Task A_page_number_goes_no_further_than_one_past_the_pages_seen()
    {
        var provider = new FakeProvider("first") { Results = Enumerable.Repeat(Song, 20).ToList() };
        var search = new SearchViewModel([provider], new SearchCache(null)) { Query = "song" };
        await search.SearchAsync();

        await search.GoToPageAsync(9);
        var afterFirstJump = search.Page;
        await search.GoToPageAsync(0);

        Assert.Multiple(() =>
        {
            Assert.That(afterFirstJump, Is.EqualTo(2));
            Assert.That(search.Page, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task A_link_to_a_list_is_searched_and_one_to_a_creator_becomes_an_author_term()
    {
        var lists = new FakeProvider("lists") { LinkKind = LinkKind.List, Results = [Song] };
        var search = new SearchViewModel([lists], new SearchCache(null)) { Query = "https://site.test/playlist" };
        await search.SearchAsync();
        var listed = search.Results.ToList();

        var creators = new FakeProvider("creators") { LinkKind = LinkKind.Creator, Creator = "Lofi Girl" };
        var byCreator = new SearchViewModel([creators], new SearchCache(null));
        var problem = await byCreator.SearchCreatorAsync(new Uri("https://site.test/@lofigirl"));

        Assert.Multiple(() =>
        {
            Assert.That(listed, Is.EqualTo(new[] { Song }));
            Assert.That(lists.Queries, Is.EqualTo(new[] { "list /playlist from 0" }));
            Assert.That(search.Title, Is.EqualTo("Playlist"));
            Assert.That(problem, Is.Null);
            Assert.That(byCreator.Terms, Is.EqualTo(new[] { new SearchTerm("author", "Lofi Girl") }));
            Assert.That(byCreator.Query, Is.Empty);
        });
    }

    [Test]
    public async Task A_pinned_search_stays_first_and_is_not_counted_or_repeated()
    {
        var search = new SearchViewModel([new FakeProvider("first")], new SearchCache(null)) { Query = "one" };
        await search.CommitSearchAsync();
        search.Query = "two";
        await search.CommitSearchAsync();

        search.RecentSearches.TogglePin(search.RecentSearches[1]);
        search.Query = "one";
        await search.CommitSearchAsync();
        search.Query = "three";
        await search.CommitSearchAsync();

        var listed = search.RecentSearches.Select(recent => (recent.Words, recent.IsPinned)).ToList();
        Assert.That(listed, Is.EqualTo(new[] { ("one", true), ("three", false), ("two", false) }));
    }

    [Test]
    public async Task The_title_says_what_the_results_are()
    {
        var search = new SearchViewModel([new FakeProvider("first") { Results = [Song] }], new SearchCache(null));
        search.Replace("author:someone");
        await search.SearchAsync();
        var byAuthor = search.Title;

        search.Replace("sort:views exact:\"lofi beats\" exclude:live study");
        await search.SearchAsync();

        Assert.Multiple(() =>
        {
            Assert.That(byAuthor, Is.EqualTo("Popular by someone"));
            Assert.That(search.Title, Is.EqualTo("Most viewed for study \"lofi beats\", doesn't have \"live\""));
            Assert.That(search.Summary, Is.EqualTo("Results 1 to 1"));
        });
    }

    [Test]
    public async Task Nothing_to_search_for_asks_the_sources_for_what_is_popular()
    {
        var provider = new FakeProvider("first") { Results = [Song] };
        var search = new SearchViewModel([provider], new SearchCache(null)) { Query = "song" };
        await search.SearchAsync();

        search.Query = "   ";
        await search.SearchAsync();
        var popularTitle = search.Title;
        provider.Results = [];
        search.SelectProviders([]);
        await new SearchViewModel([new FakeProvider("empty")], new SearchCache(null)).SearchAsync();

        Assert.Multiple(() =>
        {
            Assert.That(provider.Queries, Is.EqualTo(new[] { "song", "" }), "an empty query is the request for what is popular");
            Assert.That(search.Results, Is.EqualTo(new[] { Song }));
            Assert.That(popularTitle, Is.EqualTo("Popular"));
            Assert.That(search.Message, Is.Null);
        });
    }

    [Test]
    public async Task A_search_that_finds_nothing_or_fails_says_so()
    {
        var empty = new SearchViewModel([new FakeProvider("first")], new SearchCache(null)) { Query = "song" };
        var broken = new SearchViewModel([new FakeProvider("first") { Failure = new InvalidOperationException("Offline") }], new SearchCache(null)) { Query = "song" };
        var sourceless = new SearchViewModel([], new SearchCache(null)) { Query = "song" };

        await empty.SearchAsync();
        await broken.SearchAsync();
        await sourceless.SearchAsync();

        Assert.Multiple(() =>
        {
            Assert.That(empty.Message, Is.EqualTo("No results on first. Try different words."));
            Assert.That(broken.Message, Is.EqualTo("Couldn't search first. Offline"));
            Assert.That(broken.IsSearching, Is.False);
            Assert.That(sourceless.Message, Is.EqualTo("There are no sources to search."));
        });
    }

    [Test]
    public async Task A_newer_search_replaces_one_still_running()
    {
        var provider = new FakeProvider("first") { Pause = new TaskCompletionSource<IReadOnlyList<MediaSearchResult>>() };
        var search = new SearchViewModel([provider], new SearchCache(null)) { Query = "old" };

        var old = search.SearchAsync();
        provider.Pause = null;
        provider.Results = [Song];
        search.Query = "new";
        await search.SearchAsync();
        await old;

        Assert.Multiple(() =>
        {
            Assert.That(provider.Cancelled, Is.EqualTo(new[] { "old" }), "the old one was told to stop");
            Assert.That(search.Results, Is.EqualTo(new[] { Song }));
            Assert.That(search.Message, Is.Null);
            Assert.That(search.IsSearching, Is.False);
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

        public LinkKind LinkKind { get; init; }
        public string? Creator { get; init; }

        public LinkKind GetLinkKind(Uri link) => LinkKind;

        public Task<MediaSearchResult?> FindByLinkAsync(Uri link, CancellationToken cancellationToken = default) =>
            Task.FromResult<MediaSearchResult?>(null);

        public Task<IReadOnlyList<MediaSearchResult>> ListByLinkAsync(
            Uri link,
            int skip = 0,
            int limit = 10,
            CancellationToken cancellationToken = default)
        {
            Queries.Add($"list {link.AbsolutePath} from {skip}");
            return Task.FromResult(Results);
        }

        public Task<string?> FindCreatorAsync(Uri link, CancellationToken cancellationToken = default) =>
            Task.FromResult(Creator);

        public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, int skip = 0, int limit = 10, SearchSort sort = SearchSort.Relevance, CancellationToken cancellationToken = default)
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

            // Like a real source, it has TotalCount results in all and gives the ones asked for.
            var left = Math.Max(0, TotalCount.Value - skip);
            var count = Math.Min(limit, left);
            return Enumerable.Repeat(Results[0], count).ToList();
        }
    }
}
