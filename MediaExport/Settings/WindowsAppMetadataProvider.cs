// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace MediaExport.Settings;

/// <summary>Reads About information from the package when present and assembly metadata during development.</summary>
public sealed class WindowsAppMetadataProvider : IAppMetadataProvider
{
    private readonly Assembly _assembly = typeof(App).Assembly;

    public AppMetadata Get()
    {
        var metadata = _assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(item => item.Value is not null)
            .ToDictionary(item => item.Key, item => item.Value!, StringComparer.OrdinalIgnoreCase);
        var repository = ReadUri(metadata, "RepositoryUrl", "https://github.com/IronExcavater/MediaExport");

        var displayName = _assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
            ?? _assembly.GetName().Name
            ?? "MediaExport";
        var version = _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0]
            ?? _assembly.GetName().Version?.ToString(3)
            ?? "Development";

        try
        {
            var package = Package.Current;
            displayName = package.DisplayName;
            var packaged = package.Id.Version;
            version = packaged.Revision == 0
                ? $"{packaged.Major}.{packaged.Minor}.{packaged.Build}"
                : $"{packaged.Major}.{packaged.Minor}.{packaged.Build}.{packaged.Revision}";
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            // Unpackaged development builds have no Package.Current identity.
        }

        var company = _assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
        var contributors = string.IsNullOrWhiteSpace(company) ? "MediaExport contributors" : $"By {company}";
        return new AppMetadata(
            displayName,
            version,
            contributors,
            repository,
            ReadUri(metadata, "ReportIssueUri", $"{repository.ToString().TrimEnd('/')}/issues/new"),
            ReadUri(metadata, "LicenseUri", $"{repository.ToString().TrimEnd('/')}/blob/dev/LICENSE"));
    }

    private static Uri ReadUri(IReadOnlyDictionary<string, string> metadata, string key, string fallback) =>
        new(metadata.GetValueOrDefault(key, fallback));
}
