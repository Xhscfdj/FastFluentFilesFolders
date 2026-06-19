using Microsoft.UI.Xaml.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using LRS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Microsoft.UI.Xaml;

namespace LRS.ViewModels
{
    public partial class FolderNodeViewModel : FileSystemNodeViewModel
    {
		private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcherQueue;
        private readonly Configs _configs;
		[ObservableProperty] public ObservableCollection<FileSystemNodeViewModel> _children = [new PlaceholderNodeViewModel("")];
		[ObservableProperty] private int? _cachedChildrenCount;
        [ObservableProperty] private bool _isLoaded = false;
        [ObservableProperty] private string _childrenCountText = string.Empty;
        [ObservableProperty] private bool _isCounting = false;
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
                    dirs = SafeGetDirs(FullPath).Count;
                    files = SafeGetFiles(FullPath).Count;
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

		private void StartAsyncCount()
        {
            if (CachedChildrenCount.HasValue || IsCounting) return;
            IsCounting = true;
            Task.Run(async () =>
            {
                int count = 0;
                try
                {
                    // 仍然可能抛异常（权限问题），所以用 try-catch
                    count = SafeGetDirs(FullPath).Count +
                            SafeGetFiles(FullPath).Count;
                }
                catch { count = 0; }
                CachedChildrenCount = count;
                _uiDispatcherQueue.TryEnqueue(() =>
                {
                    // 更新显示文本（注意：如果当前 Children 中只有占位符，显示真实计数）
                    if (Children.Count == 1 && Children[0] is PlaceholderNodeViewModel)
                    {
                        ChildrenCountText = count > 0 ? $" [{count}]" : string.Empty;
                    }
                    else
                    {
                        // 如果已经加载过真实子项，则根据当前 Children 中非占位符的数量显示
                        var actualCount = Children.Count(c => c is not PlaceholderNodeViewModel);
                        ChildrenCountText = actualCount > 0 ? $" [{actualCount}]" : string.Empty;
                    }
                });
            });
        }
        public FolderNodeViewModel(string fullPath, IIconProvider iconProvider, Configs configs,
            Microsoft.UI.Dispatching.DispatcherQueue uiDispatcherQueue) : base(fullPath, uiDispatcherQueue)
		{
			_configs = configs;
			//Children.Add(new PlaceholderNodeViewModel("", null));
			ArgumentNullException.ThrowIfNull(iconProvider);
			_uiDispatcherQueue = uiDispatcherQueue;
            FullPath = fullPath;
            Name = fullPath.Length == 3 && fullPath.Contains(":\\") ? fullPath : Path.GetFileName(fullPath);
            IconProvider = iconProvider;
            _ = LoadIconAsync(true, _configs.ifUsesWin32APIToGetIcon, FullPath, uiDispatcherQueue);
            StartAsyncCount();
            //_ = LoadFolderInfoAsync(fullPath);
            LoadFolderInfoSync(fullPath);

        }
        public async Task LoadFolderInfoAsync(string fullPath)
        {
			if (!(fullPath == $"{fullPath[0]}:\\"))
			{
				DirectoryInfo dirInfo = new(fullPath);
				//ExactSize = GetDirSize(fullPath);
				//VisualSize = FormatFileSize(ExactSize);
				//Debug.WriteLine($"File: {fullPath}, Visual size: {VisualSize}, Exact size: {ExactSize}", "Visual size setted.");
				//Debug.WriteLine($"File: {fullPath}, Visual size: {VisualSize}, Exact size: {ExactSize}", "Visual size setted.");
				LastModifiedTime = dirInfo.LastWriteTimeUtc;
				FirstCreatedTime = dirInfo.CreationTimeUtc;
				LastModifiedTimeString = LastModifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
				FirstCreatedTimeString = FirstCreatedTime.ToString("yyyy-MM-dd HH:mm:ss");
				Debug.WriteLine($"File: {fullPath} completely loaded.\nVisual size: {VisualSize}\nExact size: {ExactSize}\nLast modified: {LastModifiedTimeString}\nFirst created: {FirstCreatedTimeString}\nFile info loaded.");
			}
		}
        protected override void OnExpanded(bool value)
        {
            if (!value) return; // 只在展开时处理

            if (IsLoaded) return; // 已加载则不再触发

            _ = LoadChildrenAsync();
            IsLoaded = true; // 标记为已加载，避免重复加载
        }

