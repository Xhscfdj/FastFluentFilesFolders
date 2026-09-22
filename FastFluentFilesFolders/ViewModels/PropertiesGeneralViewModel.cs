using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace FastFluentFilesFolders.ViewModels
{
	/// <summary>常规页 ViewModel：图标/名称/基本属性/属性修改与重命名。</summary>
	public partial class PropertiesGeneralViewModel : ObservableObject
	{
		private readonly FileSystemNodeViewModel _item;
		private readonly FileAttributes _originalAttributes;
		private readonly DispatcherQueue? _dispatcher;

		public MultiLanguageStringsViewModel ML => App.ML;

		// 标签文本（由 VM 提供，供 XAML 绑定）
		public string TypeLabelText => App.ML.Get("PropertiesType");
		public string DescriptionLabelText => App.ML.Get("PropertiesDescription");
		public string LocationLabelText => App.ML.Get("PropertiesLocation");
		public string SizeLabelText => App.ML.Get("PropertiesSize");
		public string SizeOnDiskLabelText => App.ML.Get("PropertiesSizeOnDisk");
		public string ContainsLabelText => App.ML.Get("PropertiesContains");
		public string CreatedLabelText => App.ML.Get("PropertiesCreated");
		public string ModifiedLabelText => App.ML.Get("PropertiesModified");
		public string AccessedLabelText => App.ML.Get("PropertiesAccessed");
		public string ProcessesLabelText => App.ML.Get("PropertiesProcesses");
		public string AttributesLabelText => App.ML.Get("PropertiesAttributes");
		public string ReadOnlyLabel => App.ML.Get("PropertiesReadOnly");
		public string HiddenLabel => App.ML.Get("PropertiesHidden");
		public string AdvancedLabel => App.ML.Get("PropertiesAdvanced");

		public ImageSource? Icon { get; }

		[ObservableProperty] private string name;
		[ObservableProperty] private string typeText = string.Empty;
		[ObservableProperty] private string description = string.Empty;
		[ObservableProperty] private string location = string.Empty;
		[ObservableProperty] private string size = string.Empty;
		[ObservableProperty] private string sizeOnDisk = string.Empty;
		[ObservableProperty] private string contains = string.Empty;
		[ObservableProperty] private string created = string.Empty;
		[ObservableProperty] private string modified = string.Empty;
		[ObservableProperty] private string accessed = string.Empty;
		[ObservableProperty] private string processes = string.Empty;
		[ObservableProperty] private bool hasProcessesInfo;
		[ObservableProperty] private string attributesSummary = string.Empty;
		[ObservableProperty] private bool isReadOnly;
		[ObservableProperty] private bool isHidden;
		[ObservableProperty] private bool hasDescription;
		[ObservableProperty] private bool isDirectory;

		public string? LastError { get; private set; }

		public PropertiesGeneralViewModel(FileSystemNodeViewModel item, FileAttributes originalAttributes)
		{
			_item = item;
			_originalAttributes = originalAttributes;
			Icon = item.Icon;
			Name = item.Name;
			IsDirectory = item.IsDirectory;
			TypeText = item.IsDirectory
				? App.ML.Get("PropertiesFolder")
				: item.Extension.Length > 0
					? $"{item.Extension.TrimStart('.')} {App.ML.Get("PropertiesFile")}"
					: App.ML.Get("PropertiesFile");

			if (!item.IsDirectory)
			{
				var desc = GetDescription(item.FullPath);
				if (!string.IsNullOrWhiteSpace(desc))
				{
					HasDescription = true;
					Description = desc;
				}
			}

			_dispatcher = DispatcherQueue.GetForCurrentThread();

			Location = GetParentPath(item);
			Size = item.VisualSize;
			SizeOnDisk = item.IsDirectory ? item.VisualSize : GetSizeOnDisk(item);

			if (item.IsDirectory)
				Contains = string.IsNullOrWhiteSpace(item.ChildrenCountText)
					? "-"
					: item.ChildrenCountText.Trim('[', ']');

			Created = item.FirstCreatedTimeString;
			Modified = item.LastModifiedTimeString;
			Accessed = ReadAccessTime(item.FullPath);

			item.PropertyChanged += OnItemPropertyChanged;
			if (!item.IsDirectory)
			{
				HasProcessesInfo = true;
				Processes = string.IsNullOrWhiteSpace(item.ProcessesUsingThisFile) ? "-" : item.ProcessesUsingThisFile;
			}
			else
			{
				// 文件夹大小在加载前默认 0B：属性窗口启动时立即触发后台计算。
				if (!item.IsSizeCalculated)
					_ = item.CalculateSizeCommand.ExecuteAsync(null);
				_ = CalculateFolderSizeOnDiskAsync();
			}

			AttributesSummary = FormatAttributes(_originalAttributes);
			IsReadOnly = _originalAttributes.HasFlag(FileAttributes.ReadOnly);
			IsHidden = _originalAttributes.HasFlag(FileAttributes.Hidden);
		}

		private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(FileSystemNodeViewModel.ProcessesUsingThisFile))
			{
				Processes = string.IsNullOrWhiteSpace(_item.ProcessesUsingThisFile) ? "-" : _item.ProcessesUsingThisFile;
			}
			else if (_item.IsDirectory &&
			         (e.PropertyName == nameof(FileSystemNodeViewModel.VisualSize) ||
			          e.PropertyName == nameof(FileSystemNodeViewModel.ExactSize)))
			{
				Size = _item.VisualSize;
			}
		}

		private async System.Threading.Tasks.Task CalculateFolderSizeOnDiskAsync()
		{
			try
			{
				var path = _item.FullPath;
				long diskBytes = await System.Threading.Tasks.Task.Run(() => CalculateAllocatedFolderSize(path));
				if (_dispatcher != null)
					_dispatcher.TryEnqueue(() => SizeOnDisk = FormatSizeWithBytes(diskBytes));
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Properties] Folder size-on-disk calculation failed: {ex.Message}");
			}
		}

		private static long CalculateAllocatedFolderSize(string fullPath)
		{
			long total = 0;
			try
			{
				foreach (var file in FileSystemNodeViewModel.SafeGetFiles(fullPath))
				{
					try
					{
						var low = GetCompressedFileSize(file, out uint high);
						total += ((long)high << 32) | low;
					}
					catch { }
				}
				foreach (var subDir in FileSystemNodeViewModel.SafeGetDirs(fullPath))
				{
					total += CalculateAllocatedFolderSize(subDir);
				}
			}
			catch { }
			return total;
		}

		private string FormatSizeWithBytes(long bytes)
		{
			if (bytes <= 0) return "-";
			return $"{FileSystemNodeViewModel.FormatFileSize(bytes)} ({string.Format(App.ML.Get("PropertiesBytesFmt"), bytes.ToString("N0"))})";
		}

		/// <summary>应用重命名与属性修改；失败返回 false 并填充 LastError。</summary>
		public bool ApplyCore()
		{
			LastError = null;
			if (!TryApplyRename(Name))
				return false;
			ApplyAttributes();
			return true;
		}

		private void ApplyAttributes()
		{
			var current = ReadAttributes(_item.FullPath);
			var desired = current;
			if (IsReadOnly) desired |= FileAttributes.ReadOnly;
			else desired &= ~FileAttributes.ReadOnly;
			if (IsHidden) desired |= FileAttributes.Hidden;
			else desired &= ~FileAttributes.Hidden;
			if (desired != current)
				File.SetAttributes(_item.FullPath, desired);
		}

		private bool TryApplyRename(string? newName)
		{
			if (string.IsNullOrWhiteSpace(newName))
				return true;

			newName = newName.Trim();
			if (newName == _item.Name)
				return true;

			if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			{
				LastError = App.ML.Get("PropertiesRenameFailed");
				return false;
			}

			try
			{
				var fullPath = _item.FullPath;
				var dir = Path.GetDirectoryName(fullPath);
				if (string.IsNullOrWhiteSpace(dir) || fullPath.TrimEnd('\\').Length <= 3)
					return true;

				var dest = Path.Combine(dir, newName);
				if (File.Exists(dest) || Directory.Exists(dest))
				{
					LastError = App.ML.Get("PropertiesRenameFailed");
					return false;
				}

				if (_item.IsDirectory)
					Directory.Move(fullPath, dest);
				else
					File.Move(fullPath, dest);

				_item.Name = newName;
				_item.FullPath = dest;
				Name = _item.Name;
				Debug.WriteLine($"[Properties] Renamed '{fullPath}' -> '{dest}'");
				return true;
			}
			catch (Exception ex)
			{
				LastError = ex.Message;
				return false;
			}
		}

		/// <summary>高级属性对话框使用的数据/写入（对话框本身在视图中展示）。</summary>
		public FileAttributes ReadCurrentAttributes() => ReadAttributes(_item.FullPath);

		public void ApplyAdvancedAttributes(FileAttributes current, bool archive, bool notIndexed)
		{
			try
			{
				var desired = current;
				if (archive) desired |= FileAttributes.Archive;
				else desired &= ~FileAttributes.Archive;
				if (notIndexed) desired |= FileAttributes.NotContentIndexed;
				else desired &= ~FileAttributes.NotContentIndexed;
				if (desired != current)
					File.SetAttributes(_item.FullPath, desired);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Properties] Advanced attributes apply failed: {ex.Message}");
			}
		}

		private static string GetDescription(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) return string.Empty;
			try
			{
				var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
				return string.IsNullOrWhiteSpace(info.FileDescription) ? string.Empty : info.FileDescription;
			}
			catch
			{
				return string.Empty;
			}
		}

		private string GetSizeOnDisk(FileSystemNodeViewModel item)
		{
			if (item.IsDirectory)
				return item.VisualSize;

			var low = GetCompressedFileSize(item.FullPath, out uint high);
			long sizeBytes = ((long)high << 32) | low;
			if (sizeBytes <= 0)
				return item.VisualSize;

			return $"{FileSystemNodeViewModel.FormatFileSize(sizeBytes)} ({string.Format(App.ML.Get("PropertiesBytesFmt"), sizeBytes.ToString("N0"))})";
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern uint GetCompressedFileSize(string lpFileName, out uint lpFileSizeHigh);

		private static string GetParentPath(FileSystemNodeViewModel item)
		{
			try
			{
				var path = item.FullPath.TrimEnd('\\');
				var parent = Path.GetDirectoryName(path);
				return string.IsNullOrWhiteSpace(parent) ? path : parent;
			}
			catch
			{
				return item.FullPath;
			}
		}

		private static string ReadAccessTime(string path)
		{
			try { return File.GetLastAccessTime(path).ToString("yyyy-MM-dd HH:mm:ss"); }
			catch { return "-"; }
		}

		private static FileAttributes ReadAttributes(string path)
		{
			try { return File.GetAttributes(path); }
			catch { return FileAttributes.Normal; }
		}

		private static string FormatAttributes(FileAttributes attributes)
		{
			var parts = new List<string>();
			if (attributes.HasFlag(FileAttributes.ReadOnly)) parts.Add("R");
			if (attributes.HasFlag(FileAttributes.Hidden)) parts.Add("H");
			if (attributes.HasFlag(FileAttributes.System)) parts.Add("S");
			if (attributes.HasFlag(FileAttributes.Archive)) parts.Add("A");
			if (attributes.HasFlag(FileAttributes.Directory)) parts.Add("D");
			if (attributes.HasFlag(FileAttributes.Compressed)) parts.Add("C");
			if (attributes.HasFlag(FileAttributes.Encrypted)) parts.Add("E");
			if (attributes.HasFlag(FileAttributes.NotContentIndexed)) parts.Add("I");
			return parts.Count > 0 ? string.Join(" ", parts) : "-";
		}
	}
}
