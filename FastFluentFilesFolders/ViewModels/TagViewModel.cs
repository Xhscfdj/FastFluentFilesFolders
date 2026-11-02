using CommunityToolkit.Mvvm.ComponentModel;
//using Microsoft.UI.Xaml.Media;
//using System;
//using System.Collections.Generic;
//using System.Text;

namespace FastFluentFilesFolders.ViewModels
{
    public partial class TagViewModel : ObservableObject
    {
        [ObservableProperty] public partial string Name { get; set; } = "Unknown";
        [ObservableProperty] public partial string Glyph { get; set; } = "\uE9CE";
	}
}
