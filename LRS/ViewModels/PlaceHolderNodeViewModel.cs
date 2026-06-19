using System;
using System.Collections.Generic;
using System.Text;

namespace LRS.ViewModels
{
    internal class PlaceholderNodeViewModel : FileSystemNodeViewModel
    {
        public PlaceholderNodeViewModel(string? fullPath, Microsoft.UI.Dispatching.DispatcherQueue? uiDispatcherQueue = null) : base(fullPath, uiDispatcherQueue)
        {
            Name = "Loading";
        }
    }
}
