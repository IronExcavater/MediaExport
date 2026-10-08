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
    public void Recent_picks_are_shown_for_the_chosen_source_only()
    {
        var settings = new SettingsStore(null);
        SearchViewModel search = Create(settings, new FakeProvider("youtube"), new FakeProvider("soundcloud"));
        search.Remember(Song);
        search.Remember(Clip);

        search.ShowSuggestions();
        MediaSearchResult?[] onYouTube = search.Suggestions.Select(suggestion => suggestion.Media).ToArray();
        search.SelectedProvider = search.Providers.Single(provider => provider.Id == "soundcloud");
        search.ShowSuggestions();

        Assert.Multiple(() =>
        {
            Assert.That(onYouTube, Is.EqualTo(new[] { Clip }));
            Assert.That(search.Suggestions.Select(suggestion => suggestion.Media), Is.EqualTo(new[] { Song }));
            Assert.That(search.Recents, Is.EqualTo(new[] { Clip, Song }), "the home page shows every source");
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
        public IReadOnlyList<MediaSearchResult> Results { get; init; } = [];
        public Exception? Failure { get; init; }
        public List<string> Queries { get; } = [];
        public TaskCompletionSource Searched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, SearchSort sort = SearchSort.Relevance, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            Searched.TrySetResult();
            return Failure is null ? Task.FromResult(Results) : Task.FromException<IReadOnlyList<MediaSearchResult>>(Failure);
        }
    }
}
