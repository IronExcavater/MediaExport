// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MediaExport.Core.Storage;

/// <summary>Keeps one object in a JSON file between runs. A file that can't be read or written is skipped.</summary>
/// <remarks>The type information is source-generated, because reflection-based JSON breaks in trimmed builds.</remarks>
internal static class JsonFile
{
    /// <returns>The object, or <c>null</c> if there is no file or it is unreadable or corrupt.</returns>
    public static T? Load<T>(string? path, JsonTypeInfo<T> type) where T : class
    {
        try
        {
            if (path is null || !File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, type);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <param name="path">The file, or <c>null</c> to keep nothing, as tests do.</param>
    public static void Save<T>(string? path, T value, JsonTypeInfo<T> type)
    {
        if (path is null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Write a temporary file and then swap it in, so a crash mid-write can't corrupt the real one.
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, type));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Losing a change is better than interrupting the user with an error.
        }
    }
}
