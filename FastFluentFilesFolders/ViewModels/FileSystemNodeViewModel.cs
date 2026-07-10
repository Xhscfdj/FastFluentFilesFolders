using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI;
//using Isg.Collections;
using FastFluentFilesFolders.Services;
using FastFluentFilesFolders.Extensions;
using FastFluentFilesFolders.Extensions.Interfaces;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
//using System.Threading;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.ViewModels
{
	public partial class FileSystemNodeViewModel : ViewModelBase
	{
		private readonly Configs _configs;
		private readonly DispatcherQueue _uiDispatcherQueue;
		private bool _isLoaded;
		private bool _isCounting;
		private bool _isInited;
		private bool _isLazyLoad;
		private bool _hasBasicInfo;

		// 基础属性
		[ObservableProperty] private bool _isPlaceholder = false;
		[ObservableProperty] private bool _isSpecialFolder = false;
		[ObservableProperty] private bool _willSplitToDifferentSorts = false;
		[ObservableProperty] private string _name = string.Empty;
		[ObservableProperty] private string _fullPath = string.Empty;
		[ObservableProperty] private bool _isDirectory = true;
		[ObservableProperty] private string _extension = string.Empty;
		[ObservableProperty] private ImageSource? _icon;
		[ObservableProperty] private MainIconProvider _iconProvider;
		[ObservableProperty] private long _exactSize = 0;
		[ObservableProperty] private string _visualSize = "0B";
		[ObservableProperty] private DateTime _lastModifiedTime = DateTime.MinValue;
		[ObservableProperty] private DateTime _firstCreatedTime = DateTime.MinValue;
		[ObservableProperty] private string _lastModifiedTimeString = string.Empty;
		[ObservableProperty] private string _firstCreatedTimeString = string.Empty;
		[ObservableProperty] private bool _isSelected = false;
		[ObservableProperty] private bool _isRenaming = false;
		[ObservableProperty] private bool _isCutPending = false;

		// 压缩包预览相关：当节点位于压缩包内部时为 true
		[ObservableProperty] private bool _isArchiveEntry = false;
		// 物理压缩包文件的完整路径（如 C:\a\test.zip）
		public string ArchiveFilePath { get; private set; } = string.Empty;
		// 在压缩包内部的相对路径（"" 表示压缩包根目录）
		public string ArchiveRelativePath { get; private set; } = string.Empty;
		private string _sortByTime = string.Empty;
		public string SortByTime
		{
			get => _sortByTime;
			set
			{
				_sortByTime = value;
				OnPropertyChanged();
			}
		}
		public bool IsGroupExpanded { get; set; } = true;

		public Microsoft.UI.Xaml.Visibility IsRenamingVisibility =>
			IsRenaming ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
		public Microsoft.UI.Xaml.Visibility IsNotRenamingVisibility =>
			IsRenaming ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

		public double CutOpacity => IsCutPending ? 0.4 : 1.0;

		partial void OnIsRenamingChanged(bool value)
		{
			OnPropertyChanged(nameof(IsRenamingVisibility));
			OnPropertyChanged(nameof(IsNotRenamingVisibility));
		}

		partial void OnIsCutPendingChanged(bool value)
		{
			OnPropertyChanged(nameof(CutOpacity));
		}

		// 树形结构相关（文件夹特有，文件则为空）
		[ObservableProperty] private ObservableCollection<FileSystemNodeViewModel> _children = [];
		[ObservableProperty] private string _childrenCountText = string.Empty;
		private int? _cachedChildrenCount;

		public bool IsLoaded => _isLoaded;

		// 构造函数（统一入口）
		public FileSystemNodeViewModel(
			string fullPath,
			bool isDirectory,
			bool isPlaceholder,
			Configs configs,
			DispatcherQueue uiDispatcherQueue,
			bool lazyLoad)
			: base()
		{
			IsPlaceholder = isPlaceholder;
			_isLazyLoad = lazyLoad;
			_uiDispatcherQueue = uiDispatcherQueue;
			if (configs != null)
			{
				_configs = configs;
			}
			_iconProvider = new MainIconProvider(configs);
			FullPath = fullPath;
			IsDirectory = isDirectory;
			// 设置名称和扩展名
			if (isDirectory && !IsPlaceholder)
			{
				// 对于驱动器根目录，名称为 "C:\" 形式
				Children.Add(new PlaceholderNodeViewModel());
				Name = (fullPath.Length == 3 && fullPath.EndsWith(":\\")) ? fullPath : Path.GetFileName(fullPath.TrimEnd('\\'));
				Extension = string.Empty;
				IsSpecialFolder = ShellIconHelper.IsSpecialFolder(fullPath);
				if (_configs != null && _configs.IsTimeGroupedFolder(fullPath))
				{
					WillSplitToDifferentSorts = true;
				}
			}
			else
			{
				Name = Path.GetFileName(fullPath);
				Extension = Path.GetExtension(fullPath);
			}

		    if (!_isLazyLoad)
			{
				_ = LoadBasicInfoAsync();
				if (configs != null)
				{
					_ = LoadIconAsync(fullPath, isDirectory);
				}

				if (isDirectory)
				{
					_ = StartAsyncCount();
				}
			}

			if (!isDirectory)
			{
				_isLoaded = true;
			}
		}

		// 创建压缩包根节点（把压缩包当作一个可进入的“文件夹”）
		public static FileSystemNodeViewModel CreateArchiveRoot(
			string archiveFilePath,
			Configs configs,
			DispatcherQueue uiDispatcherQueue)
		{
			var node = new FileSystemNodeViewModel(
				ArchiveHelper.GetArchiveRootVirtualPath(archiveFilePath),
				true, false, configs, uiDispatcherQueue, true);
			node.IsArchiveEntry = true;
			node.ArchiveFilePath = archiveFilePath;
			node.ArchiveRelativePath = string.Empty;
			node.Name = Path.GetFileName(archiveFilePath);
			node.Extension = Path.GetExtension(archiveFilePath);
			node.Children.Clear();
			node.Children.Add(new PlaceholderNodeViewModel());
			_ = node.LoadIconAsync(archiveFilePath, false);
			return node;
		}

		// 创建压缩包内部指定相对路径的目录节点（用于地址栏/历史导航）
		public static FileSystemNodeViewModel CreateArchiveDirectory(
			string archiveFilePath,
			string relativePath,
			Configs configs,
			DispatcherQueue uiDispatcherQueue)
		{
			if (string.IsNullOrEmpty(relativePath))
				return CreateArchiveRoot(archiveFilePath, configs, uiDispatcherQueue);

			var virtualPath = ArchiveHelper.CombineArchiveVirtualPath(archiveFilePath, relativePath);
			var node = new FileSystemNodeViewModel(virtualPath, true, false, configs, uiDispatcherQueue, true);
			node.IsArchiveEntry = true;
			node.ArchiveFilePath = archiveFilePath;
			node.ArchiveRelativePath = relativePath.TrimEnd('\\');
			node.Name = node.ArchiveRelativePath.Split('\\').Last();
			node.Extension = string.Empty;
			node.Children.Clear();
			node.Children.Add(new PlaceholderNodeViewModel());
			return node;
		}

		// 根据压缩包内部条目创建节点
		private FileSystemNodeViewModel CreateArchiveChild(
			Extensions.Interfaces.ArchiveEntry entry)
		{
			var virtualPath = ArchiveHelper.CombineArchiveVirtualPath(ArchiveFilePath, entry.RelativePath);
			var node = new FileSystemNodeViewModel(
				entry.IsDirectory ? virtualPath : virtualPath.TrimEnd('\\'),
				entry.IsDirectory, false, _configs, _uiDispatcherQueue, true);
			node.IsArchiveEntry = true;
			node.ArchiveFilePath = ArchiveFilePath;
			node.ArchiveRelativePath = entry.RelativePath;
			node.Name = entry.Name;
			node.Extension = entry.IsDirectory ? string.Empty : Path.GetExtension(entry.Name);
			node.ApplyMetadata(entry.IsDirectory, entry.Size, entry.LastModified, entry.LastModified);
			if (entry.IsDirectory)
			{
				node.Children.Clear();
				node.Children.Add(new PlaceholderNodeViewModel());
			}
			return node;
		}

		// 同步加载基本文件/文件夹信息（确保排序时属性已就绪）
		private async Task LoadBasicInfoAsync()
		{
			if (IsPlaceholder || _hasBasicInfo) { return; }
			_hasBasicInfo = true;
			try
			{
				if (IsDirectory)
				{
					var dirInfo = await Task.Run(() => new DirectoryInfo(FullPath));
					LastModifiedTime = dirInfo.LastWriteTimeUtc;
					FirstCreatedTime = dirInfo.CreationTimeUtc;
					ExactSize = 0;
				}
				else if (File.Exists(FullPath))
				{
					var fileInfo = await Task.Run(() => new FileInfo(FullPath));
					LastModifiedTime = fileInfo.LastWriteTimeUtc;
					FirstCreatedTime = fileInfo.CreationTimeUtc;
					ExactSize = fileInfo.Length;
				}

				await _uiDispatcherQueue.EnqueueAsync(() =>
				{
					// 更新字符串显示
					LastModifiedTimeString = LastModifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
					FirstCreatedTimeString = FirstCreatedTime.ToString("yyyy-MM-dd HH:mm:ss");
					VisualSize = FormatFileSize(ExactSize);
				});

				Debug.WriteLine($"[BasicInfo] {FullPath} loaded: Size={ExactSize}, Modified={LastModifiedTimeString}");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[LoadBasicInfo] Error loading info for {FullPath}: {ex.Message}");
				// 保持默认值，不影响排序
			}
		}
		public async Task InitAsync(string fullPath, bool isDirectory)
		{
			if (_isInited) return;
			_isInited = true;

			await Task.Run(async () =>
			{
				await LoadBasicInfoAsync();
				_ = LoadIconAsync(fullPath, isDirectory);
			});
			
			if (IsDirectory)
			{
				await StartAsyncCount();
			}
		}	

		// 直接应用枚举时一次性获取到的元数据，避免每个子项再单独发起一次文件系统访问
		public void ApplyMetadata(bool isDirectory, long size, DateTime lastWriteUtc, DateTime creationUtc)
		{
			if (IsPlaceholder) return;
			_hasBasicInfo = true;
			LastModifiedTime = lastWriteUtc;
			FirstCreatedTime = creationUtc;
			ExactSize = isDirectory ? 0 : size;
			LastModifiedTimeString = LastModifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
			FirstCreatedTimeString = FirstCreatedTime.ToString("yyyy-MM-dd HH:mm:ss");
			VisualSize = FormatFileSize(ExactSize);
		}

		// 同步读取磁盘元数据（用于粘贴/新建等刚创建的项，确保加入分组视图前 LastModifiedTime 已就绪）
		public void LoadMetadataSync()
		{
			if (IsPlaceholder) return;
			try
			{
				if (IsDirectory)
				{
					var dirInfo = new DirectoryInfo(FullPath);
					ApplyMetadata(true, 0, dirInfo.LastWriteTimeUtc, dirInfo.CreationTimeUtc);
				}
				else if (File.Exists(FullPath))
				{
					var fileInfo = new FileInfo(FullPath);
					ApplyMetadata(false, fileInfo.Length, fileInfo.LastWriteTimeUtc, fileInfo.CreationTimeUtc);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[LoadMetadataSync] Error for {FullPath}: {ex.Message}");
			}
		}

		public async Task RefreshAsync()
		{
			_hasBasicInfo = false;
			if (IsPlaceholder) return;
			await LoadBasicInfoAsync();
			_ = LoadIconAsync(FullPath, IsDirectory);
		}

		public async Task LoadIconAsync(string fullPath, bool isDirectory)
		{
			try
			{
				if (IconProvider == null) return;

				// 快速路径：命中缓存直接赋值，省去线程切换与重复解码
				if (IconProvider.TryGetCachedIcon(fullPath, isDirectory, out var cached) && cached != null)
				{
					if (_uiDispatcherQueue.HasThreadAccess)
						Icon = cached;
					else
						_uiDispatcherQueue.TryEnqueue(() => Icon = cached);
					return;
				}

				ImageSource? icon = null;
				await Task.Run( async () =>
				{
					var task = IconProvider.GetIconAsync(fullPath, isDirectory, _uiDispatcherQueue, 32);
					if (task != null) icon = await task;
				});
				if (icon != null)
				{
					_uiDispatcherQueue.TryEnqueue(() => Icon = icon);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[LoadIconAsync] Failed for {fullPath}: {ex.Message}");
			}
		}

		// 静态工具方法
		public static string FormatFileSize(long bytes)
		{
			string[] sizes = { "B", "KB", "MB", "GB", "TB" };
			double len = bytes;
			int order = 0;
			while (len >= 1024 && order < sizes.Length - 1)
			{
				order++;
				len /= 1024;
			}
			return $"{len:0.##} {sizes[order]}";
		}

		// 安全枚举方法（已有）
		public static List<string> SafeGetFiles(string Path)
		{
			var accessible = new List<string>();
			try
			{
				var allFiles = Directory.GetFiles(Path);
				foreach (string file in allFiles)
				{
					if (IsFileAccessible(file))
						accessible.Add(file);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[SafeGetFiles] {Path}: {ex.Message}");
			}
			return accessible;
		}

		public static List<string> SafeGetDirs(string Path)
		{
			var accessible = new List<string>();
			try
			{
				var allSubDirs = Directory.GetDirectories(Path);
				foreach (string subDir in allSubDirs)
				{
					if (IsDirectoryAccessible(subDir))
						accessible.Add(subDir);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[SafeGetDirs] {Path}: {ex.Message}");
			}
			return accessible;
		}

		public static bool IsDirectoryAccessible(string Path)
		{
			try
			{
				return Directory.Exists(Path);
			}
			catch
			{
				return false;
			}
		}

		// 一次目录枚举即拿到名称/属性/时间/大小，避免对每个子项再单独调用 FileInfo/DirectoryInfo
		public readonly record struct FileSystemEntryInfo(
			string FullPath,
			bool IsDirectory,
			long Size,
			DateTime LastWriteTimeUtc,
			DateTime CreationTimeUtc);

		public static List<FileSystemEntryInfo> SafeEnumerateEntries(string path)
		{
			var result = new List<FileSystemEntryInfo>();
			try
			{
				var dirInfo = new DirectoryInfo(path);
				foreach (var entry in dirInfo.EnumerateFileSystemInfos())
				{
					try
					{
						bool isDir = (entry.Attributes & FileAttributes.Directory) != 0;
						long size = isDir ? 0 : ((FileInfo)entry).Length;
						result.Add(new FileSystemEntryInfo(
							entry.FullName,
							isDir,
							size,
							entry.LastWriteTimeUtc,
							entry.CreationTimeUtc));
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"[SafeEnumerateEntries] entry error {entry.FullName}: {ex.Message}");
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[SafeEnumerateEntries] {path}: {ex.Message}");
			}
			return result;
		}

		public static bool IsFileAccessible(string Path)
		{
			try
			{
				return File.Exists(Path);
			}
			catch
			{
				return false;
			}
		}

		// 异步加载子项（仅文件夹有效）
		public async Task LoadChildrenAsync()
		{
			if (_isLoaded || !IsDirectory) return;
			_isLoaded = true;
			await ReloadChildrenAsync();
		}

		public async Task ReloadChildrenAsync()
		{
			if (!IsDirectory) return;

			if (IsArchiveEntry)
			{
				await ReloadArchiveChildrenAsync();
				return;
			}

			var myPath = FullPath;

			var entries = await Task.Run(() => SafeEnumerateEntries(myPath));

			// 目录在前、文件在后，保持 SafeGetDirs()/SafeGetFiles() 的原始集合顺序，不做任何排序
			var dirNodes = new List<FileSystemNodeViewModel>();
			var fileNodes = new List<FileSystemNodeViewModel>();
			foreach (var entry in entries)
			{
				var node = new FileSystemNodeViewModel(entry.FullPath, entry.IsDirectory, false, _configs, _uiDispatcherQueue, true);
				node.ApplyMetadata(entry.IsDirectory, entry.Size, entry.LastWriteTimeUtc, entry.CreationTimeUtc);
				if (entry.IsDirectory)
					dirNodes.Add(node);
				else
					fileNodes.Add(node);
			}

			var allNodes = new List<FileSystemNodeViewModel>(dirNodes.Count + fileNodes.Count);
			allNodes.AddRange(dirNodes);
			allNodes.AddRange(fileNodes);

			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				Children.Clear();
				foreach (var item in allNodes)
				{
					if (WillSplitToDifferentSorts)
						item.SortByTime = Helpers.GroupedFileList.GetTimeGroup(item.LastModifiedTime);
					Children.Add(item);
				}

				var actualCount = Children.Count(c => !c.IsPlaceholder);
				ChildrenCountText = actualCount > 0 ? $"[{actualCount}]" : "[?]";
			});

			_ = LoadIconsBatchedAsync(allNodes);
		}

		private async Task ReloadArchiveChildrenAsync()
		{
			var browser = App.PluginManager?.GetArchiveBrowsers()
				.FirstOrDefault(b => b.CanBrowse(ArchiveFilePath));

			var allNodes = new List<FileSystemNodeViewModel>();
			if (browser != null)
			{
				try
				{
					var entries = await browser.ListEntriesAsync(ArchiveFilePath, ArchiveRelativePath);
					var dirNodes = new List<FileSystemNodeViewModel>();
					var fileNodes = new List<FileSystemNodeViewModel>();
					foreach (var entry in entries)
					{
						var node = CreateArchiveChild(entry);
						if (entry.IsDirectory)
							dirNodes.Add(node);
						else
							fileNodes.Add(node);
					}
					allNodes.AddRange(dirNodes);
					allNodes.AddRange(fileNodes);
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"[ReloadArchiveChildren] {ArchiveFilePath}: {ex.Message}");
				}
			}

			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				Children.Clear();
				foreach (var item in allNodes)
				{
					if (WillSplitToDifferentSorts)
						item.SortByTime = Helpers.GroupedFileList.GetTimeGroup(item.LastModifiedTime);
					Children.Add(item);
				}

				var actualCount = Children.Count(c => !c.IsPlaceholder);
				ChildrenCountText = actualCount > 0 ? $"[{actualCount}]" : "[?]";
			});

			_ = LoadIconsBatchedAsync(allNodes);
		}


		private async Task LoadIconsBatchedAsync(List<FileSystemNodeViewModel> nodes)
		{
			int maxConcurrency = App.SharedViewModel?.AppConfigs?.IconParallelLoadingCount ?? 30;
			if (maxConcurrency <= 0) maxConcurrency = 30;
			var semaphore = new System.Threading.SemaphoreSlim(maxConcurrency, maxConcurrency);

			await Task.Run(async () =>
			{
				var tasks = nodes.Select(async node =>
				{
					await semaphore.WaitAsync();
					try
					{
						await node.LoadIconAsync(node.FullPath, node.IsDirectory);
					}
					finally
					{
						semaphore.Release();
					}
				});
				await Task.WhenAll(tasks);
			});
		}

		// 启动异步统计子项数量（用于显示括号）
		private async Task StartAsyncCount()
		{
			if (_cachedChildrenCount.HasValue || _isCounting || !IsDirectory) return;
			_isCounting = true;

			await Task.Run(async () =>
			{
				try
				{
					int count = SafeGetDirs(FullPath).Count + SafeGetFiles(FullPath).Count;
					_cachedChildrenCount = count;
					_uiDispatcherQueue.TryEnqueue(() =>
					{
						if (Children.Count == 1 && Children[0].IsPlaceholder)
						{
							ChildrenCountText = count > 0 ? $" [{count}]" : string.Empty;
						}
						else
						{
							var actualCount = Children.Count(c => !(c.IsPlaceholder));
							ChildrenCountText = actualCount > 0 ? $" [{actualCount}]" : string.Empty;
						}
					});
				}
				catch { }
				finally { _isCounting = false; }
			});
		}

		// 获取子项数量（用于界面显示）
		public string GetChildrenCount()
		{
			if (!IsDirectory) return string.Empty;
			if (ChildrenCountText != string.Empty) return ChildrenCountText;

			int count = Children.Count(c => !(c.IsPlaceholder));
			if (count == 0 && Children.Count == 1 && Children[0].IsPlaceholder && !string.IsNullOrEmpty(FullPath))
			{
				try
				{
					count = SafeGetDirs(FullPath).Count + SafeGetFiles(FullPath).Count;
				}
				catch
				{
					count = 0;
				}
			}
			return count > 0 ? $" [{count}]" : string.Empty;
		}

		// 节点类型名称（用于调试）
		public string NodeTypeName => this.GetType().Name;

		// 扩展：展开/折叠（若需要）
		private bool _isExpanded;
		public bool IsExpanded
		{
			get => _isExpanded;
			set
			{
				if (SetProperty(ref _isExpanded, value) && value && IsDirectory)
				{
					_ = LoadChildrenAsync();
				}
			}
		}
		//private bool IsRoot()
		//{
		//	if (FullPath == null) return true;
		//	if (FullPath[FullPath.Length - 1] == '\\')
		//	{
		//		return true;
		//	}
		//	return false;
		//}
	}
}
