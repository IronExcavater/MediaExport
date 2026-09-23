using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediaExport.ViewModels;

public partial class ExportViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    public partial string Url { get; set; } = string.Empty;

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
