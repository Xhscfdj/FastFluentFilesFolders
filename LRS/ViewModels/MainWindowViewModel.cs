//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;
using LRS.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
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
		private IFileOperator _fileOperator;
		public MultiLanguageStringsViewModel ML { get; }
		public MainWindowViewModel(IIconProvider iconProvider, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcherQueue, Configs configs, IFileOperator fileOperator, MultiLanguageStringsViewModel ml)
		{
			AppConfigs = configs;
			_fileOperator = fileOperator;
			CurrentBreadcrumbPath = configs.HomePageFullPath;
			_uiDispatcherQueue = uiDispatcherQueue;
			_iconProvider = iconProvider;
			ML = ml;
			NavigateToPathCommand = new RelayCommand<string>(NavigateToPath);
			NavigateToSubFolderCommand = new RelayCommand<string>(NavigateToPath);
			GoBackCommand = new RelayCommand(GoBack);
			GoForwardCommand = new RelayCommand(GoForward);
			GoUpCommand = new RelayCommand(GoUp);
			if (configs.IconParallelLoadingCount != 0)
				IconLoadSemaphore = new(configs.IconParallelLoadingCount, configs.IconParallelLoadingCount);
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
			InitializePinnedShortcuts(configs, uiDispatcherQueue);
			if (Directory.Exists(configs.HomePageFullPath))
				NavigateToPath(configs.HomePageFullPath);
			else
				SelectedFolder = RootDirectories.FirstOrDefault();
		}
		[RelayCommand]
		private void testFunction()
		{
			Debug.WriteLine("[DebugButton] Pressed.");
			var folder = RootDirectories.FirstOrDefault(); // 取第一个驱动器
			_ = UpdateCurrentFolderContentAsync(folder);
			TestString = "Modified by testFunction.";
			Debug.WriteLine($"[DebugButton] CurrentFolderContent count is {CurrentFolderContent.Count}");
			foreach (var item in CurrentFolderContent)
			{
				Debug.WriteLine($"[DebugButton]Item: {item.Name}, Type is {item.NodeTypeName}");
			}
		}

		[RelayCommand]
		private async Task Copy(FileSystemNodeViewModel? item)
		{
			if (item == null) return;
			await _fileOperator.CopyToClipBoard(new[] { item.FullPath });
		}

		[RelayCommand]
		private async Task Cut(FileSystemNodeViewModel? item)
		{
			if (item == null) return;
			await _fileOperator.CopyToClipBoard(new[] { item.FullPath }, true);
		}

		[RelayCommand]
		private async Task Paste()
		{
			var (paths, isCut) = await _fileOperator.PasteClipboardFiles();
			if (paths == null || !paths.Any()) return;

			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			foreach (var srcPath in paths)
			{
				var name = Path.GetFileName(srcPath);
				var destPath = Path.Combine(destDir, name);
				destPath = GenerateUniquePath(destPath);
				if (isCut)
					await _fileOperator.MoveAsync(srcPath, destPath);
				else
					await _fileOperator.CopyToAsync(srcPath, destPath);

				bool isDir = Directory.Exists(destPath);
				var node = new FileSystemNodeViewModel(destPath, isDir, false, AppConfigs, _uiDispatcherQueue, false);
				_ = node.InitAsync(node.FullPath, isDir);
				await _uiDispatcherQueue.EnqueueAsync(() =>
				{
					CurrentFolderContent.Add(node);
					SelectedFolder?.Children.Add(node);
				});
			}
			BreadcrumbRefreshRequested?.Invoke();
		}

		private static string GenerateUniquePath(string destPath)
		{
			if (!File.Exists(destPath) && !Directory.Exists(destPath))
				return destPath;

			var dir = Path.GetDirectoryName(destPath) ?? "";
			var name = Path.GetFileNameWithoutExtension(destPath);
			var ext = Path.GetExtension(destPath);

			int index = 1;
			string newPath;
			do
			{
				var suffix = index == 1 ? "" : $" ({index})";
				newPath = Path.Combine(dir, $"{name} - 副本{suffix}{ext}");
			}
			while (File.Exists(newPath) || Directory.Exists(newPath));

			return newPath;
		}

		[RelayCommand]
		private async Task Delete(FileSystemNodeViewModel? item)
		{
			if (item == null) return;
			await _fileOperator.DeleteToRecycleBinAsync(item.FullPath);
			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				CurrentFolderContent.Remove(item);
				SelectedFolder?.Children.Remove(item);
			});
		}

		[RelayCommand]
		private async Task PermanentDelete(FileSystemNodeViewModel? item)
		{
			if (item == null) return;
			await _fileOperator.DeleteAsync(item.FullPath);
			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				CurrentFolderContent.Remove(item);
				SelectedFolder?.Children.Remove(item);
			});
		}

		[RelayCommand]
		private async Task Rename(FileSystemNodeViewModel? item)
		{
			if (item == null) return;
			CancelRename();
			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				item.IsRenaming = true;
				_renamingItem = item;
				RenameFocusRequested?.Invoke(item);
			});
		}

		private FileSystemNodeViewModel? _renamingItem;

		public event Action<FileSystemNodeViewModel>? RenameFocusRequested;

		public void CancelRename()
		{
			if (_renamingItem != null)
			{
				_renamingItem.IsRenaming = false;
				_renamingItem = null;
			}
		}

		public event Action? BreadcrumbRefreshRequested;

		public async Task CommitRenameAsync(FileSystemNodeViewModel item, string newName)
		{
			if (string.IsNullOrEmpty(newName)) return;
			try
			{
				await _fileOperator.RenameAsync(item.FullPath, newName);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Rename] Failed: {ex.Message}");
			}
			_renamingItem = null;
		}

		[RelayCommand]
		private async Task CopyPath(FileSystemNodeViewModel? item)
		{
			if (item == null) return;
			var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
			dataPackage.SetText(item.FullPath);
			Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
			await Task.CompletedTask;
		}

		[RelayCommand]
		private async Task OpenWith(FileSystemNodeViewModel? item)
		{
			if (item == null || item.IsDirectory) return;
			await Task.Run(() => ShowOpenWithDialog(item.FullPath));
		}

		internal static void ShowOpenWithDialog(string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
				return;

			var info = new OPENASINFO
			{
				pcszFile = Marshal.StringToHGlobalUni(filePath),
				pcszClass = IntPtr.Zero,
				oaifInFlags = OPEN_AS_INFO_FLAGS.OAIF_ALLOW_REGISTRATION | OPEN_AS_INFO_FLAGS.OAIF_EXEC
			};
			try
			{
				SHOpenWithDialog(IntPtr.Zero, ref info);
			}
			finally
			{
				Marshal.FreeHGlobal(info.pcszFile);
			}
		}

		[RelayCommand]
		private async Task Properties(FileSystemNodeViewModel? item)
		{
			if (item == null) return;
			await _uiDispatcherQueue.EnqueueAsync(() => ShowPropertiesDialog(item));
		}

		private async void ShowPropertiesDialog(FileSystemNodeViewModel item)
		{
			var panel = new StackPanel { Spacing = 12, Width = 420, Margin = new Thickness(0, 0, 0, 8) };

			var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
			if (item.Icon != null)
				header.Children.Add(new Image { Source = item.Icon, Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center });
			header.Children.Add(new TextBlock
			{
				Text = item.Name,
				FontSize = 18,
				FontWeight = FontWeights.SemiBold,
				TextTrimming = TextTrimming.CharacterEllipsis,
				VerticalAlignment = VerticalAlignment.Center
			});
			panel.Children.Add(header);

			panel.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"] });

			var propsGrid = new Grid { ColumnSpacing = 16, RowSpacing = 10 };
			propsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
			propsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			int row = 0;
			void AddRow(string label, string value)
			{
				propsGrid.RowDefinitions.Add(new RowDefinition());
				var lbl = new TextBlock
				{
					Text = label,
					Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
					FontSize = 13,
					VerticalAlignment = VerticalAlignment.Center,
					Margin = new Thickness(0, 0, 4, 0)
				};
				var val = new TextBlock
				{
					Text = value,
					Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
					FontSize = 13,
					TextWrapping = TextWrapping.Wrap,
					VerticalAlignment = VerticalAlignment.Center
				};
				Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
				Grid.SetRow(val, row); Grid.SetColumn(val, 1);
				propsGrid.Children.Add(lbl);
				propsGrid.Children.Add(val);
				row++;
			}

			var typeDesc = item.IsDirectory ? "文件夹"
				: item.Extension.Length > 0 ? $"{item.Extension.TrimStart('.')} 文件"
				: "文件";
			AddRow("类型:", typeDesc);
			AddRow("路径:", item.FullPath);
			AddRow("大小:", item.IsDirectory ? item.VisualSize : $"{item.VisualSize} ({item.ExactSize:N0} 字节)");
			AddRow("修改日期:", item.LastModifiedTimeString);
			AddRow("创建日期:", item.FirstCreatedTimeString);

			panel.Children.Add(propsGrid);

			var dialog = new ContentDialog
			{
				Title = "属性",
				Content = panel,
				CloseButtonText = "关闭",
				DefaultButton = ContentDialogButton.Close,
				XamlRoot = App.MainWindow.Content.XamlRoot
			};
			_ = dialog.ShowAsync();
		}

		[RelayCommand]
		private async Task NewFolder()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			AddNewItemToView(destDir, "新建文件夹", isDirectory: true);
			BreadcrumbRefreshRequested?.Invoke();
			await Task.CompletedTask;
		}

		[RelayCommand]
		private async Task NewTextDocument()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			AddNewItemToView(destDir, "新建文本文档.txt", isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
			await Task.CompletedTask;
		}

		[RelayCommand]
		private async Task NewShortcut()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			AddNewItemToView(destDir, "新建快捷方式.lnk", isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
			await Task.CompletedTask;
		}

		[RelayCommand]
		private async Task NewFile()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			AddNewItemToView(destDir, "新建文件", isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
			await Task.CompletedTask;
		}

		[RelayCommand]
		private async Task NewExcelSpreadsheet()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			AddNewItemToView(destDir, "新建Excel表格.xlsx", isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
			await Task.CompletedTask;
		}

		[RelayCommand]
		private async Task NewWordDocument()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			AddNewItemToView(destDir, "新建Word文档.docx", isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
			await Task.CompletedTask;
		}

		[RelayCommand]
		private async Task NewPowerPointPresentation()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			AddNewItemToView(destDir, "新建PPT演示.pptx", isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
			await Task.CompletedTask;
		}

		private void AddNewItemToView(string destDir, string defaultName, bool isDirectory)
		{
			var newPath = GenerateUniquePath(Path.Combine(destDir, defaultName));
			if (isDirectory)
				Directory.CreateDirectory(newPath);
			else
				File.Create(newPath).Dispose();

			var node = new FileSystemNodeViewModel(newPath, isDirectory, false, _appConfigs, _uiDispatcherQueue, false);
			_ = node.InitAsync(node.FullPath, isDirectory);
			_uiDispatcherQueue.TryEnqueue(() =>
			{
				CurrentFolderContent.Add(node);
				SelectedFolder?.Children.Add(node);
			});
		}

		private async Task RefreshCurrentFolder()
		{
			if (SelectedFolder != null)
			{
				await SelectedFolder.ReloadChildrenAsync();
				await UpdateCurrentFolderContentAsync(SelectedFolder);
				BreadcrumbRefreshRequested?.Invoke();
			}
		}

		[ObservableProperty] private string _testString = "hasn't changed";
		[ObservableProperty] private ObservableCollection<FileSystemNodeViewModel> _pinnedShortcuts = new();
		// 文件系统相关的属性和方法
		private readonly IIconProvider _iconProvider;
		//[ObservableProperty] private string[] _PathsForBreadcrumbBar = ["C:\\"];
		[ObservableProperty] private ObservableCollection<FileSystemNodeViewModel> _currentFolderContent = new();
		[ObservableProperty] private string _currentBreadcrumbPath = "C:\\";
		[ObservableProperty] private Configs? _appConfigs = null;
		[ObservableProperty] private bool _canGoBack;
		[ObservableProperty] private bool _canGoForward;
		[ObservableProperty] private bool _isSettingsOpen;

		public Microsoft.UI.Xaml.Visibility FileTableVisibility => IsSettingsOpen ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
		public Microsoft.UI.Xaml.Visibility SettingsVisibility => IsSettingsOpen ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
		partial void OnIsSettingsOpenChanged(bool value)
		{
			OnPropertyChanged(nameof(FileTableVisibility));
			OnPropertyChanged(nameof(SettingsVisibility));
		}
		public SemaphoreSlim IconLoadSemaphore = new(30, 30); // 最多30个并发
		private readonly Stack<string> _backStack = new();
		private readonly Stack<string> _forwardStack = new();
		private bool _isNavigatingFromHistory;
		private ObservableCollection<FileSystemNodeViewModel> _rootDirectories = new();
		public ObservableCollection<FileSystemNodeViewModel> RootDirectories
		{
			get => _rootDirectories;
			set => _rootDirectories = value;
		}
		private Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcherQueue;
		[ObservableProperty] private FileSystemNodeViewModel? _selectedFolder;

		public bool IsCurrentFolderSpecial => SelectedFolder?.IsSpecialFolder ?? false;

		partial void OnSelectedFolderChanged(FileSystemNodeViewModel? value)
		{
			Debug.WriteLine($"\n----Selected:{value?.Name}\n");
			Debug.WriteLine($"OnSelectedFolderChanged called with value: {value?.FullPath ?? "null"}");
			Debug.WriteLine($"Is UI thread? {_uiDispatcherQueue.HasThreadAccess}");
			if (value != null)
			{
				if (!_isNavigatingFromHistory && _previousPath != null && _previousPath != value.FullPath)
				{
					_backStack.Push(_previousPath);
					_forwardStack.Clear();
				}
				_previousPath = value.FullPath;
				CanGoBack = _backStack.Count > 0;
				CanGoForward = _forwardStack.Count > 0;
				_ = UpdateCurrentFolderContentAsync(value);
			}
			else
			{
				CurrentFolderContent.Clear();
			}
		}
		private string? _previousPath;

		public async Task UpdateCurrentFolderContentAsync(FileSystemNodeViewModel? folder)
		{
			if (folder == null)
			{
				_uiDispatcherQueue.TryEnqueue(() => CurrentFolderContent.Clear());
				return;
			}

			CancelRename();

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
				OnPropertyChanged(nameof(IsCurrentFolderSpecial));
			});
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
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct OPENASINFO
		{
			public IntPtr pcszFile;
			public IntPtr pcszClass;
			public OPEN_AS_INFO_FLAGS oaifInFlags;
		}

		[Flags]
		private enum OPEN_AS_INFO_FLAGS
		{
			OAIF_ALLOW_REGISTRATION = 0x00000001,
			OAIF_REGISTER_EXT = 0x00000002,
			OAIF_EXEC = 0x00000004,
			OAIF_FORCE_REGISTRATION = 0x00000008,
			OAIF_HIDE_REGISTRATION = 0x00000020,
			OAIF_URL_PROTOCOL = 0x00000040,
			OAIF_DEFAULT = 0x00000080,
			OAIF_FILE_IS_URI = 0x00000100
		}

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OPENASINFO poainfo);

		public static void OpenWithDefaultProgram(string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath))
				throw new ArgumentNullException(nameof(filePath));

			if (!File.Exists(filePath))
				throw new FileNotFoundException($"文件不存在: {filePath}");

			try
			{
				// 先尝试默认打开
				Process.Start(new ProcessStartInfo
				{
					FileName = filePath,
					UseShellExecute = true
				});
			}
			catch (Win32Exception ex) when (ex.NativeErrorCode == 1155) // 无关联程序
			{
				// 弹出“打开方式”对话框
				OPENASINFO info = new OPENASINFO
				{
					pcszFile = Marshal.StringToHGlobalUni(filePath),
					pcszClass = IntPtr.Zero,
					oaifInFlags = OPEN_AS_INFO_FLAGS.OAIF_EXEC
				};
				try
				{
					int hr = SHOpenWithDialog(IntPtr.Zero, ref info);
					if (hr < 0) // 失败
					{
						throw new InvalidOperationException($"无法显示“打开方式”对话框，错误码: {hr}");
					}
				}
				finally
				{
					Marshal.FreeHGlobal(info.pcszFile);
				}
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"打开文件失败: {ex.Message}", ex);
			}
		}
		public ICommand NavigateToPathCommand { get; }
		public ICommand NavigateToSubFolderCommand { get; }
		public ICommand GoBackCommand { get; }
		public ICommand GoForwardCommand { get; }
		public ICommand GoUpCommand { get; }

		private void NavigateToPath(string path)
		{
			var target = FindNodeByPath(path);
			if (target != null)
				SelectedFolder = target;
			else
				NavigateToNewPath(path);
		}

		private void NavigateToNewPath(string path)
		{
			if (!Directory.Exists(path)) return;
			var node = new FileSystemNodeViewModel(path, true, false, _appConfigs, _uiDispatcherQueue, false);
			_previousPath = null;
			SelectedFolder = node;
		}

		private void GoBack()
		{
			if (_backStack.Count == 0) return;
			_isNavigatingFromHistory = true;
			_forwardStack.Push(_previousPath ?? _selectedFolder?.FullPath ?? "");
			var path = _backStack.Pop();
			_previousPath = null;
			var target = FindNodeByPath(path);
			if (target != null)
				SelectedFolder = target;
			else
				NavigateToNewPath(path);
			CanGoBack = _backStack.Count > 0;
			CanGoForward = _forwardStack.Count > 0;
			_isNavigatingFromHistory = false;
		}

		private void GoForward()
		{
			if (_forwardStack.Count == 0) return;
			_isNavigatingFromHistory = true;
			_backStack.Push(_previousPath ?? _selectedFolder?.FullPath ?? "");
			var path = _forwardStack.Pop();
			_previousPath = null;
			var target = FindNodeByPath(path);
			if (target != null)
				SelectedFolder = target;
			else
				NavigateToNewPath(path);
			CanGoBack = _backStack.Count > 0;
			CanGoForward = _forwardStack.Count > 0;
			_isNavigatingFromHistory = false;
		}

		private void GoUp()
		{
			if (_selectedFolder == null) return;
			var parentPath = GetParentPath(_selectedFolder.FullPath);
			if (parentPath == null) return;
			NavigateToPath(parentPath);
		}

		private static string? GetParentPath(string path)
		{
			if (string.IsNullOrEmpty(path)) return null;
			if (path.EndsWith(":\\") || path == "\\\\")
				return null;
			if (path.StartsWith("\\\\"))
			{
				var parts = path.TrimEnd('\\').Split('\\');
				if (parts.Length <= 2) return null;
				return string.Join("\\", parts.Take(parts.Length - 1));
			}
			var parent = Directory.GetParent(path);
			return parent?.FullName;
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

		private void InitializePinnedShortcuts(Configs configs, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcherQueue)
		{
			var pinnedPaths = GetQuickAccessPinnedFolders();
			foreach (var path in pinnedPaths)
			{
				if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
				{
					var node = new FileSystemNodeViewModel(path, true, false, configs, uiDispatcherQueue, false);
					PinnedShortcuts.Add(node);
				}
			}
		}

		private static List<string> GetQuickAccessPinnedFolders()
		{
			var result = new List<string>();
			try
			{
				Type shellType = Type.GetTypeFromProgID("Shell.Application", true);
				dynamic shell = Activator.CreateInstance(shellType);
				dynamic quickAccess = shell.NameSpace("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}");
				if (quickAccess != null)
				{
					foreach (dynamic item in quickAccess.Items())
					{
						try
						{
							string? path = item.Path;
							if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
								result.Add(path);
						}
						catch { }
					}
				}
			}
			catch { }
            return result;
		}
	}
}
