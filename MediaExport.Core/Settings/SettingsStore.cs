// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaExport.Core.Settings;

/// <summary>Application settings persisted between runs.</summary>
public sealed class AppSettings
{
    public bool IsPaneOpen { get; set; } = true;
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON.</summary>
/// <param name="path">The settings file, or <c>null</c> for an in-memory store.</param>
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
            // Write a temporary file and then swap it in, so a crash mid-write can't corrupt the real one.
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Current, SettingsJson.Default.AppSettings));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Losing a settings change is better than interrupting the user with an error.
        }
    }

    private static AppSettings Load(string? path)
    {
        try
        {
            if (path is null || !File.Exists(path))
                return new AppSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // An unreadable or corrupt file falls back to the defaults.
            return new AppSettings();
        }
    }
}

// Source-generated serialization, because reflection-based JSON breaks when Release builds are trimmed.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
