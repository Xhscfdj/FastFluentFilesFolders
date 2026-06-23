//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
using BetterBreadcrumbBar.Control;
using BetterBreadcrumbBar.Control.Providers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;
using LRS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Runtime.InteropServices;

namespace LRS.ViewModels
{
	public partial class MainWindowViewModel : ViewModelBase
	{
		[RelayCommand]
		private void testFunction()
		{
			Debug.WriteLine("[DebugButton] Pressed.");
			var folder = RootDirectories.FirstOrDefault(); // 取第一个驱动器
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
		[ObservableProperty] private IPathProvider _pathProvider = new FileSystemPathProvider();
		[ObservableProperty] private string _testString = "hasn't changed";
		// 文件系统相关的属性和方法
		private readonly IIconProvider _iconProvider;
		//[ObservableProperty] private string[] _PathsForBreadcrumbBar = ["C:\\"];
		[ObservableProperty] private ObservableCollection<FileSystemNodeViewModel> _currentFolderContent = new();
		[ObservableProperty] private string _currentBreadcrumbPath = "C:\\";
		[ObservableProperty] private Configs _appConfigs = new();
		[ObservableProperty] private bool _canGoBack;
		private SemaphoreSlim IconLoadSemaphore = new(30, 30); // 最多10个并发
		public Stack<List<PathNode>> history = new();
		private ObservableCollection<FileSystemNodeViewModel> _rootDirectories = new();
		public ObservableCollection<FileSystemNodeViewModel> RootDirectories
		{
			get => _rootDirectories;
			set => _rootDirectories = value;
		}
		private Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcherQueue;
		[ObservableProperty] private FileSystemNodeViewModel? _selectedFolder;
		partial void OnSelectedFolderChanged(FileSystemNodeViewModel? value)
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

		public async Task UpdateCurrentFolderContentAsync(FileSystemNodeViewModel? folder)
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
			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				CurrentFolderContent.Clear();
				foreach (var item in folder.Children)
				{
					if (!item.IsPlaceholder)
						CurrentFolderContent.Add(item);
				}
				CurrentBreadcrumbPath = folder.FullPath;
				// 以下代码原本用来切掉PathsForBreadcrumbBar的最后一项，现在用不到，遂注释
				//PathsForBreadcrumbBar = folder.FullPath.Split();
				//string[] newArray = new string[PathsForBreadcrumbBar.Length - 1];
				//Array.Copy(PathsForBreadcrumbBar, 0, newArray, 0, PathsForBreadcrumbBar.Length - 1);
			});
		}

		public MainWindowViewModel(IIconProvider iconProvider, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcherQueue, Configs configs)
		{
			_uiDispatcherQueue = uiDispatcherQueue;
			_iconProvider = iconProvider;
			NavigateToPathCommand = new RelayCommand<string>(NavigateToPath);
			NavigateToSubFolderCommand = new RelayCommand<string>(NavigateToPath);
			if (configs.IconParallelLoadingCount != 0) IconLoadSemaphore = new(configs.IconParallelLoadingCount, configs.IconParallelLoadingCount);
			//在构造函数中，添加测试数据
			//CurrentFolderContent.Add(new FileNodeViewModel(@"C:\test.txt", _iconProvider, _appConfigs, _uiDispatcherQueue));
			//CurrentFolderContent.Add(new FileSystemNodeViewModel(@"C:\testfolder", _iconProvider, _appConfigs, _uiDispatcherQueue));
			foreach (var drive in DriveInfo.GetDrives())
			{
				if (drive.IsReady)  // 只添加就绪的驱动器
				{
					RootDirectories.Add(new FileSystemNodeViewModel(drive.RootDirectory.FullName, true, false, configs, uiDispatcherQueue, false));
				}
			}
			SelectedFolder = RootDirectories.FirstOrDefault();
		}
		public void OpenItem(FileSystemNodeViewModel item)
		{
			if (item.IsDirectory)
			{
				SelectedFolder = item;

			}
			else if (!item.IsDirectory)
			{
				OpenWithDefaultProgram(item.FullPath);
			}
		}
		/// <summary>
		/// 使用 Windows 默认关联程序打开指定路径的文件
		/// </summary>
		/// <param name="filePath">要打开的文件的完整路径</param>
		/// <exception cref="ArgumentNullException">路径为空或 null</exception>
		/// <exception cref="FileNotFoundException">文件不存在</exception>
		/// <exception cref="InvalidOperationException">打开文件时发生其他错误</exception>
		public static void OpenWithDefaultProgram(string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath))
				throw new ArgumentNullException(nameof(filePath), "文件路径不能为空");

			if (!File.Exists(filePath))
				throw new FileNotFoundException($"文件不存在: {filePath}", filePath);

			try
			{
				// UseShellExecute = true 表示使用 Windows Shell（即双击打开的方式）
				Process.Start(new ProcessStartInfo
				{
					FileName = filePath,
					UseShellExecute = true
				});
			}
			catch(Win32Exception ex) when (ex.NativeErrorCode == 1155)
			{
				IntPtr result = ShellExcute
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"无法打开文件: {ex.Message}", ex);
			}
		}
		public ICommand NavigateToPathCommand { get; }  // 用于文本导航
		public ICommand NavigateToSubFolderCommand { get; } // 用于子菜单导航

		private void NavigateToPath(string path)
		{
			// 根据路径查找对应的 FileSystemNodeViewModel 并设置为 SelectedFolder
			var target = FindNodeByPath(path);
			if (target != null)
				SelectedFolder = target;
		}
		public FileSystemNodeViewModel? FindNodeByPath(string fullPath)
		{
			foreach (var root in RootDirectories)
			{
				var result = FindNodeRecursive(root, fullPath);
				if (result != null)
					return result;
			}
			return null;
		}

		public static FileSystemNodeViewModel? FindNodeRecursive(FileSystemNodeViewModel node, string fullPath)
		{
			if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
				return node;

			if (node.IsDirectory && node.IsLoaded)
			{
				foreach (var child in node.Children)
				{
					var result = FindNodeRecursive(child, fullPath);
					if (result != null)
						return result;
				}
			}
			return null;
		}
	}
}