// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace MediaExport.Core.Media.Providers;

/// <summary>Every source searched at once, their best results put into one order.</summary>
/// <param name="perSource">How many of each source's results are kept, so one source's don't crowd out the rest.</param>
/// <param name="arrangement">How the results are laid out once they're ranked.</param>
public sealed class AllSearchProvider(
    IReadOnlyList<IMediaSearchProvider> sources,
    int perSource = 5,
    SearchArrangement arrangement = SearchArrangement.AsRanked) : IMediaSearchProvider
{
    public string Id => "all";
    public string DisplayName => "All sources";
    public string Icon => "all";
    public bool SupportsPaging => sources.Any(source => source.SupportsPaging);
    public bool SupportsPopular => sources.Any(source => source.SupportsPopular);

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
        string query,
        SearchSort sort = SearchSort.Relevance,
        CancellationToken cancellationToken = default,
        int limit = 10)
    {
        IReadOnlyList<MediaSearchResult> results = [];
        await foreach (IReadOnlyList<MediaSearchResult> soFar in SearchEachAsync(query, sort, cancellationToken))
            results = soFar;
        return results;
    }

    public Task<SearchResultPage> SearchAsync(
        string query,
        SearchSort sort,
        int offset,
        int take,
        CancellationToken cancellationToken = default) =>
        ReadPageAsync(query, sort, offset, take, popular: false, cancellationToken);

    public Task<SearchResultPage> PopularAsync(int offset, int take, CancellationToken cancellationToken = default) =>
        ReadPageAsync(string.Empty, SearchSort.Popularity, offset, take, popular: true, cancellationToken);

    public IAsyncEnumerable<SearchResultPage> SearchEachPageAsync(
        string query,
        SearchSort sort,
        int offset,
        int take,
        CancellationToken cancellationToken = default) =>
        ReadEachPageAsync(query, sort, offset, take, popular: false, cancellationToken);

    public IAsyncEnumerable<SearchResultPage> PopularEachPageAsync(
        int offset,
        int take,
        CancellationToken cancellationToken = default) =>
        ReadEachPageAsync(string.Empty, SearchSort.Popularity, offset, take, popular: true, cancellationToken);

    private async Task<SearchResultPage> ReadPageAsync(
        string query,
        SearchSort sort,
        int offset,
        int take,
        bool popular,
        CancellationToken cancellationToken)
    {
        SearchResultPage result = new([], false);
        await foreach (SearchResultPage snapshot in ReadEachPageAsync(query, sort, offset, take, popular, cancellationToken))
            result = snapshot;
        return result;
    }

    private async IAsyncEnumerable<SearchResultPage> ReadEachPageAsync(
        string query,
        SearchSort sort,
        int offset,
        int take,
        bool popular,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        offset = Math.Max(0, offset);
        take = Math.Max(1, take);
        if (sources.Count == 0 || (!popular && string.IsNullOrWhiteSpace(query)))
        {
            yield return new SearchResultPage([], false);
            yield break;
        }

        // Every application page gives each source the same stable share. That keeps page-sized APIs aligned:
        // page two advances every source instead of reslicing the larger response used for page one.
        int applicationPage = offset / take;
        int[] eligibleSources = Enumerable.Range(0, sources.Count)
            .Where(index => !popular || sources[index].SupportsPopular)
            .ToArray();
        if (eligibleSources.Length == 0)
        {
            yield return new SearchResultPage([], false);
            yield break;
        }

        int each = take / eligibleSources.Length;
        int extra = take % eligibleSources.Length;
        var searches = new List<Task<(SearchResultPage Page, int Source)>>();
        for (int position = 0; position < eligibleSources.Length; position++)
        {
            int index = eligibleSources[position];
            int sourceTake = each + (position < extra ? 1 : 0);
            if (sourceTake == 0 || (applicationPage > 0 && !sources[index].SupportsPaging))
                continue;
            int sourceOffset = applicationPage * sourceTake;
            searches.Add(ReadSourceAsync(sources[index], index, query, sort, sourceOffset, sourceTake, popular, cancellationToken));
        }

        var answers = new List<(SearchResultPage Page, int Source)>();
        Exception? failure = null;
        int failures = 0;
        int settled = 0;
        if (searches.Count == 0)
        {
            yield return new SearchResultPage([], false);
            yield break;
        }

        await foreach (Task<(SearchResultPage Page, int Source)> search in Task.WhenEach(searches).WithCancellation(cancellationToken))
        {
            settled++;
            if (search.IsFaulted)
            {
                failure ??= search.Exception.GetBaseException();
                failures++;
                continue;
            }

            if (!search.IsCompletedSuccessfully)
                continue;

            answers.Add(search.Result);
            bool isComplete = settled == searches.Count
                && failures == 0
                && answers.All(answer => answer.Page.IsComplete);
            yield return BuildPage(answers, query, sort, take, popular, isComplete);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (answers.Count == 0 && failure is not null)
            ExceptionDispatchInfo.Throw(failure);
    }

    private SearchResultPage BuildPage(
        IReadOnlyList<(SearchResultPage Page, int Source)> answers,
        string query,
        SearchSort sort,
        int take,
        bool popular,
        bool isComplete)
    {
        IReadOnlyList<IReadOnlyList<MediaSearchResult>> orderedSources = answers
            .OrderBy(answer => answer.Source)
            .Select(answer => answer.Page.Results)
            .ToArray();
        IReadOnlyList<MediaSearchResult> combined = popular
            ? Interleave(orderedSources)
            : SearchRanking.Order(orderedSources, query, sort, arrangement);
        bool hasMore = answers.Any(answer => sources[answer.Source].SupportsPaging && answer.Page.HasMore);
        return new SearchResultPage(combined.Take(take).ToArray(), hasMore, isComplete);
    }

    private static async Task<(SearchResultPage Page, int Source)> ReadSourceAsync(
        IMediaSearchProvider source,
        int sourceIndex,
        string query,
        SearchSort sort,
        int offset,
        int take,
        bool popular,
        CancellationToken cancellationToken) =>
        (popular
            ? await source.PopularAsync(offset, take, cancellationToken)
            : await source.SearchAsync(query, sort, offset, take, cancellationToken), sourceIndex);

    private static IReadOnlyList<MediaSearchResult> Interleave(IReadOnlyList<IReadOnlyList<MediaSearchResult>> sourceResults)
    {
        int longest = sourceResults.Count == 0 ? 0 : sourceResults.Max(source => source.Count);
        return Enumerable.Range(0, longest)
            .SelectMany(index => sourceResults.Where(source => index < source.Count).Select(source => source[index]))
            .DistinctBy(result => result.SourceUrl)
            .ToArray();
    }

    /// <summary>
    /// The results so far, in order, each time another source answers; the quick ones don't wait for the slow.
    /// A source that fails is left out, unless none of them answer.
    /// </summary>
    public async IAsyncEnumerable<IReadOnlyList<MediaSearchResult>> SearchEachAsync(
        string query,
        SearchSort sort,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        var answers = new List<IReadOnlyList<MediaSearchResult>>();
        Exception? failure = null;
        Task<IReadOnlyList<MediaSearchResult>>[] searches = sources.Select(source => source.SearchAsync(query, sort, cancellationToken, perSource)).ToArray();
        await foreach (Task<IReadOnlyList<MediaSearchResult>> search in Task.WhenEach(searches).WithCancellation(cancellationToken))
        {
            if (search.IsFaulted)
                failure ??= search.Exception.GetBaseException();
            if (!search.IsCompletedSuccessfully || search.Result.Count == 0)
                continue;

            answers.Add(search.Result.Take(perSource).ToArray());
            yield return SearchRanking.Order(answers, query, sort, arrangement);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (answers.Count == 0 && failure is not null)
            ExceptionDispatchInfo.Throw(failure);
    }
}
