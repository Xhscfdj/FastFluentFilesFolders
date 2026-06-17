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
				var viewModel = new MainWindowViewModel(iconProvider, uiDispatcherQueue, configs);
				this.DataContext = viewModel;

			}
			catch (Exception ex)
			{
				Debug.WriteLine($"InitializeComponent failed: {ex}");
				throw; // 或者不抛出，看能否显示窗口
			}

		}

		private void TreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs e)
		{
			if (DataContext is not MainWindowViewModel mainVm) return;

			// 获取新增的选中项（单选时通常只有一个）
			var selectedItem = e.AddedItems.Count > 0 ? e.AddedItems[0] as FileSystemNodeViewModel : null;

			if (selectedItem is FolderNodeViewModel folder)
			{
				mainVm.SelectedFolder = folder; // 触发 OnSelectedFolderChanged
				Debug.WriteLine($"Selected folder: {folder.FullPath}");
			}
			else
			{
				// 点击文件或取消选择时，可清空内容或保持原样
				// mainVm.SelectedFolder = null; 
			}
		}
	}

}
