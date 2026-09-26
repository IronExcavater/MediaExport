// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Settings;
using MediaExport.Export;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediaExport.Tests.Export;

public sealed class ExportViewModelTests
{
    private static readonly MediaSearchResult Video = new("id", "https://example.test/media", null, "Result", null, null);
    private static readonly MediaSearchResult Track = Video with { SourceUrl = "https://example.test/track", Kind = MediaKind.Audio };

    [Test]
    public void ShowSuggestions_reopens_cached_results_when_searching_is_idle()
    {
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], new FakeDownloader())
        {
            SearchText = "query"
        };
        viewModel.Suggestions.Add(new SearchSuggestion(Video, false));

        viewModel.ShowSuggestions();

        Assert.That(viewModel.IsSuggestionListOpen, Is.True);
    }

    [Test]
    public void ClearingSearchText_clears_the_selected_result()
    {
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], new FakeDownloader())
        {
            SearchText = "query"
        };

        viewModel.SelectResultCommand.Execute(Video);
        viewModel.SearchText = string.Empty;

        Assert.That(viewModel.SelectedResult, Is.Null);
    }

    [Test]
    public async Task SearchCommand_sets_loading_state_while_searching()
    {
        var provider = new WaitingSearchProvider();
        using var viewModel = new ExportViewModel([provider], new FakeDownloader())
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
        using var viewModel = new ExportViewModel([provider], new FakeDownloader());

        viewModel.SearchText = "first";
        Task firstSearch = viewModel.SearchCommand.ExecuteAsync(null);
        viewModel.SearchText = "second";
        Task secondSearch = viewModel.SearchCommand.ExecuteAsync(null);

        await Task.WhenAll(firstSearch, secondSearch);

        Assert.That(provider.Queries, Is.EqualTo(new[] { "second" }));
    }

    [Test]
    public void Selecting_audio_defaults_to_an_audio_export_without_a_video_option()
    {
        var downloader = new FakeDownloader { Inspection = new MediaInspection(true, null, false, null) };
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], downloader);

        viewModel.SelectResultCommand.Execute(Track);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsAudioExport, Is.True);
            Assert.That(viewModel.CanExportVideo, Is.False);
        });
    }

    [Test]
    public void Protected_media_is_reported_and_cannot_be_exported()
    {
        var downloader = new FakeDownloader { Inspection = MediaInspection.Unavailable("It's protected by DRM.") };
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], downloader);

        viewModel.SelectResultCommand.Execute(Track);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.UnavailableReason, Is.EqualTo("It's protected by DRM."));
            Assert.That(viewModel.ExportCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void Qualities_taller_than_the_media_become_unavailable()
    {
        var downloader = new FakeDownloader { Inspection = new MediaInspection(true, null, true, 720) };
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], downloader);

        viewModel.SelectResultCommand.Execute(Video);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Qualities.Where(quality => !quality.IsAvailable).Select(quality => quality.MaxHeight),
                Is.EqualTo(new int?[] { 2160, 1440, 1080 }));
            Assert.That(viewModel.SelectedQuality.MaxHeight, Is.EqualTo(720));
        });
    }

    [Test]
    public void Media_shorter_than_every_preset_falls_back_to_best_available()
    {
        var downloader = new FakeDownloader { Inspection = new MediaInspection(true, null, true, 240) };
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], downloader);

        viewModel.SelectResultCommand.Execute(Video);

        Assert.That(viewModel.SelectedQuality, Is.SameAs(viewModel.Qualities[0]));
    }

    [Test]
    public async Task ExportCommand_exports_the_selected_media_with_the_chosen_options()
    {
        var downloader = new FakeDownloader();
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], downloader);
        viewModel.SelectResultCommand.Execute(Video);
        viewModel.SelectedQuality = viewModel.Qualities[4];
        viewModel.TrimStart = "0:30";
        viewModel.DownloadSubtitles = true;
        viewModel.SubtitleLanguages = "en, fr";

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(downloader.Options, Is.EqualTo(new ExportOptions(MediaKind.Video, "mp4", 720, viewModel.OutputFolder)
            {
                TrimStart = TimeSpan.FromSeconds(30),
                SubtitleLanguages = "en,fr"
            }));
            Assert.That(viewModel.SelectedJob?.IsCompleted, Is.True);
            Assert.That(viewModel.SelectedJob?.OutputPath, Is.EqualTo(FakeDownloader.OutputPath));
            Assert.That(viewModel.ActiveExportCount, Is.Zero);
        });
    }

    [Test]
    public void An_invalid_trim_blocks_the_export()
    {
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], new FakeDownloader());
        viewModel.SelectResultCommand.Execute(Video);

        viewModel.TrimStart = "1:30";
        viewModel.TrimEnd = "0:45";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.TrimError, Is.Not.Null);
            Assert.That(viewModel.ExportCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void Picked_results_come_back_as_recent_suggestions_that_match_the_search()
    {
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], new FakeDownloader());
        viewModel.SelectResultCommand.Execute(Video);
        viewModel.SelectResultCommand.Execute(Track with { Title = "Another" });
        viewModel.SearchText = string.Empty;

        viewModel.ShowSuggestions();
        SearchSuggestion[] onFocus = viewModel.Suggestions.ToArray();
        viewModel.SearchText = "anoth";
        viewModel.SearchCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(onFocus.Select(suggestion => suggestion.Media.Title), Is.EqualTo(new[] { "Another", "Result" }));
            Assert.That(onFocus.All(suggestion => suggestion.IsRecent), Is.True);
            Assert.That(viewModel.Suggestions.Select(suggestion => suggestion.Media.Title), Is.EqualTo(new[] { "Another" }));
        });
    }

    [Test]
    public async Task A_finished_export_turns_the_button_into_its_result_until_the_options_change()
    {
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], new FakeDownloader());
        viewModel.SelectResultCommand.Execute(Video);

        await viewModel.ExportCommand.ExecuteAsync(null);
        bool doneAfterExport = viewModel.IsExportDone;
        viewModel.SelectedVideoFormat = viewModel.VideoFormats[1];

        Assert.Multiple(() =>
        {
            Assert.That(doneAfterExport, Is.True);
            Assert.That(viewModel.IsExportDone, Is.False);
            Assert.That(viewModel.ExportCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public async Task A_failed_export_offers_to_try_again_and_explains_why()
    {
        var downloader = new FakeDownloader { Failure = new InvalidOperationException("No formats found") };
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], downloader);
        viewModel.SelectResultCommand.Execute(Video);

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ExportLabel, Is.EqualTo("Try again"));
            Assert.That(viewModel.HintKind, Is.EqualTo(ExportHintKind.Error));
            Assert.That(viewModel.Hint, Is.EqualTo("No formats found"));
        });
    }

    [Test]
    public void Export_choices_are_remembered_for_next_time()
    {
        var settings = new SettingsStore(null);
        using (var viewModel = new ExportViewModel([new RecordingSearchProvider()], new FakeDownloader(), settings))
            viewModel.SelectedAudioFormat = viewModel.AudioFormats[2];

        using var next = new ExportViewModel([new RecordingSearchProvider()], new FakeDownloader(), settings);

        Assert.That(next.SelectedAudioFormat.Extension, Is.EqualTo("opus"));
    }

    [Test]
    public void ClearFinished_keeps_running_exports()
    {
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], new FakeDownloader());
        var running = new ExportJob(Video, new ExportOptions(MediaKind.Video, "mp4", null, "out"));
        viewModel.Jobs.Add(running);

        viewModel.ClearFinishedCommand.Execute(null);

        Assert.That(viewModel.Jobs, Is.EqualTo(new[] { running }));
    }

    [Test]
    public async Task A_failed_export_keeps_the_error_on_the_job()
    {
        var downloader = new FakeDownloader { Failure = new InvalidOperationException("No formats found") };
        using var viewModel = new ExportViewModel([new RecordingSearchProvider()], downloader);
        viewModel.SelectResultCommand.Execute(Video);

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SelectedJob?.IsFailed, Is.True);
            Assert.That(viewModel.SelectedJob?.Status, Is.EqualTo("No formats found"));
        });
    }

    private sealed class FakeDownloader : IMediaDownloader
    {
        public const string OutputPath = @"C:\Exports\Result.mp4";

        public ExportOptions? Options { get; private set; }
        public Exception? Failure { get; init; }
        public MediaInspection Inspection { get; init; } = new(true, null, true, 2160);

        public Task<MediaInspection> InspectAsync(string sourceUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(Inspection);

        public Task<string> DownloadAsync(string sourceUrl, ExportOptions options, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Options = options;
            return Failure is null ? Task.FromResult(OutputPath) : Task.FromException<string>(Failure);
        }
    }

    private sealed class RecordingSearchProvider : IMediaSearchProvider
    {
        public string Id => "test";
        public string DisplayName => "Test";
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
