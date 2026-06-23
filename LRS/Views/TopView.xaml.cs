//using BetterBreadcrumbBar.Control;
//using LRS.ViewModels;
//using Microsoft.UI.Dispatching;
//using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
//using Microsoft.UI.Xaml.Controls.Primitives;
//using Microsoft.UI.Xaml.Data;
//using Microsoft.UI.Xaml.Input;
//using Microsoft.UI.Xaml.Media;
//using Microsoft.UI.Xaml.Navigation;
//using System;
//using System.Collections.Generic;
//using System.ComponentModel;
//using System.IO;
//using System.Linq;
//using System.Runtime.InteropServices.WindowsRuntime;
//using Windows.Foundation;
//using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace LRS.Views
{
	/// <summary>
	/// An empty page that can be used on its own or navigated to within a Frame.
	/// </summary>
	public sealed partial class TopView : Page
	{
		public TopView()
		{
			InitializeComponent();
			DataContext = App.SharedViewModel;
		}
		//public TopView()
		//{
		//	InitializeComponent();
		//	DataContext = App.SharedViewModel;

		//	App.SharedViewModel.PropertyChanged += OnCurrentBreadcrumbPathChanged;
		//}
		//public void OnCurrentBreadcrumbPathChanged(object sender, PropertyChangedEventArgs e) 
		//{
		//	if (e.PropertyName == nameof(MainWindowViewModel.CurrentBreadcrumbPath))
		//	{
		//		BBB.SetPath(App.SharedViewModel.CurrentBreadcrumbPath);
		//	}

		//}
		//private void Navigate(string fullPath)
		//{
		//	LRS.ViewModels.TopViewModel.(fullPath);
		//	BBB.SetPath(fullPath);
		//}
		//private void FsBreadcrumb_NodeSelected(object s, PathNodeEventArgs e)
		//	=> Navigate(e.Node.FullPath);
		//private void BBB_UpRequested(object s, PathNodeEventArgs e)
		//{
		//	Navigate(e.Node.FullPath);
		//}
		//private void BBB_HomeRequested(object s, EventArgs e)
		//{
		//	Navigate(App.SharedViewModel.AppConfigs.HomePageFullPath);

		//	DispatcherQueue uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
		//	App.SharedViewModel.SelectedFolder = new FileSystemNodeViewModel(App.SharedViewModel.AppConfigs.HomePageFullPath, true, false, App.SharedViewModel.AppConfigs, DispatcherQueue.GetForCurrentThread(), false);
		//}
		//private void BBB_BackRequested(object s, EventArgs e)
		//{
		//	var prev = TopViewModel.BBB_BackRequestedForViewModel();
		//	if (prev != null) BBB.SetPath(prev);
		//}
	}
}
