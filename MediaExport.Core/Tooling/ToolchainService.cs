// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;

namespace MediaExport.Core.Tooling;

public sealed class ToolchainService(SettingsStore settings)
{
    public string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MediaExport",
        "Tools");

    public ToolSettings? GetTool(string id) =>
        settings.Current.Tools.TryGetValue(id, out var tool) ? tool : null;

    public string? Locate(string executable)
    {
        var fileName = executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? executable
            : executable + ".exe";

        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var folder in paths.Prepend(Folder))
        {
            var candidate = Path.Combine(folder, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public bool IsInstalled(string id)
    {
        var tool = GetTool(id);
        if (tool is null)
            return false;

        if (!string.IsNullOrWhiteSpace(tool.InstallPath))
            return File.Exists(tool.InstallPath);

        return Locate(tool.Executable) is not null;
    }

    public async Task EnsureInstalledAsync(string id, string downloadUrl, CancellationToken cancellationToken = default)
    {
        var tool = GetTool(id) ?? new ToolSettings { Id = id, Executable = id };
        var installDir = Path.Combine(Folder, id);
        Directory.CreateDirectory(installDir);

        var targetFile = Path.Combine(
            installDir,
            tool.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? tool.Executable
                : tool.Executable + ".exe");

        if (!File.Exists(targetFile))
        {
            using var client = new HttpClient();
            var data = await client.GetByteArrayAsync(downloadUrl, cancellationToken);
            await File.WriteAllBytesAsync(targetFile, data, cancellationToken);
        }

        tool.InstallPath = targetFile;
        tool.IsInstalled = true;
        tool.Version = "latest";
        tool.LastCheckedUtc = DateTimeOffset.UtcNow;

        settings.Current.Tools[id] = tool;
        settings.Save();
    }

    public async Task UpdateAsync(string id, string downloadUrl, CancellationToken cancellationToken = default)
    {
        await EnsureInstalledAsync(id, downloadUrl, cancellationToken);
    }

    public Task RemoveAsync(string id)
    {
        var tool = GetTool(id);
        if (tool is not null && !string.IsNullOrWhiteSpace(tool.InstallPath) && File.Exists(tool.InstallPath))
            File.Delete(tool.InstallPath);

        settings.Current.Tools.Remove(id);
        settings.Save();

        return Task.CompletedTask;
    }
}