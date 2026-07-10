//using BetterBreadcrumbBar.Control;
//using FastFluentFilesFolders.ViewModels;
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

namespace FastFluentFilesFolders.Views
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
			App.SharedViewModel.BreadcrumbRefreshRequested += OnBreadcrumbRefreshRequested;
		}

		private void OnBreadcrumbRefreshRequested()
		{
			DispatcherQueue.TryEnqueue(() => BreadcrumbCtrl.RefreshSegments());
		}
	}
}
