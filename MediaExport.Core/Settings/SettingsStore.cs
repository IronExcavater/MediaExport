// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Storage;
using System.Text.Json.Serialization;

namespace MediaExport.Core.Settings;

/// <summary>Application settings persisted between runs.</summary>
public sealed class AppSettings
{
    public bool IsPaneOpen { get; set; } = true;

    /// <summary>The window's size in screen pixels when it was last neither maximised nor minimised.</summary>
    public int? WindowWidth { get; set; }

    public int? WindowHeight { get; set; }

    public bool IsWindowMaximized { get; set; }

    /// <summary>The search when the app closed, as text. It is kept even if it was never carried out.</summary>
    public string? LastSearch { get; set; }

    /// <summary>The searches that were carried out, newest first, each written as text like <see cref="LastSearch"/>.</summary>
    public List<string> RecentSearches { get; set; } = [];
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
