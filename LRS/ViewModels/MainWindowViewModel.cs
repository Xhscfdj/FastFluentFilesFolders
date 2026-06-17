//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LRS.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace LRS.ViewModels
{
	public partial class MainWindowViewModel : ViewModelBase
	{
		// 文件系统相关的属性和方法
		private readonly IIconProvider _iconProvider;

		[ObservableProperty] private Configs _appConfigs = new();
		private ObservableCollection<FileSystemNodeViewModel> _rootDirectories = new();
		public ObservableCollection<FileSystemNodeViewModel> RootDirectories
		{
			get => _rootDirectories;
			set => _rootDirectories = value;
		}
		private Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcherQueue;
		[ObservableProperty] private ObservableCollection<FileSystemNodeViewModel> _currentFolderItems = new();
		[ObservableProperty] private FolderNodeViewModel? _selectedFolder;
		partial void OnSelectedFolderChanged(FolderNodeViewModel? value)
		{
			if (value != null)
			{
				_ = LoadFolderContentAsync(value);
			}
			else
			{
				CurrentFolderItems.Clear();
			}
		}

		public async Task LoadFolderContentAsync(FolderNodeViewModel folder)
		{
			if (folder == null)
			{
				Debug.WriteLine("Selected folder is null.");
				return;
			}

			Debug.WriteLine($"Loading folder: {folder.FullPath}");

			CurrentFolderItems.Clear();

			if (!folder.IsLoaded && folder.Children.Count == 1)
			{
				Debug.WriteLine($"Folder '{folder.FullPath}' is not loaded. Loading children...");
				await folder.LoadChildrenAsync();
			}

			foreach (var item in folder.Children)
			{
				if (item is not PlaceholderNodeViewModel)
				{
					CurrentFolderItems.Add(item);
					Debug.WriteLine("[MainWindowViewModel.LoadFolderContentAsync] CurrentFolderItems added item.");
				}
			}
		}
		public MainWindowViewModel(IIconProvider iconProvider, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcherQueue, Configs configs)
		{
			_uiDispatcherQueue = uiDispatcherQueue;
			_iconProvider = iconProvider;
			RootDirectories = new ObservableCollection<FileSystemNodeViewModel>();
			foreach (var drive in DriveInfo.GetDrives())
			{
				if (drive.IsReady)  // 只添加就绪的驱动器
				{
					RootDirectories.Add(new FolderNodeViewModel(drive.RootDirectory.FullName, _iconProvider, configs, uiDispatcherQueue));
				}
			}

		}
		public void OpenItem(FileSystemNodeViewModel item)
		{
			if (item is FolderNodeViewModel folder)
			{
				SelectedFolder = folder;

			}
			else if (item is FileNodeViewModel file)
			{
				Debug.WriteLine($"Double clicked file: {file.Name}");
			}
		}
	}
}
