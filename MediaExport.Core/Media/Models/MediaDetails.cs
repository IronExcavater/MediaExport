// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Media.Models;

public sealed record MediaDetails(
    string Identity,
    string Title,
    string? Creator,
    TimeSpan? Duration,
    DateTimeOffset? UploadDate);
