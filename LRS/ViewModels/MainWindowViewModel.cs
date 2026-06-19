//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LRS.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LRS.ViewModels
{
	public partial class MainWindowViewModel : ViewModelBase
	{
		[RelayCommand]
		private void testFunction()
		{
			Debug.WriteLine("[DebugButton] Pressed.");
			var folder = RootDirectories.FirstOrDefault() as FolderNodeViewModel; // 取第一个驱动器
			_ = UpdateCurrentFolderContentAsync(folder);
			TestString = "Modified by testFunction.";
			//CurrentFolderContent.Add(new FileNodeViewModel(@"C:\D\", _iconProvider, _appConfigs, _uiDispatcherQueue));
			//var temp = CurrentFolderContent;
			//CurrentFolderContent = temp;
			Debug.WriteLine($"[DebugButton] CurrentFolderContent count is {CurrentFolderContent.Count}");
			foreach (var item in CurrentFolderContent)
			{
				Debug.WriteLine($"[DebugButton]Item: {item.Name}, Type is {item.NodeTypeName}");
			}
		}
		[ObservableProperty] private string _testString = "hasn't changed";
		// 文件系统相关的属性和方法
		private readonly IIconProvider _iconProvider;
		[ObservableProperty] private ObservableCollection<FileSystemNodeViewModel> _currentFolderContent = new();
		[ObservableProperty] private Configs _appConfigs = new();
		private ObservableCollection<FileSystemNodeViewModel> _rootDirectories = new();
		public ObservableCollection<FileSystemNodeViewModel> RootDirectories
		{
			get => _rootDirectories;
			set => _rootDirectories = value;
		}
		private Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcherQueue;
		[ObservableProperty] private FolderNodeViewModel? _selectedFolder;
		partial void OnSelectedFolderChanged(FolderNodeViewModel? value)
		{
			Debug.WriteLine($"\n----Selected:{value?.Name}\n");
			Debug.WriteLine($"OnSelectedFolderChanged called with value: {value?.FullPath ?? "null"}");
			Debug.WriteLine($"Is UI thread? {_uiDispatcherQueue.HasThreadAccess}");
			//CurrentFolderContent.Add(new FileNodeViewModel(@"C:\test.txt", _iconProvider, _appConfigs, _uiDispatcherQueue));
			//UpdateCurrentFolderContentSync(value);
			if (value != null)
			{
				_ = UpdateCurrentFolderContentAsync(value);
			}
			else
			{
				CurrentFolderContent.Clear();
			}
		}
		public async Task UpdateCurrentFolderContentAsync(FolderNodeViewModel? folder)
		{
			if (folder == null)
			{
				_uiDispatcherQueue.TryEnqueue(() => CurrentFolderContent.Clear());
				return;
			}

			// 确保子项已加载（同步等待，确保 Children 已填充）
			if (!folder.IsLoaded)
			{
				await folder.LoadChildrenAsync();
			}

			// 此时 folder.Children 已经在 UI 线程完成填充，可以直接读取
			// 但为了线程安全，仍然在 UI 线程执行 Clear + Add
			_uiDispatcherQueue.TryEnqueue(() =>
			{
				CurrentFolderContent.Clear();
				foreach (var item in folder.Children)
				{
					if (item is not PlaceholderNodeViewModel)
						CurrentFolderContent.Add(item);
				}
			});
		}

		public MainWindowViewModel(IIconProvider iconProvider, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcherQueue, Configs configs)
		{
			_uiDispatcherQueue = uiDispatcherQueue;
			_iconProvider = iconProvider;
			RootDirectories = new ObservableCollection<FileSystemNodeViewModel>();
			// 在构造函数中，添加测试数据
			//CurrentFolderContent.Add(new FileNodeViewModel(@"C:\test.txt", _iconProvider, _appConfigs, _uiDispatcherQueue));
			//CurrentFolderContent.Add(new FolderNodeViewModel(@"C:\testfolder", _iconProvider, _appConfigs, _uiDispatcherQueue));
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
		//// 排序相关
		//[ObservableProperty] private string _sortPropertyName = "Name"; // 默认按名称排序

		//[ObservableProperty] private bool _sortAscending = true;

		//[RelayCommand]
		//private void SortItems(string propertyName)
		//{
		//	// 如果点击同一列，切换升序/降序；否则按该列升序
		//	if (propertyName == SortPropertyName)
		//	{
		//		SortAscending = !SortAscending;
		//	}
		//	else
		//	{
		//		SortPropertyName = propertyName;
		//		SortAscending = true;
		//	}

		//	ApplySort();
		//}

		//private void ApplySort()
		//{
		//	if (CurrentFolderContent == null || CurrentFolderContent.Count == 0)
		//		return;

		//	var sorted = CurrentFolderContent.ToList(); // 复制一份

		//	// 根据属性名和排序方向排序
		//	Func<FileSystemNodeViewModel, object> keySelector = propertyName switch
		//	{
		//		"Name" => item => item.Name,
		//		"LastModifiedTime" => item => item.LastModifiedTime,
		//		"FirstCreatedTime" => item => item.FirstCreatedTime,
		//		"ExactSize" => item => item.ExactSize,
		//		_ => item => item.Name
		//	};

		//	if (SortAscending)
		//		sorted = sorted.OrderBy(keySelector).ToList();
		//	else
		//		sorted = sorted.OrderByDescending(keySelector).ToList();

		//	// 重新创建 ObservableCollection 并赋值（触发 PropertyChanged）
		//	CurrentFolderContent = new ObservableCollection<FileSystemNodeViewModel>(sorted);
		//}
	}
}

/* public async Task LoadFolderContentAsync(FolderNodeViewModel folder)
{
	Debug.WriteLine($"----Is UI thread? {_uiDispatcherQueue.HasThreadAccess}");
	Debug.WriteLine($"LoadFolderContentAsync: folder = {folder?.FullPath}");
	if (folder == null)
	{
		Debug.WriteLine("Selected folder is null.");
		return;
	}
	var tcs = new TaskCompletionSource<bool>();
	TestString = "Changed outside tryEnqueue";
	Debug.WriteLine($"Before TryEnqueue: CurrentFolderContent hash = {CurrentFolderContent.GetHashCode()}");

	Debug.WriteLine($"\nInside TryEnqueue: Is UI thread? {_uiDispatcherQueue.HasThreadAccess}\n");
	Debug.WriteLine($"Inside TryEnqueue: CurrentFolderContent hash = {CurrentFolderContent.GetHashCode()}");
	Debug.WriteLine($"Loading folder: {folder.FullPath}");
	TestString = "changed! HU!!!!!!";
	Debug.WriteLine($"Before Clear: _CurrentFolderContent == CurrentFolderContent? {ReferenceEquals(_CurrentFolderContent, CurrentFolderContent)}");
	CurrentFolderContent.Clear();
	var testItem = new FileNodeViewModel(@"C:\test.txt", _iconProvider, _appConfigs, _uiDispatcherQueue);
	Debug.WriteLine($"LoadFolderContentAsync: Cleared CurrentFolderContent");
	Debug.WriteLine($"folder.IsLoaded = {folder.IsLoaded}, folder.Children.Count = {folder.Children.Count}");
	if (!folder.IsLoaded && folder.Children.Count == 1)
	{
		Debug.WriteLine($"Folder '{folder.FullPath}' is not loaded. Loading children...");
		await folder.LoadChildrenAsync();
		Debug.WriteLine($"After LoadChildrenAsync: folder.Children.Count = {folder.Children.Count}");
	}
	else
	{
		Debug.WriteLine("Skipping LoadChildrenAsync (already loaded or no placeholder)");
	}

	int added = 0;

	foreach (var item in folder.Children)
	{
		if (!(item is PlaceholderNodeViewModel))
		{
			CurrentFolderContent.Add(item);
			added++;
			Debug.WriteLine($"Added:{item.Name} Type:{item.GetType().Name}");
			Debug.WriteLine($"[MainWindowViewModel.LoadFolderContentAsync] CurrentFolderContent added item: {item.Name}");
		}
	}
	Debug.WriteLine($"AFTER Clear: _CurrentFolderContent == CurrentFolderContent? {ReferenceEquals(_CurrentFolderContent, CurrentFolderContent)}");
	OnPropertyChanged(nameof(CurrentFolderContent));
	Debug.WriteLine($"Total added: {added}, CurrentFolderContent.Count = {CurrentFolderContent.Count}");
	CurrentFolderContent = CurrentFolderContent;
		//tcs.SetResult(true);

	//await tcs.Task;
} */