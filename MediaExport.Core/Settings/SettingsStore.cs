// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaExport.Core.Settings;

/// <summary>What the app remembers between runs: the last export choices, theme and recent results.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
    public string? ProviderId { get; set; }
    public string? OutputFolder { get; set; }
    public string VideoFormat { get; set; } = "mp4";
    public int? VideoQuality { get; set; } = 1080;
    public string AudioFormat { get; set; } = "mp3";
    public int? AudioBitrate { get; set; }
    public string FileNameTemplate { get; set; } = "%(title)s";
    public bool EmbedMetadata { get; set; } = true;
    public bool EmbedThumbnail { get; set; } = true;
    public bool EmbedChapters { get; set; } = true;
    public string SubtitleLanguages { get; set; } = "en";
    public List<MediaSearchResult> RecentResults { get; set; } = [];
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
            File.WriteAllText(path, JsonSerializer.Serialize(Current, SettingsJson.Default.AppSettings));
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }
}

// Source-generated so the settings survive trimming in Release builds.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
