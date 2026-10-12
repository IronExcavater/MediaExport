// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Storage;
using System.Text.Json.Serialization;

namespace MediaExport.Core.Settings;

// Enum for themes
public enum AppTheme
{
    System = 0,
    Dark = 1,
    Light = 2
}

public sealed class ToolSettings
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Executable { get; set; } = "";
    public string? InstallPath { get; set; }
    public bool AutoUpdate { get; set; } = true;
    public string? Version { get; set; }
    public bool IsInstalled { get; set; }
    public DateTimeOffset? LastCheckedUtc { get; set; }
}
/// <summary>Application settings persisted between runs.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool IsPaneOpen { get; set; } = true;

    /// <summary>The window's size in screen pixels when it was last neither maximised nor minimised.</summary>
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool IsWindowMaximized { get; set; }

    /// <summary>The search when the app closed, as text. It is kept even if it was never carried out.</summary>
    public string? LastSearch { get; set; }

    /// <summary>The searches that were carried out, newest first, each written as text like <see cref="LastSearch"/>.</summary>
    public List<string> RecentSearches { get; set; } = [];

    /// <summary>The searches that were pinned, which are kept however many are carried out after them.</summary>
    public List<string> PinnedSearches { get; set; } = [];

    /// <summary>The media that was opened, newest first, and the media that was pinned.</summary>
    public List<MediaSearchResult> RecentlyViewed { get; set; } = [];
    public List<MediaSearchResult> PinnedViewed { get; set; } = [];

    /// <summary>
    /// This defaults to the system theme 0.
    /// </summary>
    public int ThemeIndex { get; set; } = 0; // 0 = System, 1 = Dark, 2 = Light

    public Dictionary<string, ToolSettings> Tools { get; set; } = new()
    {
        ["yt-dlp"] = new ToolSettings
        {
            Id = "yt-dlp",
            Name = "yt-dlp",
            Executable = "yt-dlp",
            AutoUpdate = true
        },
        ["ffmpeg"] = new ToolSettings
        {
            Id = "ffmpeg",
            Name = "ffmpeg",
            Executable = "ffmpeg",
            AutoUpdate = true
        },
        ["ffprobe"] = new ToolSettings
        {
            Id = "ffprobe",
            Name = "ffprobe",
            Executable = "ffprobe",
            AutoUpdate = true
        }
    };
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON.</summary>
/// <param name="path">The settings file, or <c>null</c> for an in-memory store.</param>
public sealed class SettingsStore(string? path)
{
    public AppSettings Current { get; } = JsonFile.Load(path, SettingsJson.Default.AppSettings) ?? new AppSettings();

    public void Save() => JsonFile.Save(path, Current, SettingsJson.Default.AppSettings);

}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
