// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Settings;
using MediaExport.Search;
using MediaExport.Tests.Media;

namespace MediaExport.Tests.Search;

public sealed class SearchViewModelTests
{
    private static readonly MediaSearchResult Song = new("1", "https://soundcloud.com/a/song", null, "Song", "Artist", null, MediaKind.Audio, ProviderId: "soundcloud");
    private static readonly MediaSearchResult Clip = new("2", "https://www.youtube.com/watch?v=2", null, "Song video", "Channel", null, ProviderId: "youtube");

    [Test]
    public void Search_placeholder_is_short_and_does_not_change_with_scope()
    {
        SearchViewModel search = Create(new SettingsStore(null), new FakeProvider("youtube"), new FakeProvider("soundcloud"));

        search.SelectedProvider = search.Providers.Single(provider => provider.Id == "soundcloud");
        search.IsPageSearch = true;

        Assert.That(search.Placeholder, Is.EqualTo("Search or paste a link"));
    }

    [Test]
    public void Typed_terms_leave_the_text_and_the_whole_search_can_be_copied_and_offered_again()
    {
        var settings = new SettingsStore(null);
        SearchViewModel search = Create(settings, new FakeProvider("youtube"), new FakeProvider("soundcloud"));

        search.SearchText = "night song longer:10m source:soundcloud ";
        bool changed = search.ResolveTerms(includeUnfinished: false);
        string box = search.SearchText.Trim(), terms = string.Join(' ', search.Draft.Terms.Select(term => $"{term.DisplayName} {term.ValueName}")), copied = search.FullText;
        search.RememberSearch();
        search.Draft.Cut();
        search.ShowSuggestions();

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(box, Is.EqualTo("night song"), "only the words stay in the box");
            Assert.That(terms, Is.EqualTo("Longer 10m"));
            Assert.That(search.SelectedProvider.Id, Is.EqualTo("soundcloud"), "the source term chose the source");
            Assert.That(copied, Is.EqualTo("night song longer:10m source:soundcloud"));
            Assert.That(search.Suggestions.Select(suggestion => (suggestion.Query, suggestion.IsRecentSearch)), Is.EqualTo(new[] { ("night song longer:10m", true) }));
            Assert.That(settings.Current.RecentSearches, Is.EqualTo(new[] { "night song longer:10m" }));
        });
    }

    [Test]
    public void Search_page_source_terms_raise_every_recognised_source_and_leave_the_draft()
    {
        SearchViewModel search = Create(new SettingsStore(null), new FakeProvider("youtube"), new FakeProvider("soundcloud"));
        search.IsPageSearch = true;
        IReadOnlyList<IMediaSearchProvider>? named = null;
        search.SourcesNamed += (_, sources) => named = sources;
        search.PageSources = () => named ?? [];
        search.SearchText = "night source:youtube source:soundcloud ";

        search.ResolveTerms(includeUnfinished: false);

        Assert.Multiple(() =>
        {
            Assert.That(search.QueryText, Is.EqualTo("night"));
            Assert.That(named?.Select(source => source.Id), Is.EqualTo(new[] { "youtube", "soundcloud" }));
            Assert.That(search.FullText, Is.EqualTo("night source:youtube source:soundcloud"));
        });
    }

    [Test]
    public async Task Search_page_source_resolution_searches_the_selected_source_once()
    {
        var youtube = new FakeProvider("youtube");
        var soundcloud = new FakeProvider("soundcloud");
        SearchViewModel search = Create(new SettingsStore(null), youtube, soundcloud);
        var results = new ResultsViewModel([youtube, soundcloud], search);
        search.IsPageSearch = true;
        await results.SearchAsync("old");
        youtube.Queries.Clear();
        soundcloud.Queries.Clear();
        search.SearchText = "song source:soundcloud ";

        search.ResolveTerms(includeUnfinished: false);
        await Task.Yield();

        Assert.Multiple(() =>
        {
            Assert.That(youtube.Queries, Is.Empty);
            Assert.That(soundcloud.Queries, Is.EqualTo(new[] { "song" }));
        });
    }

    [Test]
    public async Task Removing_the_only_term_clears_stale_ordinary_page_suggestions()
    {
        var provider = new FakeProvider("youtube") { Results = [Clip] };
        SearchViewModel search = Create(new SettingsStore(null), provider);
        SearchTerm term = search.Draft.Add(SearchTermType.Creator, "Channel");
        await provider.Searched.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Yield();
        Assert.That(search.Suggestions.Select(suggestion => suggestion.Media), Is.EqualTo(new[] { Clip }));

        search.Draft.Remove(term);

        Assert.That(search.Suggestions, Is.Empty);
    }

    [Test]
    public async Task Cutting_the_search_resets_search_page_results()
    {
        var provider = new FakeProvider("youtube") { Results = [Clip] };
        SearchViewModel search = Create(new SettingsStore(null), provider);
        var results = new ResultsViewModel([provider], search);
        search.IsPageSearch = true;
        search.SearchText = "song";
        await results.SearchAsync(search.QueryText);
        Assert.That(results.Query, Is.EqualTo("song"));

        search.Draft.Cut();

        Assert.Multiple(() =>
        {
            Assert.That(results.Query, Is.Empty);
            Assert.That(results.Summary, Is.EqualTo("Search for media"));
            Assert.That(results.EmptyTitle, Is.Null);
        });
    }

    [Test]
    public void Restoring_a_recent_search_recreates_text_and_terms()
    {
        SearchViewModel search = Create(new SettingsStore(null), new FakeProvider("youtube"));

        search.Restore("night \"blue moon\" -live creator:\"The Band\"");

        Assert.Multiple(() =>
        {
            Assert.That(search.SearchText, Is.EqualTo("night"));
            Assert.That(search.Draft.Terms.Select(term => (term.Definition.Type, term.Value)), Is.EqualTo(new[]
            {
                (SearchTermType.ExactPhrase, "blue moon"),
                (SearchTermType.ExcludedWord, "live"),
                (SearchTermType.Creator, "The Band")
            }));
        });
    }

    [Test]
    public void Replacing_the_whole_field_removes_old_terms_and_routes_the_copied_provider()
    {
        SearchViewModel search = Create(new SettingsStore(null), new FakeProvider("youtube"), new FakeProvider("soundcloud"));
        search.SearchText = "old";
        search.Draft.Add(SearchTermType.ExactPhrase, "old phrase");

        search.Replace("new -live source:soundcloud");

        Assert.Multiple(() =>
        {
            Assert.That(search.SearchText, Is.EqualTo("new"));
            Assert.That(search.Draft.Terms.Select(term => (term.Definition.Type, term.Value)), Is.EqualTo(new[]
            {
                (SearchTermType.ExcludedWord, "live")
            }));
            Assert.That(search.SelectedProvider.Id, Is.EqualTo("soundcloud"));
        });
    }

    [Test]
    public void Replacing_the_whole_field_with_a_link_on_the_search_page_uses_link_suggestions()
    {
        var provider = new FakeProvider("youtube");
        SearchViewModel search = Create(new SettingsStore(null), provider);
        var results = new ResultsViewModel([provider], search);
        search.Restore("old \"phrase\"");
        search.IsPageSearch = true;
        bool beganLinkSearch = false;
        search.PropertyChanged += (_, args) => beganLinkSearch |= args.PropertyName == nameof(SearchViewModel.IsSearching) && search.IsSearching;

        search.Replace("https://vimeo.com/123");

        Assert.Multiple(() =>
        {
            Assert.That(provider.Queries, Is.Empty, "a link is opened rather than sent to search providers");
            Assert.That(results.Query, Is.Empty, "the page results reset while the link suggestion is prepared");
            Assert.That(beganLinkSearch, Is.True, "the link suggestion path ran even if opening failed immediately");
        });
    }

    [Test]
    public async Task Replacing_a_page_link_with_words_cancels_its_suggestions_and_searches_the_page()
    {
        var provider = new FakeProvider("youtube");
        SearchViewModel search = Create(new SettingsStore(null), provider);
        var results = new ResultsViewModel([provider], search);
        search.IsPageSearch = true;
        search.Replace("https://vimeo.com/123");
        search.Replace("song");

        await provider.Searched.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Yield();

        Assert.Multiple(() =>
        {
            Assert.That(provider.Queries, Is.EqualTo(new[] { "song" }));
            Assert.That(results.Query, Is.EqualTo("song"));
            Assert.That(search.Suggestions, Is.Empty);
            Assert.That(search.IsSuggestionListOpen, Is.False);
            Assert.That(search.IsSearching, Is.False);
        });
    }

    [Test]
    public async Task Starting_page_results_cancels_a_pending_link_suggestion()
    {
        var provider = new FakeProvider("youtube");
        SearchViewModel search = Create(new SettingsStore(null), provider);
        var results = new ResultsViewModel([provider], search);
        search.IsPageSearch = true;
        search.SearchText = "https://vimeo.com/123";
        Task staleLink = search.SearchAsync();
        search.Restore("recent song");

        await results.SearchAsync(search.QueryText);
        await staleLink;

        Assert.Multiple(() =>
        {
            Assert.That(provider.Queries, Is.EqualTo(new[] { "recent song" }));
            Assert.That(results.Query, Is.EqualTo("recent song"));
            Assert.That(search.Suggestions, Is.Empty);
            Assert.That(search.IsSuggestionListOpen, Is.False);
        });
    }

    [Test]
    public async Task Clearing_a_page_link_cancels_its_pending_suggestion()
    {
        var provider = new FakeProvider("youtube");
        SearchViewModel search = Create(new SettingsStore(null), provider);
        var results = new ResultsViewModel([provider], search);
        search.IsPageSearch = true;
        search.SearchText = "https://vimeo.com/123";
        Task staleLink = search.SearchAsync();

        search.Draft.Cut();
        await staleLink;

        Assert.Multiple(() =>
        {
            Assert.That(search.QueryText, Is.Empty);
            Assert.That(results.Query, Is.Empty);
            Assert.That(search.Suggestions, Is.Empty);
            Assert.That(search.IsSuggestionListOpen, Is.False);
        });
    }

    [Test]
    public void Picking_a_recent_again_moves_it_to_the_top_and_saves_the_order()
    {
        var settings = new SettingsStore(null);
        SearchViewModel search = Create(settings, new FakeProvider("youtube"));
        search.Remember(Song);
        search.Remember(Clip);
        int changes = 0;
        search.Recents.CollectionChanged += (_, _) => changes++;

        search.Remember(Song);

        Assert.Multiple(() =>
        {
            Assert.That(search.Recents, Is.EqualTo(new[] { Song, Clip }));
            Assert.That(settings.Current.RecentResults, Is.EqualTo(new[] { Song, Clip }));
            Assert.That(changes, Is.EqualTo(2), "moved, not rebuilt");
        });
    }

    [Test]
    public async Task Focusing_a_box_with_unsearched_text_searches_straight_away()
    {
        var provider = new FakeProvider("youtube") { Results = [Clip] };
        SearchViewModel search = Create(new SettingsStore(null), provider);
        search.SearchText = "song";

        search.ShowSuggestions();
        await provider.Searched.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Yield();

        Assert.Multiple(() =>
        {
            Assert.That(provider.Queries, Is.EqualTo(new[] { "song" }));
            Assert.That(search.Suggestions.Select(suggestion => suggestion.Media), Is.EqualTo(new[] { Clip }));
            Assert.That(search.IsSuggestionListOpen, Is.True);
        });
    }

    [Test]
    public async Task Typing_waits_for_a_pause_and_searches_only_the_latest_text()
    {
        var provider = new FakeProvider("youtube");
        SearchViewModel search = Create(new SettingsStore(null), provider);

        search.SearchText = "fir";
        Task first = search.SearchAsync();
        search.SearchText = "first";
        Task second = search.SearchAsync();
        await Task.WhenAll(first, second);

        Assert.That(provider.Queries, Is.EqualTo(new[] { "first" }));
    }

    [Test]
    public async Task No_results_and_errors_are_said_in_the_list()
    {
        SearchViewModel empty = Create(new SettingsStore(null), new FakeProvider("youtube"));
        empty.SearchText = "nothing";
        await empty.SearchAsync(debounce: false);

        SearchViewModel failing = Create(new SettingsStore(null), new FakeProvider("youtube") { Failure = new InvalidOperationException("Offline") });
        failing.SearchText = "query";
        await failing.SearchAsync(debounce: false);

        Assert.Multiple(() =>
        {
            Assert.That(empty.Suggestions.Single().Message, Is.EqualTo("No results for \"nothing\""));
            Assert.That(empty.Suggestions.Single().Query, Is.EqualTo("nothing"));
            Assert.That(failing.Suggestions.Single().Message, Is.EqualTo("Couldn't search YouTube. Offline"));
        });
    }

    [TestCase("https://vimeo.com/123", true)]
    [TestCase("http://example.test/video", true)]
    [TestCase("lofi hip hop", false)]
    [TestCase("ftp://example.test/file", false)]
    public void Links_are_recognised(string text, bool isLink) =>
        Assert.That(SearchViewModel.IsLink(text), Is.EqualTo(isLink));

    private static SearchViewModel Create(SettingsStore settings, params IMediaSearchProvider[] providers) =>
        new(providers, YtdlpServiceTests.CreateService(), settings);

    private sealed class FakeProvider(string id) : IMediaSearchProvider
    {
        public string Id => id;
        public string DisplayName => id == "youtube" ? "YouTube" : "SoundCloud";
        public string Icon => id;
        public bool SupportsPopular => true;
        public IReadOnlyList<MediaSearchResult> Results { get; init; } = [];
        public Exception? Failure { get; init; }
        public List<string> Queries { get; } = [];
        public TaskCompletionSource Searched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, SearchSort sort = SearchSort.Relevance, CancellationToken cancellationToken = default, int limit = 10)
        {
            Queries.Add(query);
            Searched.TrySetResult();
            return Failure is null ? Task.FromResult(Results) : Task.FromException<IReadOnlyList<MediaSearchResult>>(Failure);
        }

        public Task<SearchResultPage> PopularAsync(int offset, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SearchResultPage(Results.Skip(offset).Take(take).ToArray(), Results.Count > offset + take));
    }
}
