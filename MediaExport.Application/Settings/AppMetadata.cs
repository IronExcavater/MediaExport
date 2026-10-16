// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Settings;

/// <summary>The identity and project links shown by Settings, independent of package state.</summary>
public sealed record AppMetadata(
    string DisplayName,
    string Version,
    string Contributors,
    Uri SourceUri,
    Uri ReportIssueUri,
    Uri LicenseUri);

public interface IAppMetadataProvider
{
    AppMetadata Get();
}
