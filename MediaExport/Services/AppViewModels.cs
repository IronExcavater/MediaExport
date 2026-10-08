// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Export;
using MediaExport.Search;
using MediaExport.Settings;

namespace MediaExport.Services;

/// <summary>The application-scoped view models shared by the shell and its pages.</summary>
public sealed record AppViewModels(
    SearchViewModel Search,
    ResultsViewModel Results,
    ActivityViewModel Activity,
    ExportViewModel Export,
    SettingsViewModel Settings);
