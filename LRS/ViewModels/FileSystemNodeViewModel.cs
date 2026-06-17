using CommunityToolkit.Mvvm.ComponentModel;
using LRS.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;

namespace LRS.ViewModels
{
    public partial class FileSystemNodeViewModel : ViewModelBase
    {
        private bool _isExpanded;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcherQueue;
        [ObservableProperty] public string _name = string.Empty;
        [ObservableProperty] public string _fullPath = string.Empty;
        [ObservableProperty] public bool _isDirectory;
        [ObservableProperty] public ObservableCollection<FileSystemNodeViewModel> _children = [];
        [ObservableProperty] public string _childrenCount = "?";
        [ObservableProperty] public ImageSource? _icon;
        [ObservableProperty] public IIconProvider _iconProvider;
        [ObservableProperty] public long _exactSize = 0;
        [ObservableProperty] public string _visualSize = "0B";
        [ObservableProperty] public DateTime _lastModifiedTime = new();
        [ObservableProperty] public DateTime _firstCreatedTime = new();
        [ObservableProperty] public string _extension = string.Empty;
        [ObservableProperty] public bool _isSelected = false;
        [ObservableProperty] public string _lastModifiedTimeString = "string.Empty";
        [ObservableProperty] public string _firstCreatedTimeString = "string.Empty";
		////////////////////////////////////////////////////////////////
		////////////////////////////////////////////////////////////////
		public async Task LoadIconAsync(bool isFolder, bool ifUseWin32API, string fullPath, DispatcherQueue dispatcherQueue)
		{
			if (IconProvider == null) return;

			IconProvider = ifUseWin32API ? new ShellIconHelper() : new WindowsIconProvider();
			ImageSource? icon = null;

			if (ifUseWin32API)
			{
				icon = await ((ShellIconHelper)IconProvider).GetIconAsync(fullPath, isFolder, dispatcherQueue, 32);
                if (icon == null)
                {
                    Debug.WriteLine("Icon is NULLLLLLLLLLL");
                }
			}
			else
			{
				icon = await ((WindowsIconProvider)IconProvider).GetIconAsync(fullPath, isFolder, dispatcherQueue);
			}

			if (_uiDispatcherQueue != null)
				_uiDispatcherQueue.TryEnqueue(() => Icon = icon);
		}

		public string NodeTypeName => this.GetType().Name;
        public FileSystemNodeViewModel(string fullPath, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            _uiDispatcherQueue = dispatcherQueue;
            // Name 和 ChildrenCount 需要在派生类构造器设置完属性后再计算，
            // 但为保证初始状态正确，先尝试基于 FullPath 计算 Name（若已设置），
            // 并将 ChildrenCount 的初始赋值保留给派生类来调用 GetChildrenCount()
            if (!string.IsNullOrEmpty(FullPath))
                _name = Path.GetFileName(FullPath);
        }
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

        public string GetChildrenCount()
        {
            if (ChildrenCount != "?") return ChildrenCount;
            string res = string.Empty;
            // 计算实际子项数，忽略占位符节点
            int count = 0;
            foreach (var c in Children)
            {
                if (c is not PlaceholderNodeViewModel)
                    count++;
            }

            // 如果只有占位符（尚未加载），尝试基于 FullPath 枚举实际子项数以便一开始就显示数量
            if (count == 0 && Children.Count == 1 && Children[0] is PlaceholderNodeViewModel && !string.IsNullOrEmpty(FullPath))
            {
                try
                {
                    int dirs = 0;
                    int files = 0;
                    try { dirs = Directory.GetDirectories(FullPath).Length; } catch { dirs = 0; }
                    try { files = Directory.GetFiles(FullPath).Length; } catch { files = 0; }
                    count = dirs + files;
                }
                catch
                {
                    count = 0;
                }
            }

            if (count > 0)
                res = $" [{count}]";

            return res;
        }
		public bool IsExpanded
		{
			get => _isExpanded;
			set
			{
				if (SetProperty(ref _isExpanded, value) && value)
				{
					OnExpanded(value);  // 触发虚方法
					Debug.WriteLine("6767676767676767676767");
					ChildrenCount = GetChildrenCount();
				}
			}
		}
		public static bool IsDirectoryAccessible(string path)
        {
            try
            {
                var dir = new DirectoryInfo(path);
                return dir.Exists;
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"无权访问文件夹: {path}, 错误: {ex.Message}");
                return false;
            }
            catch (DirectoryNotFoundException ex)
            {
                Debug.WriteLine($"文件夹不存在: {path}, 错误: {ex.Message}");
                return false;
            }
            catch (PathTooLongException)
            {
                return false;
            }
        }
        public static bool IsFileAccessible(string path)
        {
            try
            {
                FileInfo fileInfo = new(path);
                return fileInfo.Exists;
            }
            catch (UnauthorizedAccessException ex)
            {
                return false;
            }
            catch (FileNotFoundException ex)
            {
                Debug.WriteLine($"文件不存在: {path}, 错误: {ex.Message}");
                return false;
            }
            catch (PathTooLongException)
            {
                return false;
            }
        }
        protected virtual void OnExpanded(bool value) { }  // 基类空实现
    }
}