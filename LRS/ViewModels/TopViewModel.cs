using BetterBreadcrumbBar.Control;
using BetterBreadcrumbBar.Control.Providers;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LRS.ViewModels
{
	public class TopViewModel
	{
		private string _lastFolderPath = "C:\\";
		public static void NavigateForViewModel(string fullPath)
		{
			App.SharedViewModel.CurrentBreadcrumbPath = fullPath ?? "C:\\";
			App.SharedViewModel.history.Push(FileSystemPathProvider.BuildPathNodes(App.SharedViewModel.CurrentBreadcrumbPath));
		}
		public static List<PathNode>? BBB_BackRequestedForViewModel()
		{
			
			if (App.SharedViewModel.history.Count == 0)
			{
				return null;
			}
			List<PathNode> prev = App.SharedViewModel.history.Pop();
			App.SharedViewModel.CanGoBack = App.SharedViewModel.history.Count > 0;
			App.SharedViewModel.CurrentBreadcrumbPath = prev[^1].FullPath;
			
			App.SharedViewModel.SelectedFolder = new FileSystemNodeViewModel("", true, false, App.SharedViewModel.AppConfigs, DispatcherQueue.GetForCurrentThread(), false);
			return prev;
		}
	}
}
