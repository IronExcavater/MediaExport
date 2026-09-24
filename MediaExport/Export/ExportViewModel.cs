// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Media;
using MediaExport.Core.Media.SearchProvider;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MediaExport.Export;

public partial class ExportViewModel : ObservableObject, IDisposable
{
    public void Dispose()
    {
        throw new NotImplementedException();
    }

    public IReadOnlyList<IMediaSearchProvider> SearchProviders { get; }
    public ObservableCollection<MediaSearchResult> SearchResults { get; } = new ObservableCollection<MediaSearchResult>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextInput))]
    public partial string Input { get; set; } = string.Empty;

    public bool IsTextInput
    {
        get
        {
            var value = Input.Trim();

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return false;

            return true;
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {

    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        
    }

    [RelayCommand]
    private async Task SelectResultAsync()
    {

    }
}
