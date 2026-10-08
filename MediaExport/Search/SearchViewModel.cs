// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using MediaExport.Core.Media.Providers;
using System.Collections.Generic;
using System.Linq;

namespace MediaExport.Search;

/// <summary>What's being searched for, where, and what came back. Shared by the title bar's box and the Search page.</summary>
public sealed partial class SearchViewModel : ObservableObject
{
    public SearchViewModel(IEnumerable<IMediaSearchProvider> providers)
    {
        Providers = providers.ToList();
    }

    public IReadOnlyList<IMediaSearchProvider> Providers { get; }

    // The pattern for anything a page shows that can change: bind to it with {x:Bind ViewModel.Query, Mode=TwoWay}.
    [ObservableProperty]
    public partial string Query { get; set; } = "";
}
