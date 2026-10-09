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
            // A file that can't be read or understood is treated as no file: the defaults.
            return new AppSettings();
        }
    }
}

// Source-generated so the settings survive trimming in Release builds.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
