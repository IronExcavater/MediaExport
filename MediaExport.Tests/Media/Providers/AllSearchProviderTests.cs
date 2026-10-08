// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;

namespace MediaExport.Tests.Media.Providers;

public sealed class AllSearchProviderTests
{
    [Test]
    public async Task Results_are_given_as_each_source_answers_and_a_failing_source_is_left_out()
    {
        var quick = new Source("quick", Result("quick", "Song"));
        var slow = new Source("slow", Result("slow", "Song live"));
        var broken = new Source("broken") { Failure = new InvalidOperationException("down") };
        var all = new AllSearchProvider([slow, broken, quick]);
        var seen = new List<string[]>();

        Task reading = Task.Run(async () =>
        {
            await foreach (IReadOnlyList<MediaSearchResult> soFar in all.SearchEachAsync("song", SearchSort.Relevance))
                seen.Add(soFar.Select(result => result.ProviderId!).Order().ToArray());
        });
        broken.Answer();
        quick.Answer();
        await Task.Delay(100);
        string[][] beforeTheSlowOne = seen.ToArray();
        slow.Answer();
        await reading.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(beforeTheSlowOne, Is.EqualTo(new[] { new[] { "quick" } }), "the quick source isn't held up");
            Assert.That(seen.Last(), Is.EqualTo(new[] { "quick", "slow" }));
        });
    }

    [Test]
    public void A_search_fails_only_if_no_source_answers()
    {
        var broken = new Source("broken") { Failure = new InvalidOperationException("down") };
        broken.Answer();

        Assert.ThrowsAsync<InvalidOperationException>(() => new AllSearchProvider([broken]).SearchAsync("song"));
    }

    [Test]
    public void Relevance_weighs_the_words_the_sources_own_order_and_plays()
    {
        MediaSearchResult exact = Result("a", "Blue Song", views: 1_000_000);
        MediaSearchResult partial = Result("b", "Song of another colour", views: 1_000_000);
        MediaSearchResult unplayed = Result("c", "Blue Song", views: 3);

        Assert.Multiple(() =>
        {
            Assert.That(SearchRanking.Relevance(exact, "blue song", 0, 5), Is.GreaterThan(SearchRanking.Relevance(partial, "blue song", 0, 5)), "says every word");
            Assert.That(SearchRanking.Relevance(exact, "blue song", 0, 5), Is.GreaterThan(SearchRanking.Relevance(exact, "blue song", 4, 5)), "placed higher by its source");
            Assert.That(SearchRanking.Relevance(exact, "blue song", 0, 5), Is.GreaterThan(SearchRanking.Relevance(unplayed, "blue song", 0, 5)), "played more");
        });
    }

    [Test]
    public void Sorting_puts_results_without_a_count_or_date_last_and_leaves_one_source_as_it_came()
    {
        MediaSearchResult old = Result("a", "Old", views: 9, published: new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero));
        MediaSearchResult recent = Result("a", "New", views: 2, published: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        MediaSearchResult unknown = Result("b", "Unknown");
        IReadOnlyList<IReadOnlyList<MediaSearchResult>> sources = [[unknown], [recent, old]];

        Assert.Multiple(() =>
        {
            Assert.That(SearchRanking.Order(sources, "x", SearchSort.Popularity), Is.EqualTo(new[] { old, recent, unknown }));
            Assert.That(SearchRanking.Order(sources, "x", SearchSort.Recency), Is.EqualTo(new[] { recent, old, unknown }));
            Assert.That(SearchRanking.Order([[recent, old]], "old", SearchSort.Relevance), Is.EqualTo(new[] { recent, old }));
        });
    }

    private static MediaSearchResult Result(string source, string title, long? views = null, DateTimeOffset? published = null) =>
        new(title, $"https://{source}.test/{Uri.EscapeDataString(title)}", null, title, null, null, ProviderId: source, Views: views, Published: published);

    private sealed class Source(string id, params MediaSearchResult[] results) : IMediaSearchProvider
    {
        private readonly TaskCompletionSource<IReadOnlyList<MediaSearchResult>> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => id;
        public string DisplayName => id;
        public string Icon => id;
        public Exception? Failure { get; init; }

        public void Answer()
        {
            if (Failure is null)
                _answer.SetResult(results);
            else
                _answer.SetException(Failure);
        }

        public Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, SearchSort sort = SearchSort.Relevance, CancellationToken cancellationToken = default) =>
            _answer.Task;
    }
}
