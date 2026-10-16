using CommunityToolkit.Mvvm.ComponentModel;
//using Microsoft.UI.Xaml.Media;
//using System;
//using System.Collections.Generic;
//using System.Text;

namespace FastFluentFilesFolders.ViewModels
{
    public partial class TagViewModel : ObservableObject
    {
        [ObservableProperty] private string _name = "Unknown";
        [ObservableProperty] private string _glyph = "\uE9CE";
	}
}
