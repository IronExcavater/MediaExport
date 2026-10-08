// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using ManuHub.Ytdlp.NET;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Tooling;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SubtitleTrack = MediaExport.Core.Media.Models.SubtitleTrack;
using YtdlpClient = ManuHub.Ytdlp.NET.Ytdlp;

namespace MediaExport.Core.Media.Ytdlp;

public sealed partial class YtdlpService(ToolchainService tools) : IMediaDownloader
{
    private const string OutputMarker = "__MEDIA_EXPORT_FILE__:";
    private const string DrmProblem = "It's protected by DRM, so it can't be exported.";
    private const string SponsorCategories = "sponsor,selfpromo,interaction";
    private const string SignInProblem = "The site wants you signed in for this one. Choose a browser you're signed in with under Settings, then try again.";

    public async Task<IReadOnlyList<YtdlpSearchResult>> SearchAsync(string searchTarget, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTarget))
            return [];

        cancellationToken.ThrowIfCancellationRequested();
        return ReadSearchResults(await ReadMetadataAsync(searchTarget, "The search returned nothing.", cancellationToken));
    }

    /// <summary>Reads a link someone pasted: what it is, and what can be exported from it.</summary>
    public async Task<(MediaSearchResult Media, MediaDetails Details)> OpenLinkAsync(string url, CancellationToken cancellationToken = default)
    {
        using JsonDocument document = JsonDocument.Parse(await ReadMetadataAsync(url, "That link couldn't be opened.", cancellationToken));
        JsonElement root = document.RootElement;
        if (GetString(root, "_type") == "playlist")
            throw new InvalidOperationException("That's a playlist. Open one of its videos or tracks instead.");

        MediaDetails details = ReadDetails(root);
        string identity = GetString(root, "id") ?? url;
        // A link read by another address is still kept as it was given.
        string sourceUrl = Reachable(url) != url ? url : GetSourceUrl(root) ?? url;
        string providerId = (GetString(root, "extractor_key") ?? "web").ToLowerInvariant() switch { "archiveorg" => "internetarchive", var key => key };
        var media = new MediaSearchResult(
            identity,
            sourceUrl,
            GetThumbnailUri(root),
            GetString(root, "title") ?? sourceUrl,
            GetString(root, "uploader") ?? GetString(root, "channel"),
            GetDuration(root),
            details.HasVideo || !details.IsExportable ? MediaKind.Video : MediaKind.Audio,
            GetString(root, "channel_url") ?? GetString(root, "uploader_url"),
            EmbedLinks.For(providerId, identity, sourceUrl),
            providerId);

        return (media, details);
    }

    public async Task<MediaDetails> InspectAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        string json;
        try
        {
            json = await ReadMetadataAsync(sourceUrl, "This media couldn't be read.", cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return MediaDetails.Unavailable(exception.Message);
        }

        using JsonDocument document = JsonDocument.Parse(json);
        return ReadDetails(document.RootElement);
    }

    // What yt-dlp knows about a link or search, as JSON; when it has nothing, why (or else what went wrong, in general).
    private async Task<string> ReadMetadataAsync(string target, string nothing, CancellationToken cancellationToken)
    {
        var errors = new ErrorLog();
        string? json = await CreateClient(errors).GetMetadataRawAsync(Reachable(target), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return string.IsNullOrWhiteSpace(json) ? throw new InvalidOperationException(errors.Last ?? nothing) : json;
    }

    public async Task<IReadOnlyList<string>> DownloadAsync(
        string sourceUrl,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await DownloadOnceAsync(sourceUrl, options, progress, cancellationToken);
        }
        catch (InvalidOperationException exception) when (options.SignInOnlyWhenNeeded && exception.Message == SignInProblem)
        {
            progress?.Report(new ExportProgress(0, null, null, false, "Signing in with your browser"));
            return await DownloadOnceAsync(sourceUrl, options with { SignInOnlyWhenNeeded = false }, progress, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<string>> DownloadOnceAsync(
        string sourceUrl,
        ExportOptions options,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputFolder);

        var errors = new ErrorLog();
        YtdlpClient download = CreateClient(errors)
            .WithOutputFolder(options.OutputFolder)
            .WithOutputTemplate(GetOutputTemplate(options))
            .WithWindowsFilenames();

        foreach ((string name, string? value) in GetExportArguments(options))
            download = value is null ? download.AddFlag(name) : download.AddOption(name, value);

        var outputs = new List<string>();
        int clips = options.Clips.Count;
        if (clips > 0)
            progress?.Report(new ExportProgress(0, null, null, false, ClipStep(1, clips)));

        // Clips are cut by FFmpeg, which gives no percentages; they count up as each one finishes instead.
        void OnProgress(object? sender, DownloadProgressEventArgs args)
        {
            if (clips == 0)
            {
                progress?.Report(new ExportProgress(
                    Math.Clamp(args.Percent, 0, 100),
                    string.IsNullOrWhiteSpace(args.Speed) ? null : args.Speed,
                    string.IsNullOrWhiteSpace(args.ETA) ? null : args.ETA,
                    false));
            }
        }

        void OnPostProcessing(object? sender, string message)
        {
            if (clips == 0)
                progress?.Report(new ExportProgress(100, null, null, true));
        }

        void OnOutput(object? sender, string line)
        {
            int marker = line.IndexOf(OutputMarker, StringComparison.Ordinal);
            if (marker < 0)
                return;

            outputs.Add(line[(marker + OutputMarker.Length)..].Trim());
            if (clips > 0 && outputs.Count < clips)
                progress?.Report(new ExportProgress(outputs.Count * 100d / clips, null, null, false, ClipStep(outputs.Count + 1, clips)));
        }

        download.ProgressDownload += OnProgress;
        download.PostProcessingStarted += OnPostProcessing;
        download.OutputMessage += OnOutput;
        try
        {
            await download.DownloadAsync(Reachable(sourceUrl), cancellationToken);
        }
        finally
        {
            download.ProgressDownload -= OnProgress;
            download.PostProcessingStarted -= OnPostProcessing;
            download.OutputMessage -= OnOutput;
        }

        cancellationToken.ThrowIfCancellationRequested();

        string[] files = outputs.Where(File.Exists).Select(Path.GetFullPath).Distinct().ToArray();
        if (files.Length == 0)
            throw new InvalidOperationException(errors.Last ?? "The export finished without producing a file.");

        if (clips == 0)
            return files;

        // Parts are named by where they start, so none overwrites another; they're put in order, then joined or numbered.
        string[] parts = files.OrderBy(file => ClipStart(file)).ToArray();
        if (parts.Length == 1)
            return [MoveAside(parts[0], ClipSuffix().Replace(parts[0], string.Empty))];

        if (options.JoinClips)
        {
            progress?.Report(new ExportProgress(100, null, null, true, "Joining the clips"));
            return [await JoinAsync(parts, cancellationToken)];
        }

        return parts.Select((part, index) => MoveAside(part, ClipSuffix().Replace(part, $" (clip {index + 1})"))).ToArray();
    }

    internal static string GetOutputTemplate(ExportOptions options) =>
        options.Clips.Count > 0 ? $"{options.FileNameTemplate} [%(section_start)s].%(ext)s" : $"{options.FileNameTemplate}.%(ext)s";

    private static string ClipStep(int number, int count) => count == 1 ? "Cutting the clip" : $"Cutting clip {number} of {count}";

    private static double ClipStart(string file) =>
        ClipSuffix().Match(file) is { Success: true } match
        && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double start) ? start : 0;

    // A part's final name, without replacing a file that's already there.
    private static string MoveAside(string from, string to)
    {
        string target = to;
        for (int copy = 2; File.Exists(target) && !string.Equals(target, from, StringComparison.OrdinalIgnoreCase); copy++)
            target = Path.Combine(Path.GetDirectoryName(to)!, $"{Path.GetFileNameWithoutExtension(to)} ({copy}){Path.GetExtension(to)}");
        File.Move(from, target);
        return target;
    }

    [GeneratedRegex(@" \[([\d.]+)\](?=\.[^.\\]+$)")]
    private static partial Regex ClipSuffix();

    // yt-dlp reads a Vimeo video's page only when signed in, but its player without.
    internal static string Reachable(string url) => VimeoPage().Replace(url, "https://player.vimeo.com/video/$1");

    [GeneratedRegex(@"^https?://(?:www\.)?vimeo\.com/(\d+)/?(?:[?#].*)?$")]
    private static partial Regex VimeoPage();

    internal static IReadOnlyList<(string Name, string? Value)> GetExportArguments(ExportOptions options)
    {
        var arguments = new List<(string, string?)>
        {
            ("--no-playlist", null),
            ("--encoding", "utf-8"), // Otherwise non-ANSI characters are dropped from the printed file path.
            ("--progress", null), // --print implies --quiet, which would hide progress.
            ("--print", $"after_move:{OutputMarker}%(filepath)s"),
            ("--concurrent-fragments", "4")
        };

        if (options.EmbedMetadata)
            arguments.Add(("--embed-metadata", null));

        if (options.CookiesFromBrowser is { Length: > 0 } browser && !options.SignInOnlyWhenNeeded)
            arguments.Add(("--cookies-from-browser", browser));

        if (options.RemoveSponsors)
            arguments.Add(("--sponsorblock-remove", SponsorCategories));

        foreach (Clip clip in options.Clips)
            arguments.Add(("--download-sections", $"*{Seconds(clip.Start)}-{Seconds(clip.End)}"));
        if (options.Clips.Count > 0)
            arguments.Add(("--force-keyframes-at-cuts", null)); // Exact cuts rather than the nearest keyframe.

        if (options.IsAudioOnly)
        {
            arguments.Add(("-f", "ba/b"));
            arguments.Add(("-x", null));
            arguments.Add(("--audio-format", options.Format));
            arguments.Add(("--audio-quality", options.AudioBitrate is int bitrate ? $"{bitrate}K" : "0"));
            if (options.EmbedThumbnail && options.Format is "mp3" or "m4a" or "opus" or "flac")
                arguments.Add(("--embed-thumbnail", null));
            return arguments;
        }

        if (options.SubtitleLanguages.Count > 0)
        {
            arguments.Add(("--write-subs", null));
            if (options.SubtitleLanguages.Any(language => language.EndsWith("-orig", StringComparison.Ordinal)))
                arguments.Add(("--write-auto-subs", null));
            arguments.Add(("--sub-langs", string.Join(',', options.SubtitleLanguages)));
            arguments.Add(("--embed-subs", null));
        }

        if (options.EmbedThumbnail && options.Format is "mp4" or "mkv")
            arguments.Add(("--embed-thumbnail", null));

        // Sorting (rather than filtering) falls back gracefully when the preferred height or bitrate doesn't exist.
        var sort = new List<string>();
        if (options.MaxHeight is int height)
            sort.Add($"res:{height}");
        if (options.IncludeAudio && options.AudioBitrate is int kbps)
            sort.Add($"abr:{kbps}");
        if (options.Format is "mp4")
            sort.Add("vcodec:h264,acodec:aac");
        if (options.Format is "webm")
            sort.Add("vcodec:vp9,acodec:opus");

        arguments.Add(("-f", options.IncludeAudio ? "bv*+ba/b" : "bv*/b"));
        if (!options.IncludeAudio)
            arguments.Add(("--postprocessor-args", "Merger+ffmpeg:-an")); // A combined stream is the fallback; its sound is dropped.
        if (sort.Count > 0)
            arguments.Add(("-S", string.Join(',', sort)));
        arguments.Add(("--merge-output-format", options.Format));
        arguments.Add(("--remux-video", options.Format));
        if (options.EmbedChapters)
            arguments.Add(("--embed-chapters", null));
        return arguments;
    }

    /// <summary>Turns <c>ERROR: [soundcloud] 123: This video is DRM protected</c> into a sentence for people.</summary>
    internal static string DescribeError(string message)
    {
        string text = ErrorPrefix().Replace(message.Trim(), string.Empty);
        if (text.Contains("DRM", StringComparison.OrdinalIgnoreCase))
            return DrmProblem;
        if (text.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase))
            return SignInProblem;

        // Anything else is passed on as yt-dlp said it, just written as a sentence.
        text = text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;
        return text.Length == 0 || text.EndsWith('.') ? text : text + ".";
    }

    [GeneratedRegex(@"^ERROR:\s*(\[[^\]]+\]\s*)?([\w-]+:\s)?")]
    private static partial Regex ErrorPrefix();

    internal static MediaDetails ReadDetails(JsonElement root)
    {
        JsonElement[] formats = root.TryGetProperty("formats", out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().ToArray()
            : [];

        // Some sites list no formats, only a direct URL; there's nothing to choose from, but it still exports.
        JsonElement[] usable = formats.Where(format => format.TryGetProperty("has_drm", out JsonElement drm) is false || drm.ValueKind != JsonValueKind.True).ToArray();
        if (formats.Length > 0 && usable.Length == 0)
            return MediaDetails.Unavailable(DrmProblem);

        JsonElement[] audio = usable.Where(format => IsCodec(format, "acodec") && !IsCodec(format, "vcodec")).ToArray();
        long? audioBytes = audio.Select(GetSize).Where(size => size is not null).DefaultIfEmpty().Max();

        VideoQuality[] qualities = usable
            .Where(format => IsCodec(format, "vcodec") && GetInt(format, "height") is > 0)
            .GroupBy(format => GetInt(format, "height")!.Value)
            .OrderByDescending(group => group.Key)
            .Select(group =>
            {
                // MP4 exports prefer H.264, so that's the size worth estimating.
                JsonElement best = group.FirstOrDefault(format => (GetString(format, "vcodec") ?? string.Empty).StartsWith("avc1", StringComparison.Ordinal));
                long? video = best.ValueKind == JsonValueKind.Object ? GetSize(best) : group.Select(GetSize).Max();
                return new VideoQuality(group.Key, video + (audioBytes ?? 0));
            })
            .ToArray();

        return new MediaDetails(
            null,
            qualities,
            audioBytes,
            ReadSubtitles(root),
            ReadChapters(root),
            GetString(root, "extractor_key") == "Youtube");
    }

    private static SubtitleTrack[] ReadSubtitles(JsonElement root)
    {
        var tracks = new List<SubtitleTrack>();
        if (root.TryGetProperty("subtitles", out JsonElement written) && written.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty language in written.EnumerateObject().Where(language => language.Name != "live_chat"))
                tracks.Add(new SubtitleTrack(language.Name, GetTrackName(language) ?? language.Name, false));
        }

        // Automatic captions come in every language by translation; only the original is worth offering.
        if (root.TryGetProperty("automatic_captions", out JsonElement automatic) && automatic.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty language in automatic.EnumerateObject().Where(language => language.Name.EndsWith("-orig", StringComparison.Ordinal)))
            {
                string name = (GetTrackName(language) ?? language.Name).Replace(" (Original)", string.Empty);
                tracks.Add(new SubtitleTrack(language.Name, $"{name} (automatic)", true));
            }
        }

        return tracks.OrderBy(track => track.IsAutomatic).ThenBy(track => track.Name, StringComparer.CurrentCulture).ToArray();
    }

    private static Chapter[] ReadChapters(JsonElement root)
    {
        if (!root.TryGetProperty("chapters", out JsonElement chapters) || chapters.ValueKind != JsonValueKind.Array)
            return [];

        return chapters.EnumerateArray()
            .Where(chapter => chapter.TryGetProperty("start_time", out _) && chapter.TryGetProperty("end_time", out _))
            .Select(chapter => new Chapter(
                TimeSpan.FromSeconds(chapter.GetProperty("start_time").GetDouble()),
                TimeSpan.FromSeconds(chapter.GetProperty("end_time").GetDouble()),
                GetString(chapter, "title") ?? string.Empty))
            .ToArray();
    }

    private async Task<string> JoinAsync(IReadOnlyList<string> parts, CancellationToken cancellationToken)
    {
        string ffmpeg = tools.Locate("ffmpeg") ?? throw new InvalidOperationException("FFmpeg is needed to join clips.");
        string joined = ClipSuffix().Replace(parts[0], string.Empty);
        string list = Path.Combine(Path.GetTempPath(), $"MediaExport-join-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(list, parts.Select(part => $"file '{part.Replace("'", @"'\''")}'"), new UTF8Encoding(false), cancellationToken);
        try
        {
            var startInfo = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-y", "-hide_banner", "-f", "concat", "-safe", "0", "-i", list, "-c", "copy", joined })
                startInfo.ArgumentList.Add(argument);

            using Process process = Process.Start(startInfo)!;
            string error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"The clips couldn't be joined: {error.Trim().Split('\n').LastOrDefault()}");

            foreach (string part in parts)
                File.Delete(part);
            return joined;
        }
        finally
        {
            File.Delete(list);
        }
    }

    private YtdlpClient CreateClient(ILogger errors)
    {
        YtdlpClient client = new(tools.Locate("yt-dlp") ?? "yt-dlp.exe", errors);
        if (tools.Locate("ffmpeg") is { } ffmpeg)
            client = client.WithFFmpegLocation(ffmpeg);
        if (tools.Locate("deno") is { } deno)
            client = client.AddOption("--js-runtimes", $"deno:{deno}");
        return client;
    }

    private static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    // Ytdlp.NET reports yt-dlp failures only through its logger, so each call gets its own.
    private sealed class ErrorLog : ILogger
    {
        public string? Last { get; private set; }

        public void Log(LogType type, string message)
        {
            if (type == LogType.Error)
                Last = DescribeError(message);
        }
    }

    internal static IReadOnlyList<YtdlpSearchResult> ReadSearchResults(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement entries = document.RootElement.TryGetProperty("entries", out JsonElement value)
            ? value
            : document.RootElement;

        if (entries.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<YtdlpSearchResult>();
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            string? identity = GetString(entry, "id");
            string? title = GetString(entry, "title");

            if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(title))
                continue;

            results.Add(new YtdlpSearchResult(
                identity,
                GetSourceUrl(entry),
                GetThumbnailUri(entry),
                title,
                GetString(entry, "uploader") ?? GetString(entry, "channel"),
                GetDuration(entry),
                GetString(entry, "channel_url") ?? GetString(entry, "uploader_url")));
        }

        return results;
    }

    private static bool IsCodec(JsonElement format, string property) =>
        GetString(format, property) is { } codec && codec != "none";

    private static long? GetSize(JsonElement format) =>
        format.TryGetProperty("filesize", out JsonElement size) && size.TryGetInt64(out long exact) ? exact
        : format.TryGetProperty("filesize_approx", out JsonElement approx) && approx.TryGetDouble(out double estimate) ? (long)estimate
        : null;

    private static int? GetInt(JsonElement entry, string propertyName) =>
        entry.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt32(out int number) ? number : null;

    private static string? GetTrackName(JsonProperty language) =>
        language.Value.ValueKind == JsonValueKind.Array && language.Value.GetArrayLength() > 0
            ? GetString(language.Value[0], "name")
            : null;

    private static string? GetString(JsonElement entry, string propertyName) =>
        entry.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static string? GetSourceUrl(JsonElement entry) =>
        GetString(entry, "webpage_url") ?? GetString(entry, "original_url") ?? GetString(entry, "url");

    private static Uri? GetThumbnailUri(JsonElement entry)
    {
        if (GetString(entry, "thumbnail") is { } single && Uri.TryCreate(single, UriKind.Absolute, out Uri? direct))
            return direct;

        if (!entry.TryGetProperty("thumbnails", out JsonElement thumbnails) || thumbnails.ValueKind != JsonValueKind.Array)
            return null;

        foreach (JsonElement thumbnail in thumbnails.EnumerateArray().Reverse())
        {
            if (Uri.TryCreate(GetString(thumbnail, "url"), UriKind.Absolute, out Uri? uri))
                return uri;
        }

        return null;
    }

    private static TimeSpan? GetDuration(JsonElement entry) =>
        entry.TryGetProperty("duration", out JsonElement value) && value.TryGetDouble(out double seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
}
