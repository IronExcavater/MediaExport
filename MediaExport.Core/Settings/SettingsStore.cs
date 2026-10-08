// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaExport.Core.Settings;

public enum ExportOutcome
{
    Completed,
    Failed,
    Cancelled
}

/// <summary>A finished export, kept so Activity still lists it after a restart.</summary>
public sealed record ExportRecord(
    MediaSearchResult Media,
    ExportOptions Options,
    ExportOutcome Outcome,
    IReadOnlyList<string> Files,
    string? Message,
    DateTimeOffset FinishedAt);

public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized);

/// <summary>Everything the app remembers between runs.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
    public WindowPlacement? Window { get; set; }
    public bool IsPaneOpen { get; set; } = true;

    // The export page's choices, so the next export starts from the last one.
    public string? ProviderId { get; set; }
    public SearchSort Sort { get; set; }
    public SearchArrangement Arrangement { get; set; }
    public string? OutputFolder { get; set; }
    public bool IncludeVideo { get; set; } = true;
    public bool IncludeAudio { get; set; } = true;
    public string VideoFormat { get; set; } = "mp4";
    public int? VideoQuality { get; set; } = 1080;
    public string AudioFormat { get; set; } = "mp3";
    public int? AudioBitrate { get; set; }
    public string FileNameTemplate { get; set; } = "%(title)s";
    public bool EmbedMetadata { get; set; } = true;
    public bool EmbedThumbnail { get; set; } = true;
    public bool EmbedChapters { get; set; } = true;
    public bool RemoveSponsors { get; set; }
    public List<string> SubtitleLanguageCodes { get; set; } = [];

    /// <summary>A browser to borrow a sign-in from, e.g. <c>edge</c>; <c>auto:edge</c> uses it only when a site asks; <c>null</c> never signs in.</summary>
    public string? CookiesFromBrowser { get; set; } = "auto";

    public List<MediaSearchResult> RecentResults { get; set; } = [];
    public List<ExportRecord> History { get; set; } = [];
}

/// <summary>Keeps <see cref="AppSettings"/> in a JSON file; a <c>null</c> path keeps them in memory only.</summary>
public sealed class SettingsStore(string? path)
{
    public AppSettings Current { get; } = Load(path);

    public void Save()
    {
        if (path is null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Written aside then swapped in, so a crash mid-write can't leave a half-written file.
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Current, SettingsJson.Default.AppSettings));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save them shouldn't interrupt anything.
        }
    }

    private static AppSettings Load(string? path)
    {
        try
        {
            return path is not null && File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJson.Default.AppSettings) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AppSettings();
        }
    }
}

// Source-generated so the settings survive trimming in Release builds.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
