using LRS.Services;
using LRS.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace LRS.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class FileTreeView : Page
    {
        public FileTreeView()
        {
			Configs configs = App.Services.GetRequiredService<Configs>();
			try
			{
				InitializeComponent();
				var uiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
				IIconProvider iconProvider = new WindowsIconProvider();
				this.DataContext = App.SharedViewModel;

			}
			catch (Exception ex)
			{
				Debug.WriteLine($"InitializeComponent failed: {ex}");
				throw; // 或者不抛出，看能否显示窗口
			}

		}

		private async void TreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
		{
			Debug.WriteLine($"[TreeView_SelectionChanged] Entered. AddedItems count: {args.AddedItems.Count}");
			// 获取选中的 TreeViewItem 的数据上下文
			var selectedItem = args.AddedItems.FirstOrDefault() as FileSystemNodeViewModel;
			if (selectedItem is null)
			{
				Debug.WriteLine("[TreeView_SelectionChanged] No FileSystemNodeViewModel selected.");
				return;
			}
			await Task.Delay(50);
			Debug.WriteLine($"[TreeView_SelectionChanged] Selected item: {selectedItem.Name}, Type: {selectedItem.NodeTypeName}");
			_ = DispatcherQueue.TryEnqueue(() =>
			{
				if (selectedItem is FileSystemNodeViewModel folder)
				{
					Debug.WriteLine($"[TreeView_SelectionChanged] Setting SelectedFolder to {folder.FullPath}");
					// 调用 ViewModel 的更新方法
					var vm = DataContext as MainWindowViewModel;
					vm?.SelectedFolder = folder;
					//vm?.UpdateCurrentFolderContentAsync(folder);
				}
				else if (!selectedItem.IsDirectory)
				{
					Debug.WriteLine("[TreeView_SelectionChanged] Selected item is not a folder. Setting SelectedFolder to null.");
					// 可选：清空或显示文件信息，这里选择清空
					//var vm = DataContext as MainWindowViewModel;
					//vm?.UpdateCurrentFolderContentAsync(null);
				}
			});
		}
	}

}
