// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Text;

namespace MediaExport.Core.Media;

public sealed record MediaSearchResult(
    string Title,
    string? Creator,
    TimeSpan? Duration);
