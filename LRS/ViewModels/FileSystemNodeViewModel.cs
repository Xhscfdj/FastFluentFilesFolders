using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI;
using LRS.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Isg.Collections;

namespace LRS.ViewModels
{
	public partial class FileSystemNodeViewModel : ViewModelBase
	{
		private readonly Configs _configs;
		private readonly DispatcherQueue _uiDispatcherQueue;
		private bool _isLoaded;
		private bool _isCounting;
		private bool _isInited;
		private bool _isLazyLoad;

		// 基础属性
		[ObservableProperty] private bool _isPlaceholder = false;
		[ObservableProperty] private string _name = string.Empty;
		[ObservableProperty] private string _fullPath = string.Empty;
		[ObservableProperty] private bool _isDirectory = true;
		[ObservableProperty] private string _extension = string.Empty;
		[ObservableProperty] private ImageSource? _icon;
		[ObservableProperty] private IIconProvider _iconProvider;
		[ObservableProperty] private long _exactSize = 0;
		[ObservableProperty] private string _visualSize = "0B";
		[ObservableProperty] private DateTime _lastModifiedTime = DateTime.MinValue;
		[ObservableProperty] private DateTime _firstCreatedTime = DateTime.MinValue;
		[ObservableProperty] private string _lastModifiedTimeString = string.Empty;
		[ObservableProperty] private string _firstCreatedTimeString = string.Empty;
		[ObservableProperty] private bool _isSelected = false;
		
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
				IconProvider = configs.ifUsesWin32APIToGetIcon ? new ShellIconHelper() : new WindowsIconProvider();
			}
			FullPath = fullPath;
			IsDirectory = isDirectory;
			// 设置名称和扩展名
			if (isDirectory && !IsPlaceholder)
			{
				// 对于驱动器根目录，名称为 "C:\" 形式
				Children.Add(new PlaceholderNodeViewModel());
				Name = (fullPath.Length == 3 && fullPath.EndsWith(":\\")) ? fullPath : Path.GetFileName(fullPath.TrimEnd('\\'));
				Extension = string.Empty;
			}
			else
			{
				Name = Path.GetFileName(fullPath);
				Extension = Path.GetExtension(fullPath);
			}

		    _ = LoadBasicInfoAsync();
			if (configs != null && !_isLazyLoad)
			{
				_ = LoadIconAsync(fullPath, isDirectory);
			}


			if (isDirectory)
			{
				_ = StartAsyncCount();
			}
			else
			{
				_isLoaded = true;
			}
		}

		// 同步加载基本文件/文件夹信息（确保排序时属性已就绪）
		private async Task LoadBasicInfoAsync()
		{
			if (IsPlaceholder) { return; }
			try
			{
				if (IsDirectory)
				{
					await Task.Run(() =>
					{
						var dirInfo = new DirectoryInfo(FullPath);
						LastModifiedTime = dirInfo.LastWriteTimeUtc;
						FirstCreatedTime = dirInfo.CreationTimeUtc;
						ExactSize = 0;
					});
				}
				else if (File.Exists(FullPath))
				{
					await Task.Run(() =>
					{
						var fileInfo = new FileInfo(FullPath);
						LastModifiedTime = fileInfo.LastWriteTimeUtc;
						FirstCreatedTime = fileInfo.CreationTimeUtc;
						ExactSize = fileInfo.Length;
					});
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
				await LoadIconAsync(fullPath, isDirectory);
			});
			
			if (IsDirectory)
			{
				await StartAsyncCount();
			}
		}	

		public async Task LoadIconAsync(string fullPath, bool isDirectory)
		{
			try
			{
				if (IconProvider == null) return;

				ImageSource? icon = null;
				await Task.Run( async () =>
				{
					if (_configs.ifUsesWin32APIToGetIcon)
					{
						icon = await ((ShellIconHelper)IconProvider).GetIconAsync(fullPath, isDirectory, _uiDispatcherQueue, 32);
					}
					else
					{
						icon = await ((WindowsIconProvider)IconProvider).GetIconAsync(fullPath, isDirectory, _uiDispatcherQueue);
					}
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
		public static List<string> SafeGetFiles(string path)
		{
			var accessible = new List<string>();
			try
			{
				var allFiles = Directory.GetFiles(path);
				foreach (string file in allFiles)
				{
					if (IsFileAccessible(file))
						accessible.Add(file);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[SafeGetFiles] {path}: {ex.Message}");
			}
			return accessible;
		}

		public static List<string> SafeGetDirs(string path)
		{
			var accessible = new List<string>();
			try
			{
				var allSubDirs = Directory.GetDirectories(path);
				foreach (string subDir in allSubDirs)
				{
					if (IsDirectoryAccessible(subDir))
						accessible.Add(subDir);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[SafeGetDirs] {path}: {ex.Message}");
			}
			return accessible;
		}

		public static bool IsDirectoryAccessible(string path)
		{
			try
			{
				return Directory.Exists(path);
			}
			catch
			{
				return false;
			}
		}

		public static bool IsFileAccessible(string path)
		{
			try
			{
				return File.Exists(path);
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

			var subDirs = await Task.Run(() => SafeGetDirs(FullPath));
			var files = await Task.Run(() => SafeGetFiles(FullPath));

			var childTasks = subDirs.Select(dir => Task.Run(async () =>
			{
				var node = new FileSystemNodeViewModel(dir, true, false, _configs, _uiDispatcherQueue, true);
				await node.InitAsync(node.FullPath, true);
				return node;
			})).Concat(files.Select(file => Task.Run( async () =>
			{
				var node = new FileSystemNodeViewModel(file, false, false, _configs, _uiDispatcherQueue, true);
				await node.InitAsync(node.FullPath, false);
				return node;
			})));

			var children = await Task.WhenAll(childTasks);

			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				Children.Clear();
				foreach (var item in children)
				{
					Children.Add(item);
				}

				var actualCount = Children.Count(c => !c.IsPlaceholder);
				ChildrenCountText = actualCount > 0 ? $"[{actualCount}]" : "[?]";
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