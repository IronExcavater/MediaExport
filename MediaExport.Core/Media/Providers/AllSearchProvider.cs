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

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
        string query,
        SearchSort sort = SearchSort.Relevance,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MediaSearchResult> results = [];
        await foreach (IReadOnlyList<MediaSearchResult> soFar in SearchEachAsync(query, sort, cancellationToken))
            results = soFar;
        return results;
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
        Task<IReadOnlyList<MediaSearchResult>>[] searches = sources.Select(source => source.SearchAsync(query, sort, cancellationToken)).ToArray();
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