        public async Task LoadChildrenAsync()
        {
			Debug.WriteLine("aetqpwotieywopqtyweoiqytiowqypyetqywpeoiytqwpyeipytowpqyeiwtopqyweoptyq");
			if (IsLoaded) return;
            IsLoaded = true;
			var subDirs = SafeGetDirs(FullPath);
			var files = SafeGetFiles(FullPath);

			if (_uiDispatcherQueue != null && Application.Current != null)
            {

				_uiDispatcherQueue.TryEnqueue(() =>
				{
					Debug.WriteLine("================================================");
					Children.Clear();
                    foreach (var dir in subDirs)
                        Children.Add(new FolderNodeViewModel(dir, IconProvider, _configs, _uiDispatcherQueue));
                    foreach (var file in files)
                        Children.Add(new FileNodeViewModel(file, IconProvider, _configs, _uiDispatcherQueue));
					//Children.AddRange(subDirs.Select(d => new FolderNodeViewModel(d, IconProvider, _configs, _uiDispatcherQueue)));
					//Children.AddRange(files.Select(f => new FileNodeViewModel(f, IconProvider, _configs, _uiDispatcherQueue)));

					var actualCount = Children.Count(c => c is not PlaceholderNodeViewModel);
					ChildrenCountText = actualCount > 0 ? $" [{actualCount}]" : string.Empty;
				});
                }
            //await Task.Run(() => { });
        }

        public static List<string> SafeGetFiles(string path)
        {
            var accessible = new List<string>();

            string[] allFiles;
            try
            {
                allFiles = Directory.GetFiles(path);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine(ex.Message);
                return accessible;
            }
            catch (DirectoryNotFoundException ex)
            {
                Debug.WriteLine(ex.Message);
                return accessible;
            }
            foreach (string file in allFiles)
            {
                if (IsFileAccessible(file))
                    accessible.Add(file);
            }

            return accessible;

        }
        public static List<string> SafeGetDirs(string path)
        {
            var accessible = new List<string>();
            string[] allSubDirs;

            try
            {
                allSubDirs = Directory.GetDirectories(path);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine(ex.Message);
                return accessible;
            }
            catch (DirectoryNotFoundException ex)
            {
                Debug.WriteLine(ex.Message);
                return accessible;
            }

            foreach (string subDir in allSubDirs)
            {
                if (IsDirectoryAccessible(subDir))
                    accessible.Add(subDir);
            }

            return accessible;
        }


        public static long GetDirSize(string path)
        {
            if (!Directory.Exists(path))
                return 0;

            long totalSize = 0;

            try
            {
                // 计算当前目录下所有文件的大小
                var files = Directory.EnumerateFiles(path);
                totalSize += files.Sum(file => new FileInfo(file).Length);

                // 递归处理子目录
                var subDirs = Directory.EnumerateDirectories(path);
                foreach (var subDir in subDirs)
                {
                    totalSize += GetDirSize(subDir);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 忽略无法访问的目录或文件，并继续
            }
            catch (PathTooLongException)
            {
                // 路径过长时跳过
            }
            catch (IOException)
            {
                // I/O 异常时跳过
            }

            return totalSize;
        }
		// Functions for debug.
		private void LoadFolderInfoSync(string fullPath)
		{
			if (!(fullPath == $"{fullPath[0]}:\\"))
			{
				DirectoryInfo dirInfo = new(fullPath);
				LastModifiedTime = dirInfo.LastWriteTimeUtc;
				FirstCreatedTime = dirInfo.CreationTimeUtc;
				LastModifiedTimeString = LastModifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
				FirstCreatedTimeString = FirstCreatedTime.ToString("yyyy-MM-dd HH:mm:ss");
			}
		}
	}
}