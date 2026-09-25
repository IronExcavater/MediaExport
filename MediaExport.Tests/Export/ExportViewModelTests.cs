// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Export;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediaExport.Tests.Export;

public sealed class ExportViewModelTests
{
    [Test]
    public void ShowSuggestions_reopens_cached_results_when_searching_is_idle()
    {
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()]);
        viewModel.Suggestions.Add(new MediaSearchResult("id", "https://example.test/media", null, "Result", null, null));

        viewModel.ShowSuggestions();

        Assert.That(viewModel.IsSuggestionListOpen, Is.True);
    }

    [Test]
    public void ClearingSearchText_clears_the_selected_result()
    {
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()])
        {
            SearchText = "query"
        };
        var result = new MediaSearchResult("id", "https://example.test/media", null, "Result", null, null);

        viewModel.SelectResultCommand.Execute(result);
        viewModel.SearchText = string.Empty;

        Assert.That(viewModel.SelectedResult, Is.Null);
    }

    [Test]
    public async Task SearchCommand_sets_loading_state_while_searching()
    {
        var provider = new WaitingSearchProvider();
        using var viewModel = new ExportViewModel([provider])
        {
            SearchText = "query"
        };

        Task search = viewModel.SearchCommand.ExecuteAsync(null);

        await provider.SearchStarted.Task;
        Assert.That(viewModel.IsSearching, Is.True);

        provider.Complete();
        await search;

        Assert.That(viewModel.IsSearching, Is.False);
    }

    [Test]
    public async Task SearchCommand_only_searches_the_most_recent_query()
    {
        var provider = new RecordingSearchProvider();
        using var viewModel = new ExportViewModel([provider]);

        viewModel.SearchText = "first";
        Task firstSearch = viewModel.SearchCommand.ExecuteAsync(null);
        viewModel.SearchText = "second";
        Task secondSearch = viewModel.SearchCommand.ExecuteAsync(null);

        await Task.WhenAll(firstSearch, secondSearch);

        Assert.That(provider.Queries, Is.EqualTo(new[] { "second" }));
    }

    private sealed class RecordingSearchProvider : IMediaSearchProvider
    {
        public string Id => "test";
        public string DisplayName => "Test";
        public MediaProviderIcon Icon => MediaProviderIcon.None;
        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<MediaSearchResult>>([]);
        }
    }

    private sealed class WaitingSearchProvider : IMediaSearchProvider
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "test";
        public string DisplayName => "Test";
        public MediaProviderIcon Icon => MediaProviderIcon.None;
        public TaskCompletionSource SearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            SearchStarted.TrySetResult();
            await _completion.Task.WaitAsync(cancellationToken);
            return [];
        }

        public void Complete() => _completion.TrySetResult();
    }
}
