using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;
using FastFluentFilesFolders.Helpers;
using FastFluentFilesFolders.Models;
using FastFluentFilesFolders.Services;
using FastFluentFilesFolders.Views;
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

namespace FastFluentFilesFolders.ViewModels
{
	public partial class MainWindowViewModel : ViewModelBase
	{
		private IFileOperator _fileOperator;
		private PropertiesWindow? _propertiesWindow;
		public MultiLanguageStringsViewModel ML { get; }
		public MainWindowViewModel(IIconProvider iconProvider, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcherQueue, Configs configs, IFileOperator fileOperator, MultiLanguageStringsViewModel ml)
		{
			AppConfigs = configs;
			_fileOperator = fileOperator;
			CurrentBreadcrumbPath = configs.HomePageFullPath;
			_uiDispatcherQueue = uiDispatcherQueue;
			_iconProvider = iconProvider;
			ML = ml;

			// 初始标签页
			var firstTab = new ExplorerTab { Title = GetTabTitle(configs.HomePageFullPath) };
			firstTab.SearchResults = SearchResults;
			Tabs.Add(firstTab);
			SelectedTab = firstTab;

			NavigateToPathCommand = new RelayCommand<string>(NavigateToPath);
			NavigateToSubFolderCommand = new RelayCommand<string>(NavigateToPath);
			GoBackCommand = new RelayCommand(GoBack);
			GoForwardCommand = new RelayCommand(GoForward);
			GoUpCommand = new RelayCommand(GoUp);
			// 把图标解码并发上限接入实际生效的信号量：0 = 自动（安全上限 16，避免并发过高偶发失败），
			// >0 = 按配置限流；避免大文件夹进入时图标逐项“轮流替换”造成拖沓感
			FileSystemNodeViewModel.ConfigureIconLoadConcurrency(configs.IconParallelLoadingCount);
			if (configs.IconParallelLoadingCount > 0)
				IconLoadSemaphore = new(configs.IconParallelLoadingCount, configs.IconParallelLoadingCount);

			// 侧栏根：此电脑(含全部磁盘) → 网络 → Linux(WSL) → 回收站 → 云盘
			BuildSidebarRoots(configs, uiDispatcherQueue);
			StartDriveWatcher();
			// 关键：驱动器枚举/卷标读取绝不能在 UI 线程做——U 盘/坏盘上 IsReady/VolumeLabel
			// 可能一直阻塞，导致窗口在拔盘前都出不来。
			_ = InitializeDriveCacheAsync();

			// 注意：此处不要立即选中“此电脑”。构造函数执行期间 App.SharedViewModel 尚未赋值，
			// 会令“此电脑”子项（磁盘）枚举拿到空快照并被标记为已加载，之后一直显示为空。
			// 首次选中改到 DeferredInitializeAsync（App.SharedViewModel 已就绪）后执行。
		}

		// ======================= 侧栏位置 =======================
		private FileSystemNodeViewModel? _thisPcNode;
		private readonly object _driveLock = new();
		private readonly List<FileSystemNodeViewModel> _driveNodes = new();
		private readonly HashSet<string> _knownDriveRoots = new(StringComparer.OrdinalIgnoreCase);
		private Microsoft.UI.Dispatching.DispatcherQueueTimer? _driveTimer;

		// 驱动器刷新：同一时刻只允许一个刷新任务（防止 U 盘卡住时每 2 秒堆积一个卡死线程）
		private int _driveRefreshBusy;

		// 已知“探测很慢/卡住”的盘：短时间内不再重复探测，直接用类型名回退显示
		private readonly Dictionary<string, DateTime> _slowDriveProbeUtc = new(StringComparer.OrdinalIgnoreCase);
		private static readonly TimeSpan DriveProbeTimeout = TimeSpan.FromSeconds(2);
		private static readonly TimeSpan SlowDriveRetryInterval = TimeSpan.FromSeconds(30);

		private sealed record DriveEntry(string Root, string DisplayName);

		private void BuildSidebarRoots(Configs configs, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcherQueue)
		{
			// 此电脑（所有磁盘的入口）
			_thisPcNode = FileSystemNodeViewModel.CreateVirtualRoot(
				FileSystemNodeViewModel.ThisPcClsidPath, ML.TreeThisPC, FileNodeKind.ThisPc, configs, uiDispatcherQueue);
			_thisPcNode.Parent = null;

			var roots = new List<FileSystemNodeViewModel> { _thisPcNode };

			// 网络
			roots.Add(FileSystemNodeViewModel.CreateVirtualRoot(
				FileSystemNodeViewModel.NetworkClsidPath, ML.TreeNetwork, FileNodeKind.Network, configs, uiDispatcherQueue));

			// Linux（WSL：\wsl$ / \wsl.localhost）
			var wslRoot = Services.ShellLocations.PickWslRootPath();
			roots.Add(FileSystemNodeViewModel.CreateVirtualRoot(
				wslRoot, ML.TreeLinux, FileNodeKind.Wsl, configs, uiDispatcherQueue));

			// 回收站（叶子节点：内容在表格中查看）
			roots.Add(FileSystemNodeViewModel.CreateVirtualRoot(
				FileSystemNodeViewModel.RecycleBinClsidPath, ML.TreeRecycleBin, FileNodeKind.RecycleBin,
				configs, uiDispatcherQueue, keepPlaceholder: false));

			// 云盘（自动检测常见云盘；一个都没有时整组隐藏）
			var cloudRoots = Services.CloudDriveDetector.DetectCloudRoots();
			if (cloudRoots.Count > 0)
			{
				var cloudGroup = FileSystemNodeViewModel.CreateVirtualRoot(
					FileSystemNodeViewModel.CloudGroupPath, ML.TreeCloudDrives, FileNodeKind.CloudGroup, configs, uiDispatcherQueue);
				// 用检测到的首个云盘目录图标作为分组图标（如 OneDrive 云图标）
				cloudGroup.IconSourcePath = Services.ShellIconHelper.BuildShellItemIconPath(cloudRoots[0].Path);
				roots.Add(cloudGroup);
			}

			RootDirectories = new System.Collections.ObjectModel.ObservableCollection<FileSystemNodeViewModel>(roots);
		}

		/// <summary>
		/// 刷新驱动器缓存。只能从后台线程调用：U 盘/坏盘上的 IsReady/VolumeLabel
		/// 可能长时间阻塞，放在 UI 线程会导致窗口在拔盘前都无法显示。
		/// </summary>
		private void RefreshDriveCache()
		{
			// 保持既有驱动器节点实例不变（树/表格引用了它们），只做 增/删/改名 差异
			var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var entry in EnumerateDriveEntries(k => ML[k]))
				desired[entry.Root] = entry.DisplayName;

			lock (_driveLock)
			{
				// 移除已消失的盘
				for (int i = _driveNodes.Count - 1; i >= 0; i--)
				{
					if (!desired.ContainsKey(_driveNodes[i].FullPath))
						_driveNodes.RemoveAt(i);
				}

				// 新增盘 + 卷标变化时更新显示名
				foreach (var kv in desired)
				{
					var existing = _driveNodes.FirstOrDefault(n => string.Equals(n.FullPath, kv.Key, StringComparison.OrdinalIgnoreCase));
					if (existing == null)
					{
						_driveNodes.Add(FileSystemNodeViewModel.CreateDriveNode(kv.Key, kv.Value, AppConfigs!, _uiDispatcherQueue));
					}
					else if (!string.Equals(existing.Name, kv.Value, StringComparison.Ordinal))
					{
						existing.Name = kv.Value;
					}
				}

				_driveNodes.Sort((a, b) => string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase));
				_knownDriveRoots.Clear();
				_knownDriveRoots.UnionWith(desired.Keys);
			}
		}

		/// <summary>
		/// 枚举驱动器。每个盘的 IsReady/VolumeLabel 探测单独限时（2 秒），
		/// 超时或失败的盘用“类型名 (X:)”降级显示，避免一块坏盘拖死整个列表。
		/// </summary>
		private List<DriveEntry> EnumerateDriveEntries(Func<string, string> ml)
		{
			var entries = new List<DriveEntry>();
			DriveInfo[] drives;
			try
			{
				drives = DriveInfo.GetDrives();
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[DriveWatcher] 枚举驱动器失败: {ex.Message}");
				return entries;
			}

			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var drive in drives)
			{
				try
				{
					var root = drive.RootDirectory.FullName;
					if (!seen.Add(root)) continue;

					bool skipProbe;
					lock (_driveLock)
					{
						skipProbe = _slowDriveProbeUtc.TryGetValue(root, out var at) &&
						            DateTime.UtcNow - at < SlowDriveRetryInterval;
					}

					string? displayName = skipProbe ? null : ProbeDriveDisplayName(drive, ml, root);
					if (displayName == null)
					{
						if (!skipProbe)
						{
							lock (_driveLock) _slowDriveProbeUtc[root] = DateTime.UtcNow;
						}
						displayName = BuildFallbackDriveName(drive, root, ml);
					}
					else
					{
						lock (_driveLock) _slowDriveProbeUtc.Remove(root);
					}

					entries.Add(new DriveEntry(root, displayName));
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"[DriveWatcher] 读取驱动器信息失败: {ex.Message}");
				}
			}
			return entries;
		}

		private string? ProbeDriveDisplayName(DriveInfo drive, Func<string, string> ml, string root)
		{
			string? displayName = null;
			var probe = Task.Run(() =>
			{
				try
				{
					if (!drive.IsReady) return;
					displayName = Services.ShellLocations.FormatDriveDisplayName(drive, ml);
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"[DriveWatcher] 读取驱动器信息失败 {root}: {ex.Message}");
				}
			});

			if (probe.Wait(DriveProbeTimeout))
				return displayName;

			Debug.WriteLine($"[DriveWatcher] 驱动器探测超时，降级显示: {root}");
			return null;
		}

		private static string BuildFallbackDriveName(DriveInfo drive, string root, Func<string, string> ml)
		{
			string key;
			try
			{
				key = drive.DriveType switch
				{
					DriveType.Fixed => "Drive.LocalDisk",
					DriveType.Removable => "Drive.Removable",
					DriveType.Network => "Drive.Network",
					DriveType.CDRom => "Drive.CD",
					DriveType.Ram => "Drive.Ram",
					_ => "Drive.Unknown"
				};
			}
			catch
			{
				key = "Drive.Unknown";
			}

			var letter = root.TrimEnd('\\', '/');
			return $"{ml(key)} ({letter})";
		}

		private bool TryBeginDriveRefresh() => Interlocked.CompareExchange(ref _driveRefreshBusy, 1, 0) == 0;

		private void EndDriveRefresh() => Interlocked.Exchange(ref _driveRefreshBusy, 0);

		/// <summary>启动时的首次驱动器枚举：全程后台执行，完成后回 UI 线程补齐“此电脑”子项。</summary>
		private async Task InitializeDriveCacheAsync()
		{
			if (_thisPcNode == null || _uiDispatcherQueue == null) return;
			if (!TryBeginDriveRefresh()) return;

			try
			{
				var added = await Task.Run(() =>
				{
					RefreshDriveCache();
					lock (_driveLock) return _knownDriveRoots.ToList();
				});

				if (added.Count == 0) return;
				await _uiDispatcherQueue.EnqueueAsync(() => ApplyDriveDiffOnUi(added, new List<string>()));
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[DriveWatcher] 初始化驱动器失败: {ex.Message}");
			}
			finally
			{
				EndDriveRefresh();
			}
		}

		/// <summary>供目录树节点取当前全部驱动器（同一批实例，避免刷新时反复重建）。</summary>
		public IReadOnlyList<FileSystemNodeViewModel> DriveNodeSnapshot()
		{
			lock (_driveLock)
				return _driveNodes.ToList();
		}

		public FileSystemNodeViewModel? GetDriveNodeByPath(string rootPath)
		{
			lock (_driveLock)
				return _driveNodes.FirstOrDefault(n => string.Equals(n.FullPath, rootPath, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>U盘/光盘等插入、拔出时的简单轮询监听（每 2 秒对比一次盘符集合）。</summary>
		private void StartDriveWatcher()
		{
			if (_driveTimer != null || _uiDispatcherQueue == null) return;
			_driveTimer = _uiDispatcherQueue.CreateTimer();
			_driveTimer.Interval = TimeSpan.FromSeconds(2);
			_driveTimer.Tick += (_, _) => { _ = PollDrivesChangedAsync(); };
			_driveTimer.Start();
		}

		private async Task PollDrivesChangedAsync()
		{
			// 单飞：若上一轮刷新仍卡在某块盘上，本轮直接跳过，避免每 2 秒堆积一个卡死线程
			if (!TryBeginDriveRefresh()) return;

			try
			{
				// IsReady/VolumeLabel 对不健康网络盘/U 盘可能阻塞，轮询放到后台线程
				var previous = new HashSet<string>(_knownDriveRoots, StringComparer.OrdinalIgnoreCase);
				var added = new List<string>();
				var removed = new List<string>();
				await Task.Run(() =>
				{
					RefreshDriveCache();
					lock (_driveLock)
					{
						added.Clear();
						removed.Clear();
						added.AddRange(_knownDriveRoots.Except(previous));
						removed.AddRange(previous.Except(_knownDriveRoots));
					}
				});

				if (added.Count == 0 && removed.Count == 0) return;
				if (_thisPcNode == null || _uiDispatcherQueue == null) return;

				await _uiDispatcherQueue.EnqueueAsync(() => ApplyDriveDiffOnUi(added, removed));
			}
			finally
			{
				EndDriveRefresh();
			}
		}

		private void ApplyDriveDiffOnUi(List<string> added, List<string> removed)
		{
			// 已展开过“此电脑”时同步树子节点
			var driveNodes = DriveNodeSnapshot();
			foreach (var d in driveNodes)
				d.Parent = _thisPcNode;

			if (_thisPcNode!.IsLoaded)
			{
				foreach (var n in driveNodes)
				{
					if (!_thisPcNode.Children.Contains(n))
						_thisPcNode.Children.Add(n);
				}
				foreach (var r in removed)
				{
					var gone = _thisPcNode.Children.FirstOrDefault(c => string.Equals(c.FullPath, r, StringComparison.OrdinalIgnoreCase));
					if (gone != null)
						_thisPcNode.Children.Remove(gone);
				}
			}

			// 当前正停留的盘被拔出时退回“此电脑”（含盘下任意深度的子路径/独立节点）
			if (removed.Count > 0)
			{
				var sel = SelectedFolder;
				while (sel != null && sel != _thisPcNode)
				{
					if (sel.NodeKind == FileNodeKind.Drive &&
						removed.Any(r => string.Equals(r, sel.FullPath, StringComparison.OrdinalIgnoreCase)))
					{
						SelectedFolder = _thisPcNode;
						break;
					}
					sel = sel.Parent;
				}

				if (SelectedFolder != null && SelectedFolder != _thisPcNode &&
					removed.Any(r => SelectedFolder.FullPath.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
				{
					SelectedFolder = _thisPcNode;
				}
			}

			// 表格正在展示“此电脑”时刷新表格内容
			if (ReferenceEquals(SelectedFolder, _thisPcNode))
			{
				_displayedFolderNode = null;
				_ = UpdateCurrentFolderContentAsync(_thisPcNode, version: null);
			}
		}

		public async Task DeferredInitializeAsync()
		{
			try
			{
				// 所有磁盘存在性检查都放后台：U 盘/网络盘上 Directory.Exists 可能长时间阻塞，
				// 放在 UI 线程会让启动界面卡在加载遮罩。
				var pinnedPaths = await Task.Run(() =>
					QuickAccessHelper.GetPinnedFolderPaths()
						.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
						.ToList());
				var startPath = await Task.Run(GetStartupPath);

				await _uiDispatcherQueue.EnqueueAsync(() =>
				{
					foreach (var path in pinnedPaths)
						PinnedShortcuts.Add(new FileSystemNodeViewModel(path, true, false, AppConfigs, _uiDispatcherQueue, true));

					if (!string.IsNullOrEmpty(startPath))
						NavigateToPath(startPath);
					else if (RootDirectories.Count > 0)
						SelectedFolder = RootDirectories[0];
				});
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[DeferredInit] Failed: {ex.Message}");
			}
			finally
			{
				await _uiDispatcherQueue.EnqueueAsync(() => IsReady = true);
			}
		}

		/// <summary>
		/// 获取启动时应导航到的路径：优先上次访问路径，回退到首页路径
		/// </summary>
		private string GetStartupPath()
		{
			if (!string.IsNullOrEmpty(AppConfigs.LastVisitedPath) && Directory.Exists(AppConfigs.LastVisitedPath))
				return AppConfigs.LastVisitedPath;
			if (!string.IsNullOrEmpty(AppConfigs.HomePageFullPath) && Directory.Exists(AppConfigs.HomePageFullPath))
				return AppConfigs.HomePageFullPath;
			return string.Empty;
		}
		[RelayCommand]
		private void testFunction()
		{
			Debug.WriteLine("[DebugButton] Pressed.");
			var folder = RootDirectories.FirstOrDefault(); // 取第一个驱动器
			_ = UpdateCurrentFolderContentAsync(folder, version: null);
			TestString = "Modified by testFunction.";
			Debug.WriteLine($"[DebugButton] CurrentFolderContent count is {CurrentFolderContent.Count}");
			foreach (var item in CurrentFolderContent)
			{
				Debug.WriteLine($"[DebugButton]Item: {item.Name}, Type is {item.NodeTypeName}");
			}
		}

		/// <summary>把操作岛条目交给页面（FileOperationReporter 已在 MiddleFilesView 订阅）。</summary>
		private void ReportOperationToIsland(FileOperationItem item)
		{
			_uiDispatcherQueue.TryEnqueue(() => FileOperationReporter.ReportOperation(item));
		}

		private FileOperationItem CreateReportedOperation(string text, string iconGlyph, int fileCount = 0)
		{
			var item = new FileOperationItem
			{
				Text = text,
				IconGlyph = iconGlyph,
				FileCount = fileCount,
				Progress = 0,
				Process = "0%",
				RemainTime = "...",
				SizeText = "0 B",
				State = FileOperationState.InProgress
			};
			ReportOperationToIsland(item);
			return item;
		}

		private void ReportClipboardFailure(string operationLabel)
		{
			var item = new FileOperationItem
			{
				Text = $"{operationLabel} {ML.FileOpFailed}",
				FileCount = 0,
				Progress = 0,
				Process = ML.FileOpFailed,
				RemainTime = "0",
				SizeText = "0 B",
				State = FileOperationState.Error,
				IconGlyph = "\uE74D",
				ErrorMessage = ML.FileOpClipboardFailed
			};
			ReportOperationToIsland(item);
		}

		[RelayCommand]
		private async Task Copy(IReadOnlyList<FileSystemNodeViewModel>? items)
		{
			if (items == null || items.Count == 0) return;
			// 压缩包内部条目、虚拟位置等没有真实可复制的磁盘路径，直接不处理
			if (items.Any(i => !IsWritableItem(i))) return;

			int added = await _fileOperator.CopyToClipBoard(items.Select(i => i.FullPath));
			if (added == 0)
			{
				ReportClipboardFailure(ML.CmdCopy);
				return;
			}
			await _uiDispatcherQueue.EnqueueAsync(ClearCutPending);
		}

		[RelayCommand]
		private async Task Cut(IReadOnlyList<FileSystemNodeViewModel>? items)
		{
			if (items == null || items.Count == 0) return;
			// 搜索视图里的“剪切”来源目录不明确，禁用；虚拟位置/压缩包条目同样禁用。
			if (IsSearchMode) return;
			if (items.Any(i => !IsWritableItem(i))) return;

			// 先尝试把新内容真正写入剪贴板，成功后才更新“待剪切”标记，
			// 避免写入失败时界面出现半透明但剪贴板内容不是这些文件的状态。
			int added = await _fileOperator.CopyToClipBoard(items.Select(i => i.FullPath), cut: true);
			if (added == 0)
			{
				ReportClipboardFailure(ML.CmdCut);
				return;
			}

			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				ClearCutPending();
				_cutItems = items.ToList();
				foreach (var item in _cutItems)
					item.IsCutPending = true;
			});
		}

		private List<FileSystemNodeViewModel> _cutItems = new();

		private void ClearCutPending()
		{
			foreach (var item in _cutItems)
				item.IsCutPending = false;
			_cutItems.Clear();
		}

		/// <summary>仅清除“已真正移动成功”的剪切项（批量粘贴部分失败时保留失败项的待剪切标记）。</summary>
		private void ClearCutPendingFor(FileSystemNodeViewModel item)
		{
			if (_cutItems.Remove(item))
				item.IsCutPending = false;
		}

		[RelayCommand]
		private async Task Paste(FileOperationItem? op)
		{
			FileOperationItem? pasteOp = null;
			await _pasteLock.WaitAsync();
			try
			{
				var targetOverride = _pasteTargetOverride;
				_pasteTargetOverride = null;

				// M4 防护：搜索视图目标不明确；回收站/此电脑/网络/压缩包内部等虚拟位置不可写入。
				if (IsSearchMode) return;
				var destFolderPath = targetOverride ?? SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
				if (!IsWritableFolderPath(destFolderPath)) return;

				var (paths, isCut) = await _fileOperator.PasteClipboardFiles();
				if (paths == null || !paths.Any())
				{
					// 空剪贴板/无有效内容：不再伪造一张“成功”卡。
					if (op != null)
						FailOperation(op, new InvalidOperationException(ML.FileOpClipboardEmpty));
					return;
				}

				var pathList = paths.ToList();
				pasteOp = op ?? CreateReportedOperation(ML.CmdPaste, "\uE77F");
				_uiDispatcherQueue.TryEnqueue(() => pasteOp.IconGlyph = isCut ? "\uE8AB" : "\uE8C8");

				// 目标路径必须为绝对路径（支持地址栏输入 ../xxx 之类的相对路径后粘贴）
				var destDir = targetOverride ?? SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
				if (!string.IsNullOrEmpty(destDir))
				{
					if (SelectedFolder != null && !Path.IsPathRooted(destDir))
						destDir = Path.GetFullPath(Path.Combine(SelectedFolder.FullPath, destDir));
					else
						destDir = Path.GetFullPath(destDir);
				}
				if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
				{
					FailOperation(pasteOp, new DirectoryNotFoundException($"目标文件夹不存在: {destDir}"));
					return;
				}

				// 统计待粘贴项的文件总数与总大小，让操作岛显示真实的文件个数与大小
				var (totalFiles, totalBytes) = await _fileOperator.GetTransferStatsAsync(pathList);
				UpdateOperationProgress(pasteOp, 0, totalFiles, 0, totalBytes);

				// 剪切并粘贴回“全部来源都在同一目标目录”：本质是原地粘贴，不产生移动。
				if (isCut && _cutItems.Count > 0 &&
					_cutItems.All(i =>
					{
						var srcDir = Path.GetDirectoryName(i.FullPath) ?? "";
						return string.Equals(srcDir, destDir, StringComparison.OrdinalIgnoreCase);
					}))
				{
					await _uiDispatcherQueue.EnqueueAsync(() => ClearCutPending());
					CompleteOperation(pasteOp, totalFiles, totalBytes);
					return;
				}

				// 先规划每一项的目标路径并检测同名冲突（剪切才弹确认框；复制维持自动改名）。
				var plans = BuildPastePlans(pathList, destDir, isCut);
				var conflictPlans = plans.Where(p => p.Conflict).ToList();
				if (isCut && conflictPlans.Count > 0)
				{
					var conflictNames = conflictPlans
						.Select(p => p.Name)
						.Distinct(StringComparer.CurrentCultureIgnoreCase)
						.ToList();
					var policy = await ResolveFileConflictsAsync(conflictNames);
					ApplyConflictPolicy(plans, policy);
				}

				var createdNodes = new List<FileSystemNodeViewModel>();
				var movedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var errors = new List<string>();
				int skipped = 0;

				// 逐项执行。成功项才进 createdNodes/movedSourcePaths，
				// 失败项记录错误并继续，避免“第一项失败导致整批半途而废”。
				foreach (var plan in plans)
				{
					if (plan.Skip)
					{
						skipped++;
						continue;
					}

					try
					{
						if (isCut)
							await _fileOperator.MoveAsync(plan.SourcePath, plan.DestPath, plan.Overwrite,
								p => UpdateOperationProgress(pasteOp, p.CompletedFiles, totalFiles, p.CompletedBytes, totalBytes));
						else
							await _fileOperator.CopyToAsync(plan.SourcePath, plan.DestPath, false,
								p => UpdateOperationProgress(pasteOp, p.CompletedFiles, totalFiles, p.CompletedBytes, totalBytes));

						bool isDir = Directory.Exists(plan.DestPath);
						var node = new FileSystemNodeViewModel(plan.DestPath, isDir, false, AppConfigs, _uiDispatcherQueue, false);
						_ = node.InitAsync(node.FullPath, isDir);
						PrepareNodeForGroupedView(node);
						createdNodes.Add(node);
						if (isCut)
							movedSourcePaths.Add(plan.SourcePath);
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"[Paste] 单项失败: {plan.SourcePath}: {ex.Message}");
						errors.Add(ex.Message);
					}
				}

				if (skipped > 0)
					Debug.WriteLine($"[Paste] 已按用户选择跳过 {skipped} 个同名项");

				await _uiDispatcherQueue.EnqueueAsync(() =>
				{
					// 1) 新文件加到“目标目录”而不是“当前正在看的目录”。
					//    目标目录正好是当前展示目录 → 加到表格与树缓存；
					//    否则只让目标目录缓存失效，回访时重新读盘。
					ApplyPastedNodesToFolder(destDir, createdNodes);

					// 2) 剪切：只有真正移动成功的项才从视图/源目录缓存移除，
					//    失败的项保留（且仍带待剪切标记，可继续粘贴到别处）。
					if (isCut)
					{
						var loadedFolderNodes = CollectLoadedDirectoryNodes();
						foreach (var item in _cutItems.ToList())
						{
							if (movedSourcePaths.Contains(item.FullPath))
							{
								RemoveItemFromCurrentView(item);
								RemoveNodeFromAllFolderCaches(item, loadedFolderNodes);
								ClearCutPendingFor(item);
							}
						}
					}

					NotifyViewCountChanged();
				});

				if (errors.Count == 0)
				{
					CompleteOperation(pasteOp, totalFiles, totalBytes);
					BreadcrumbRefreshRequested?.Invoke();
				}
				else
				{
					var summary = errors.Count == 1
						? errors[0]
						: $"{errors.Count} {ML.FileOpFailed}: {errors[0]}";
					FailOperation(pasteOp, new IOException(summary));
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Paste] 粘贴失败: {ex}");
				FailOperation(pasteOp, ex);
			}
			finally
			{
				_pasteLock.Release();
			}
		}

		/// <summary>
		/// 将累积进度映射到操作岛显示：文件个数、进度百分比、已传输/总大小。
		/// </summary>
		private void UpdateOperationProgress(FileOperationItem? op, int completedFiles, int totalFiles, long completedBytes, long totalBytes)
		{
			if (op == null) return;
			double percent = totalBytes > 0
				? (double)completedBytes / totalBytes * 100.0
				: (totalFiles > 0 ? (double)completedFiles / totalFiles * 100.0 : 100.0);
			_uiDispatcherQueue.TryEnqueue(() =>
			{
				op.Progress = Math.Clamp(percent, 0.0, 100.0);
				op.Process = $"{(int)percent}%";
				op.FileCount = totalFiles;
				op.SizeText = $"{FileSystemNodeViewModel.FormatFileSize(completedBytes)} / {FileSystemNodeViewModel.FormatFileSize(totalBytes)}";
			});
		}

		/// <summary>
		/// 标记操作完成：进度 100%，大小显示为总量。
		/// </summary>
		private void CompleteOperation(FileOperationItem? op, int totalFiles, long totalBytes)
		{
			if (op == null) return;
			_uiDispatcherQueue.TryEnqueue(() =>
			{
				op.Progress = 100;
				op.Process = "100%";
				op.FileCount = totalFiles;
				op.SizeText = $"{FileSystemNodeViewModel.FormatFileSize(totalBytes)} / {FileSystemNodeViewModel.FormatFileSize(totalBytes)}";
				op.RemainTime = "0";
				op.State = FileOperationState.Successful;
			});
		}

		/// <summary>
		/// 标记操作失败。
		/// </summary>
		private void FailOperation(FileOperationItem? op, Exception? ex = null)
		{
			if (op == null) return;
			_uiDispatcherQueue.TryEnqueue(() =>
			{
				op.Progress = 0;
				op.Process = ML.FileOpFailed;
				op.RemainTime = "0";
				op.ErrorMessage = ex?.Message ?? "";
				op.State = FileOperationState.Error;
			});
		}

		private static string GenerateUniquePath(string destPath, ISet<string>? reserved = null)
		{
			bool Taken(string p)
				=> File.Exists(p) || Directory.Exists(p) || (reserved?.Contains(p) ?? false);

			if (!Taken(destPath))
				return destPath;

			var dir = Path.GetDirectoryName(destPath) ?? "";
			var name = Path.GetFileNameWithoutExtension(destPath);
			var ext = Path.GetExtension(destPath);

			int index = 1;
			string newPath;
			do
			{
				newPath = Path.Combine(dir, $"{name} ({index}){ext}");
				index++;
			}
			while (Taken(newPath));

			return newPath;
		}

		/// <summary>一次粘贴里单项的最终计划（目标路径、是否冲突、是否跳过、是否覆盖）。</summary>
		private sealed class PastePlanItem
		{
			public required string SourcePath { get; init; }
			public required string Name { get; init; }
			public required string DestPath { get; set; }
			public bool Conflict { get; set; }
			public bool IntraBatchDuplicate { get; set; }
			public bool Skip { get; set; }
			public bool Overwrite { get; set; }
		}

		/// <summary>同名冲突询问入口：由视图弹 ContentDialog，返回用户选择（无视图时默认跳过）。</summary>
		public event Func<IReadOnlyList<string>, Task<FileConflictResolution>>? ConflictResolutionRequested;

		private async Task<FileConflictResolution> ResolveFileConflictsAsync(IReadOnlyList<string> names)
		{
			var handler = ConflictResolutionRequested;
			if (handler == null)
				return FileConflictResolution.Skip;
			return await handler(names);
		}

		/// <summary>
		/// 规划粘贴目标：复制沿用自动改名；剪切检测目标目录里的同名项与本批内部的重名。
		/// </summary>
		private static List<PastePlanItem> BuildPastePlans(IReadOnlyList<string> pathList, string destDir, bool isCut)
		{
			var plans = new List<PastePlanItem>(pathList.Count);
			var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var srcPath in pathList)
			{
				var name = Path.GetFileName(srcPath);
				if (string.IsNullOrEmpty(name))
					name = Path.GetFileName(srcPath.TrimEnd('\\', '/'));
				if (string.IsNullOrEmpty(name))
					throw new ArgumentException($"无法确定要粘贴的项目的名称: {srcPath}");

				var baseDest = Path.Combine(destDir, name);
				string destPath;
				bool conflict = false;
				bool intraBatch = false;

				if (isCut)
				{
					destPath = baseDest;
					bool diskConflict = File.Exists(destPath) || Directory.Exists(destPath);
					intraBatch = !claimed.Add(destPath);
					conflict = diskConflict || intraBatch;
				}
				else
				{
					// 复制：不打扰用户，自动生成唯一名（同时避开本批内已经占用的名字）
					destPath = GenerateUniquePath(baseDest, claimed);
					claimed.Add(destPath);
				}

				plans.Add(new PastePlanItem
				{
					SourcePath = srcPath,
					Name = name,
					DestPath = destPath,
					Conflict = conflict,
					IntraBatchDuplicate = intraBatch
				});
			}

			return plans;
		}

		/// <summary>
		/// 应用用户对本次全部冲突项的选择：
		/// Replace = 覆盖/合并；Skip = 跳过；KeepBoth = 自动改名。
		/// 同一批内部重名（两个不同来源同名）强制走 KeepBoth，避免后一项覆盖前一项。
		/// </summary>
		private static void ApplyConflictPolicy(List<PastePlanItem> plans, FileConflictResolution policy)
		{
			var claimed = new HashSet<string>(plans.Select(p => p.DestPath), StringComparer.OrdinalIgnoreCase);

			foreach (var plan in plans.Where(p => p.Conflict))
			{
				if (policy == FileConflictResolution.Skip)
				{
					plan.Skip = true;
					continue;
				}

				if (policy == FileConflictResolution.KeepBoth || plan.IntraBatchDuplicate)
				{
					plan.DestPath = GenerateUniquePath(plan.DestPath, claimed);
					claimed.Add(plan.DestPath);
					plan.Overwrite = false;
				}
				else
				{
					plan.Overwrite = true;
				}
			}
		}

		// ======================= 视图/目录缓存一致性工具 =======================

		private static bool PathEquals(string? a, string? b)
		{
			if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
				return false;
			return string.Equals(
				a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'),
				StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// 从集合中移除指定路径的节点（优先按引用移除，并且会把所有同路径的残留项一起清掉，
		/// 便于“替换/合并”后清掉旧节点，避免同一路径出现两行）。
		/// </summary>
		private static int RemoveNodeByFullPath(
			ObservableCollection<FileSystemNodeViewModel>? collection,
			FileSystemNodeViewModel? preferred,
			string fullPath)
		{
			if (collection == null || collection.Count == 0) return 0;

			int removed = 0;
			if (preferred != null && collection.Remove(preferred))
				removed++;

			for (int i = collection.Count - 1; i >= 0; i--)
			{
				if (string.Equals(collection[i].FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
				{
					collection.RemoveAt(i);
					removed++;
				}
			}
			return removed;
		}

		/// <summary>把“当前正在展示的表格/搜索结果/当前目录子项”里的节点按引用优先、路径兜底移除。</summary>
		private void RemoveItemFromCurrentView(FileSystemNodeViewModel item)
		{
			if (item == null) return;
			var path = item.FullPath;
			if (IsSearchMode)
				RemoveNodeByFullPath(SearchResults, item, path);
			else
				RemoveNodeByFullPath(CurrentFolderContent, item, path);
			RemoveNodeByFullPath(SelectedFolder?.Children, item, path);
		}

		/// <summary>收集所有“已加载”的目录节点（含侧栏根、固定栏、各标签页与当前目录），UI 线程调用。</summary>
		private List<FileSystemNodeViewModel> CollectLoadedDirectoryNodes()
		{
			var result = new List<FileSystemNodeViewModel>();
			var seen = new HashSet<FileSystemNodeViewModel>();

			void Visit(FileSystemNodeViewModel? node)
			{
				if (node == null || node.IsPlaceholder || !node.IsDirectory) return;
				if (!seen.Add(node)) return;
				if (node.IsLoaded)
					result.Add(node);
				foreach (var child in node.Children.ToArray())
				{
					if (child.IsDirectory)
						Visit(child);
				}
			}

			foreach (var root in RootDirectories) Visit(root);
			foreach (var p in PinnedShortcuts) Visit(p);
			foreach (var tab in Tabs) Visit(tab.FolderNode);
			Visit(SelectedFolder);
			return result;
		}

		/// <summary>
		/// 把指定真实路径的节点从所有“已加载目录缓存”中按路径移除。
		/// 这样即使该文件曾以不同实例被缓存在多个标签页/目录树分支里，
		/// 回访时也不会出现幽灵行（无需整目录释放、不影响当前树展开状态）。
		/// </summary>
		private void RemoveNodeFromAllFolderCaches(FileSystemNodeViewModel item)
			=> RemoveNodeFromAllFolderCaches(item, CollectLoadedDirectoryNodes());

		private void RemoveNodeFromAllFolderCaches(FileSystemNodeViewModel item, IReadOnlyList<FileSystemNodeViewModel> loadedFolders)
		{
			if (item == null || item.IsPlaceholder) return;
			var parentPath = Path.GetDirectoryName(item.FullPath);
			if (string.IsNullOrEmpty(parentPath)) return;

			foreach (var folder in loadedFolders)
			{
				if (!PathEquals(folder.FullPath, parentPath)) continue;
				RemoveNodeByFullPath(folder.Children, folder.Children.Contains(item) ? item : null, item.FullPath);
			}
		}

		/// <summary>
		/// 目标目录不是当前展示目录时，把其缓存标记为“需重读磁盘”。
		/// 只释放非当前节点，避免打断正在展示的目录树/表格。
		/// </summary>
		private void InvalidateFolderByPath(string folderPath)
		{
			if (string.IsNullOrWhiteSpace(folderPath) || IsVirtualRootToken(folderPath)) return;
			var selected = SelectedFolder;
			foreach (var node in CollectLoadedDirectoryNodes())
			{
				if (ReferenceEquals(node, selected)) continue;
				if (PathEquals(node.FullPath, folderPath))
					node.ReleaseChildren();
			}
		}

		/// <summary>
		/// 把粘贴产物放入目标目录：目标目录 == 当前展示目录时直接加表格与树；
		/// 目标目录已作为其它标签页/树分支加载时直接更新其 Children；
		/// 否则只使目标目录缓存失效（回访时读盘得到正确内容）。
		/// 替换/合并时目标目录里可能已有同路径旧节点，必须先按路径移除再加新节点，否则会出现两行。
		/// </summary>
		private void ApplyPastedNodesToFolder(string destDir, List<FileSystemNodeViewModel> nodes)
		{
			if (nodes == null || nodes.Count == 0) return;

			if (!IsSearchMode && SelectedFolder != null && PathEquals(SelectedFolder.FullPath, destDir))
			{
				foreach (var node in nodes)
				{
					// 先清掉旧节点（引用/路径都可能与新建节点不同实例）
					RemoveNodeByFullPath(CurrentFolderContent, null, node.FullPath);
					RemoveNodeByFullPath(SelectedFolder.Children, null, node.FullPath);

					if (!CurrentFolderContent.Contains(node))
						CurrentFolderContent.Add(node);
					node.Parent = SelectedFolder;
					if (!SelectedFolder.Children.Contains(node))
						SelectedFolder.Children.Add(node);
				}
				return;
			}

			// 非当前但已加载的目标目录（例如另一标签页正打开、或目录树已展开）：
			// 直接补进它的 Children，保持展开状态并让回访立即可见。
			var loadedDest = CollectLoadedDirectoryNodes()
				.FirstOrDefault(f => f.IsDirectory && PathEquals(f.FullPath, destDir));
			if (loadedDest != null && loadedDest.IsLoaded)
			{
				foreach (var node in nodes)
				{
					RemoveNodeByFullPath(loadedDest.Children, null, node.FullPath);
					node.Parent = loadedDest;
					if (!loadedDest.Children.Contains(node))
						loadedDest.Children.Add(node);
				}
				return;
			}

			InvalidateFolderByPath(destDir);
		}

		/// <summary>增删条目后刷新底部“共 N 项”计数（普通视图与搜索视图都生效）。</summary>
		private void NotifyViewCountChanged()
		{
			if (IsSearchMode)
			{
				// 搜索视图的表格是对 SearchResults 的独立快照，改动后必须通知视图重建一次。
				OnPropertyChanged(nameof(SearchResults));
			}
			OnPropertyChanged(nameof(DisplayedItemCount));
		}

		[RelayCommand]
		private async Task Delete(IReadOnlyList<FileSystemNodeViewModel>? items)
			=> await RunDeleteAsync(items, toRecycleBin: true);

		[RelayCommand]
		private async Task PermanentDelete(IReadOnlyList<FileSystemNodeViewModel>? items)
			=> await RunDeleteAsync(items, toRecycleBin: false);

		/// <summary>
		/// 删除/彻底删除统一入口：先为每个待删项建 InProgress 操作岛卡，
		/// 磁盘删除成功才移除 UI 行并置 Successful，失败保留行并显示具体错误。
		/// </summary>
		private async Task RunDeleteAsync(IReadOnlyList<FileSystemNodeViewModel>? items, bool toRecycleBin)
		{
			if (items == null || items.Count == 0) return;
			var list = items.Where(i => !i.IsPlaceholder).ToList();
			if (list.Count == 0) return;

			// M4 防护：压缩包内部条目、驱动器根、虚拟位置没有可删除的真实路径。
			if (list.Any(i => !IsWritableItem(i))) return;

			var paths = list.Select(i => i.FullPath).ToList();
			var opsByPath = new Dictionary<string, FileOperationItem>(StringComparer.OrdinalIgnoreCase);
			foreach (var item in list)
			{
				var text = $"{(toRecycleBin ? ML.CmdDelete : ML.CmdPermanentDelete)} {item.Name}";
				var op = CreateReportedOperation(text, "\uE74D", fileCount: 1);
				opsByPath[item.FullPath] = op;
			}

			var results = await _fileOperator.DeleteManyAsync(paths, toRecycleBin);

			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				var loadedFolderNodes = CollectLoadedDirectoryNodes();
				for (int i = 0; i < list.Count; i++)
				{
					var item = list[i];
					var result = i < results.Count ? results[i] : new FileOperationResult(item.FullPath, false, ML.FileOpFailed);
					if (opsByPath.TryGetValue(item.FullPath, out var op))
					{
						if (result.Success)
						{
							op.Progress = 100;
							op.Process = "100%";
							op.FileCount = 1;
							op.RemainTime = "0";
							op.SizeText = "0 B / 0 B";
							op.State = FileOperationState.Successful;

							// 磁盘删除成功后才移除 UI 行，并同步清理所有已加载目录缓存中的旧节点，
							// 避免回访该目录时出现“文件已删但还在列表”的幽灵项。
							RemoveItemFromCurrentView(item);
							RemoveNodeFromAllFolderCaches(item, loadedFolderNodes);
						}
						else
						{
							op.Progress = 0;
							op.Process = ML.FileOpFailed;
							op.RemainTime = "0";
							op.ErrorMessage = result.ErrorMessage ?? ML.FileOpFailed;
							op.State = FileOperationState.Error;
						}
					}
				}

				NotifyViewCountChanged();
			});
		}

		[RelayCommand]
		private async Task Rename(FileSystemNodeViewModel? item)
		{
			if (item == null) return;
			// 压缩包内部条目/虚拟位置不可重命名
			if (!IsWritableItem(item)) return;
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
		public event Action<FileSystemNodeViewModel>? SelectItemRequested;

		public void RequestBreadcrumbRefresh() => BreadcrumbRefreshRequested?.Invoke();

		public async Task CommitRenameAsync(FileSystemNodeViewModel item, string newName)
		{
			if (string.IsNullOrEmpty(newName)) return;
			try
			{
				var oldDir = Path.GetDirectoryName(item.FullPath) ?? "";
				var newPath = Path.Combine(oldDir, newName);
				await _fileOperator.RenameAsync(item.FullPath, newName);
				item.FullPath = newPath;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Rename] Failed: {ex.Message}");
			}
			_renamingItem = null;
		}

		public void AddItemToCurrentView(string fullPath, bool isDirectory)
		{
			var node = new FileSystemNodeViewModel(fullPath, isDirectory, false, _appConfigs, _uiDispatcherQueue, false);
			_ = node.InitAsync(node.FullPath, isDirectory);
			PrepareNodeForGroupedView(node);
			_uiDispatcherQueue.TryEnqueue(() =>
			{
				CurrentFolderContent.Add(node);
				if (SelectedFolder != null)
				{
					node.Parent = SelectedFolder;
					SelectedFolder.Children.Add(node);
				}
				NotifyViewCountChanged();
			});
		}

		[RelayCommand]
		private async Task CopyPath(IReadOnlyList<FileSystemNodeViewModel>? items)
		{
			if (items == null || items.Count == 0) return;
			var text = string.Join(Environment.NewLine, items.Select(i => i.FullPath));
			var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
			dataPackage.SetText(text);
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

		/// <summary>
		/// 以独立 WinUI 子窗口显示文件/文件夹属性。
		/// </summary>
		private void ShowPropertiesDialog(FileSystemNodeViewModel item)
		{
			try
			{
				if (App.MainWindow == null)
					return;

				string fullPath = item.FullPath;
				if (string.IsNullOrWhiteSpace(fullPath))
					return;

				bool exists = item.IsDirectory ? Directory.Exists(fullPath) : File.Exists(fullPath);
				if (!exists)
					return;

				_propertiesWindow?.Close();
				var window = new PropertiesWindow(item);
				_propertiesWindow = window;
				window.Closed += (_, _) =>
				{
					if (ReferenceEquals(_propertiesWindow, window))
						_propertiesWindow = null;
				};
				window.Activate();
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Properties] Failed to open properties window for '{item.FullPath}': {ex.Message}");
			}
		}

		/// <summary>主窗口关闭时同步关闭属性子窗口。</summary>
		public void ClosePropertiesWindow()
		{
			_propertiesWindow?.Close();
			_propertiesWindow = null;
		}

		[RelayCommand]
		private async Task NewFolder()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewFolderDefault, isDirectory: true);
			BreadcrumbRefreshRequested?.Invoke();
		}

		[RelayCommand]
		private async Task NewTextDocument()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewTextDocumentDefault, isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
		}

		[RelayCommand]
		private async Task NewShortcut()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewShortcutDefault, isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
		}

		[RelayCommand]
		private async Task NewFile()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewFileDefault, isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
		}

		[RelayCommand]
		private async Task NewExcelSpreadsheet()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewExcelDefault, isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
		}

		[RelayCommand]
		private async Task NewWordDocument()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewWordDefault, isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
		}

		[RelayCommand]
		private async Task NewPowerPointPresentation()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewPPTDefault, isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
		}

		// 若当前文件夹按时间分组，需在加入视图前同步补齐 LastModifiedTime 并设置分组键，
		// 否则元数据尚未异步加载完毕，条目会被错误地归入“很久以前”而看不到刷新效果。
		private void PrepareNodeForGroupedView(FileSystemNodeViewModel node)
		{
			if (SelectedFolder?.WillSplitToDifferentSorts != true || node.IsPlaceholder)
				return;
			node.LoadMetadataSync();
			node.SortByTime = Helpers.GroupedFileList.GetTimeGroup(node.LastModifiedTime);
		}

		private async Task AddNewItemToViewAsync(string destDir, string defaultName, bool isDirectory)
		{
			// 回收站/此电脑/网络/压缩包内部等虚拟位置、以及搜索结果视图不允许新建
			if (IsSearchMode || !IsWritableFolderPath(destDir))
			{
				Debug.WriteLine($"[NewItem] 目标不可写: {destDir}");
				return;
			}
			var newPath = GenerateUniquePath(Path.Combine(destDir, defaultName));
			if (isDirectory)
				Directory.CreateDirectory(newPath);
			else
				File.Create(newPath).Dispose();

			var node = new FileSystemNodeViewModel(newPath, isDirectory, false, AppConfigs, _uiDispatcherQueue, false);
			PrepareNodeForGroupedView(node);
			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				CurrentFolderContent.Add(node);
				if (SelectedFolder != null)
				{
					node.Parent = SelectedFolder;
					SelectedFolder.Children.Add(node);
				}
				NotifyViewCountChanged();
			});
			_ = node.InitAsync(node.FullPath, isDirectory);
		}

		private void AddNewItemToView(string destDir, string defaultName, bool isDirectory)
		{
			_ = AddNewItemToViewAsync(destDir, defaultName, isDirectory);
		}

		public async Task RefreshCurrentFolderAsync()
		{
			if (SelectedFolder != null)
			{
				// 强制刷新：清空展示标记，使 UpdateCurrentFolderContentAsync 重建列表
				_displayedFolderNode = null;
				await SelectedFolder.ReloadChildrenAsync();
				await UpdateCurrentFolderContentAsync(SelectedFolder, version: null);
				BreadcrumbRefreshRequested?.Invoke();
			}
		}

		private void DebounceSaveLastVisitedPath(string path)
		{
			// 初始化完成前不保存（构造函数中 SelectedFolder=C:\ 会误触发）
			if (AppConfigs == null || !IsReady) return;
			AppConfigs.LastVisitedPath = path;
			_saveConfigCts?.Cancel();
			_saveConfigCts = new CancellationTokenSource();
			var token = _saveConfigCts.Token;
			_ = Task.Run(async () =>
			{
				try
				{
					await Task.Delay(2000, token);
					if (!token.IsCancellationRequested)
					{
						await _uiDispatcherQueue.EnqueueAsync(() =>
						{
							if (!token.IsCancellationRequested)
								AppConfigs?.SaveConfig();
						});
					}
				}
				catch (TaskCanceledException) { }
			}, token);
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
		[ObservableProperty] private bool _isReady;
		[ObservableProperty] private bool _isSearchMode = false;
		[ObservableProperty] private string _searchText = string.Empty;
		[ObservableProperty] private ObservableCollection<FileSystemNodeViewModel> _searchResults = new();
		[ObservableProperty] private bool _isSearching = false;
		private CancellationTokenSource? _searchCts;

		public int DisplayedItemCount => IsSearchMode ? SearchResults.Count : CurrentFolderContent.Count;
		partial void OnIsSearchModeChanged(bool value) => OnPropertyChanged(nameof(DisplayedItemCount));

		partial void OnSearchTextChanged(string value) => TriggerSearch(value);

		// ===== 多标签页 =====
		public ObservableCollection<ExplorerTab> Tabs { get; } = new();
		[ObservableProperty] private ExplorerTab? _selectedTab;
		public ExplorerTab? CurrentTab => SelectedTab;
		private bool _isRestoringTab;

		[RelayCommand]
		private void NewTab()
		{
			var path = GetStartupPath();
			var tab = new ExplorerTab();
			if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
			{
				tab.Path = path;
				tab.Title = GetTabTitle(path);
			}
			else
			{
				tab.Title = ML.NavExplorer;
			}
			Tabs.Add(tab);
			SwitchToTab(tab);
		}

		public void CloseCurrentTab()
		{
			if (SelectedTab != null) CloseTab(SelectedTab);
		}

		public void CloseTab(ExplorerTab tab)
		{
			if (tab == null || Tabs.Count <= 1) return; // 至少保留一个标签页
			var index = Tabs.IndexOf(tab);
			var wasActive = ReferenceEquals(tab, SelectedTab);

			if (wasActive)
			{
				SaveTabState(tab);
				var next = index + 1 < Tabs.Count ? Tabs[index + 1] : Tabs[index - 1];
				SelectedTab = next;
				ActivateTab(next);
				tab.SearchCts?.Cancel();
				Tabs.Remove(tab);
			}
			else
			{
				tab.SearchCts?.Cancel();
				Tabs.Remove(tab);
			}
		}

		public void SwitchToTab(ExplorerTab tab)
		{
			if (tab == null || ReferenceEquals(tab, SelectedTab)) return;
			if (SelectedTab != null)
			{
				SaveTabState(SelectedTab);
				SelectedTab.SearchCts?.Cancel();
			}
			SelectedTab = tab;
			ActivateTab(tab);
		}

		private void SaveTabState(ExplorerTab tab)
		{
			if (tab == null) return;
			tab.Path = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			tab.FolderNode = SelectedFolder;
			tab.IsSearchMode = IsSearchMode;
			tab.SearchText = SearchText;
			tab.IsSearching = IsSearching;
			tab.SearchResults = SearchResults;
			tab.SearchCts = _searchCts;
		}

		private void ActivateTab(ExplorerTab tab)
		{
			if (tab == null) return;

			// 恢复搜索状态（恢复过程中不重复触发搜索）
			var searchText = tab.SearchText;
			var restartSearch = tab.IsSearching;
			_isRestoringTab = true;
			try
			{
				SearchResults = tab.SearchResults;
				SearchText = searchText;
				IsSearchMode = tab.IsSearchMode;
				IsSearching = tab.IsSearching;
			}
			finally
			{
				_isRestoringTab = false;
			}
			OnPropertyChanged(nameof(SearchResults));
			OnPropertyChanged(nameof(DisplayedItemCount));

			// 恢复导航状态
			var path = string.IsNullOrEmpty(tab.Path) ? GetStartupPath() : tab.Path;
			CurrentBreadcrumbPath = path;
			tab.IsNavigatingFromHistory = true;
			try
			{
				var target = tab.FolderNode
					?? FindNodeByPath(path)
					?? CreateStandaloneNode(path);
				if (target != null)
				{
					tab.FolderNode = target;
					SelectedFolder = target;
				}
				else
				{
					CurrentFolderContent.Clear();
					NotifyViewCountChanged();
				}
			}
			finally
			{
				tab.IsNavigatingFromHistory = false;
			}

			CanGoBack = tab.BackStack.Count > 0;
			CanGoForward = tab.ForwardStack.Count > 0;

			// 切回标签页时，若上次离开时搜索仍在进行，则重新发起搜索
			if (restartSearch && tab.IsSearchMode && !string.IsNullOrEmpty(searchText))
				TriggerSearch(searchText);

			RequestBreadcrumbRefresh();
		}

		private FileSystemNodeViewModel? CreateStandaloneNode(string path)
		{
			if (string.IsNullOrEmpty(path)) return null;
			if (ArchiveHelper.IsArchiveVirtualPath(path, out var archiveFile, out var relative))
			{
				var node = FileSystemNodeViewModel.CreateArchiveDirectory(archiveFile, relative, AppConfigs!, _uiDispatcherQueue);
				node.IsStandalone = true;
				return node;
			}
			if (!Directory.Exists(path)) return null;
			// lazyLoad: true —— 恢复标签页时同样避免重复枚举，子项由 UpdateCurrentFolderContentAsync 加载一次
			var newNode = new FileSystemNodeViewModel(path, true, false, AppConfigs!, _uiDispatcherQueue, true);
			newNode.IsStandalone = true;
			return newNode;
		}

		private string GetTabTitle(string path)
		{
			if (string.IsNullOrEmpty(path)) return string.Empty;
			var sidebarName = GetSidebarDisplayName(path);
			if (sidebarName != null) return sidebarName;
			if (ArchiveHelper.IsArchiveVirtualPath(path, out var archiveFile, out var relative))
				path = string.IsNullOrEmpty(relative) ? archiveFile : relative;
			var name = Path.GetFileName(path.TrimEnd('\\'));
			return string.IsNullOrEmpty(name) ? path.TrimEnd('\\') : name;
		}

		public void EnterSearchMode()
		{
			// 已是搜索模式时（例如切换标签页恢复搜索 UI）不清空已有结果
			if (!IsSearchMode)
			{
				SearchResults.Clear();
				SearchText = string.Empty;
				IsSearchMode = true;
			}
			if (CurrentTab != null) CurrentTab.IsSearchMode = true;
		}

		public void ExitSearchMode()
		{
			_searchCts?.Cancel();
			IsSearchMode = false;
			SearchText = string.Empty;
			SearchResults.Clear();
			if (CurrentTab != null)
			{
				CurrentTab.IsSearchMode = false;
				CurrentTab.SearchText = string.Empty;
			}
		}

		private void TriggerSearch(string query)
		{
			if (_isRestoringTab) return;
			var tab = CurrentTab;
			_searchCts?.Cancel();
			_searchCts = new CancellationTokenSource();
			var token = _searchCts.Token;
			if (tab != null)
			{
				tab.SearchCts = _searchCts;
				tab.SearchText = query;
			}
			query = query.Trim();
			if (query.Length == 0)
			{
				IsSearching = false;
				if (tab != null) tab.IsSearching = false;
				SearchResults.Clear();
				OnPropertyChanged(nameof(SearchResults));
				OnPropertyChanged(nameof(DisplayedItemCount));
				return;
			}
			var scope = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			if (string.IsNullOrEmpty(scope) || !Directory.Exists(scope))
			{
				IsSearching = false;
				if (tab != null) tab.IsSearching = false;
				return;
			}
			IsSearching = true;
			if (tab != null) tab.IsSearching = true;
			SearchResults.Clear();
			OnPropertyChanged(nameof(SearchResults));
			OnPropertyChanged(nameof(DisplayedItemCount));
			_ = Task.Run(() => RunSearchAsync(scope, query, token), token);
		}

		private async Task RunSearchAsync(string scope, string query, CancellationToken token)
		{
			var results = new List<FileSystemNodeViewModel>();
			var sw = System.Diagnostics.Stopwatch.StartNew();
			var indexed = await Services.WindowsSearchHelper.QueryIndexAsync(scope, query, token);
			sw.Stop();
			System.Diagnostics.Debug.WriteLine($"[Search] index query {sw.ElapsedMilliseconds}ms, hits={indexed.Count}");
			if (token.IsCancellationRequested) return;
			if (indexed.Count > 0)
			{
				foreach (var (path, isDir) in indexed)
					results.Add(CreateSearchNode(path, isDir));
			}
			else
			{
				SearchRecursive(scope, query, results, token); // 索引无结果/失败/非索引位置 → 回退
			}
			if (token.IsCancellationRequested) return;
			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				if (token.IsCancellationRequested) return;
				SearchResults.Clear();
				foreach (var r in results) SearchResults.Add(r);
				OnPropertyChanged(nameof(SearchResults));
				OnPropertyChanged(nameof(DisplayedItemCount));
				IsSearching = false;
				if (CurrentTab != null) CurrentTab.IsSearching = false;
			});
		}

		private static List<string> EnumerateDirsSafe(string dir)
		{
			try { return Directory.EnumerateDirectories(dir).ToList(); }
			catch { return new List<string>(); } // 无权限/不存在 → 返回空，由调用方跳过
		}
		private static List<string> EnumerateFilesSafe(string dir)
		{
			try { return Directory.EnumerateFiles(dir).ToList(); }
			catch { return new List<string>(); } // 无权限/不存在 → 返回空，由调用方跳过
		}

		private void SearchRecursive(string dir, string query, List<FileSystemNodeViewModel> results, CancellationToken token)
		{
			if (token.IsCancellationRequested || results.Count >= 200) return;
			foreach (var sub in EnumerateDirsSafe(dir))
			{
				if (token.IsCancellationRequested || results.Count >= 200) break;
				if (Path.GetFileName(sub).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
					results.Add(CreateSearchNode(sub, true));
				SearchRecursive(sub, query, results, token);
			}
			if (token.IsCancellationRequested || results.Count >= 200) return;
			foreach (var file in EnumerateFilesSafe(dir))
			{
				if (token.IsCancellationRequested || results.Count >= 200) break;
				if (Path.GetFileName(file).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
					results.Add(CreateSearchNode(file, false));
			}
		}

		private FileSystemNodeViewModel CreateSearchNode(string path, bool isDir)
		{
			var node = new FileSystemNodeViewModel(path, isDir, false, AppConfigs!, _uiDispatcherQueue, true);
			try
			{
				if (isDir) { var d = new DirectoryInfo(path); node.ApplyMetadata(true, 0, d.LastWriteTimeUtc, d.CreationTimeUtc, (d.Attributes & FileAttributes.Hidden) != 0, (d.Attributes & FileAttributes.System) != 0); }
				else { var f = new FileInfo(path); node.ApplyMetadata(false, f.Length, f.LastWriteTimeUtc, f.CreationTimeUtc, (f.Attributes & FileAttributes.Hidden) != 0, (f.Attributes & FileAttributes.System) != 0); }
			}
			// 元数据读取失败时保留默认值
			catch { }
			return node;
		}

		public Microsoft.UI.Xaml.Visibility FileTableVisibility => IsSettingsOpen ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
		public Microsoft.UI.Xaml.Visibility SettingsVisibility => IsSettingsOpen ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
		partial void OnIsSettingsOpenChanged(bool value)
		{
			OnPropertyChanged(nameof(FileTableVisibility));
			OnPropertyChanged(nameof(SettingsVisibility));
		}
		public SemaphoreSlim IconLoadSemaphore = new(30, 30); // 最多30个并发
		private readonly SemaphoreSlim _pasteLock = new(1, 1);
		private string? _pasteTargetOverride;

		/// <summary>设置下一次“粘贴”的目标文件夹（右键文件夹→粘贴时使用）。</summary>
		public void SetPasteTarget(string? fullPath) => _pasteTargetOverride = fullPath;
		private const int MaxBackDepth = 100;
		private CancellationTokenSource? _saveConfigCts;
		private ObservableCollection<FileSystemNodeViewModel> _rootDirectories = new();
		public ObservableCollection<FileSystemNodeViewModel> RootDirectories
		{
			get => _rootDirectories;
			set => _rootDirectories = value;
		}

		/// <summary>是否为侧栏虚拟位置路径（CLSID 或云盘分组占位路径，不含真实盘符）。</summary>
		public bool IsVirtualRootToken(string path)
		{
			if (string.IsNullOrEmpty(path)) return false;
			if (path.StartsWith("::{", StringComparison.OrdinalIgnoreCase)) return true;
			return string.Equals(path, FileSystemNodeViewModel.CloudGroupPath, StringComparison.OrdinalIgnoreCase);
		}

		// ======================= 可写位置判断（M4 防护） =======================

		/// <summary>真实、可访问、非虚拟位置、非压缩包内部的目录才允许写入（粘贴/新建等）。</summary>
		public bool IsWritableFolderPath(string? path)
		{
			if (string.IsNullOrWhiteSpace(path)) return false;
			if (IsVirtualRootToken(path)) return false;
			if (path.StartsWith("::{", StringComparison.OrdinalIgnoreCase)) return false;
			if (ArchiveHelper.IsArchiveVirtualPath(path, out _, out _)) return false;
			if (!Path.IsPathRooted(path)) return false;
			return Directory.Exists(path);
		}

		private bool IsWritableFolderNode(FileSystemNodeViewModel? folder)
		{
			if (folder == null || folder.IsPlaceholder || folder.IsArchiveEntry) return false;

			switch (folder.NodeKind)
			{
				case FileNodeKind.ThisPc:
				case FileNodeKind.Network:
				case FileNodeKind.Wsl:
				case FileNodeKind.CloudGroup:
				case FileNodeKind.RecycleBin:
					return false;
			}
			return IsWritableFolderPath(folder.FullPath);
		}

		/// <summary>可作为复制/剪切/删除/重命名对象的真实条目（排除压缩包条目、回收站条目、驱动器根、虚拟位置）。</summary>
		public bool IsWritableItem(FileSystemNodeViewModel? item)
		{
			if (item == null || item.IsPlaceholder) return false;
			if (item.IsRecycleEntry || item.IsArchiveEntry) return false;
			if (item.NodeKind == FileNodeKind.Drive) return false;
			if (IsVirtualRootToken(item.FullPath)) return false;
			return File.Exists(item.FullPath) || Directory.Exists(item.FullPath);
		}

		/// <summary>当前所在位置是否允许粘贴/新建（供工具栏与菜单判断）。</summary>
		public bool IsCurrentLocationWritable => !IsSearchMode && IsWritableFolderNode(SelectedFolder);

		/// <summary>当前是否允许执行粘贴（搜索视图与虚拟位置都禁止）。</summary>
		public bool IsPasteAllowed => !IsSearchMode &&
			IsWritableFolderPath(_pasteTargetOverride ?? SelectedFolder?.FullPath ?? CurrentBreadcrumbPath);

		/// <summary>路径正好是某个侧栏根的 CLSID/路径时，返回该根的显示名（面包屑/标签标题用）。</summary>
		public string? GetSidebarDisplayName(string path)
		{
			if (string.IsNullOrEmpty(path)) return null;
			foreach (var root in RootDirectories)
			{
				if (string.Equals(root.FullPath, path, StringComparison.OrdinalIgnoreCase))
					return root.Name;
			}
			return null;
		}

		private FileSystemNodeViewModel? FindSidebarRoot(string path)
		{
			if (string.IsNullOrEmpty(path)) return null;
			foreach (var root in RootDirectories)
			{
				if (string.Equals(root.FullPath, path, StringComparison.OrdinalIgnoreCase))
					return root;
			}
			return null;
		}

		// ======================= 回收站操作 =======================
		/// <summary>当前表格是否正在展示回收站内容（决定右键菜单与工具栏分流）。</summary>
		public bool IsRecycleBinFolder => SelectedFolder?.NodeKind == FileNodeKind.RecycleBin;

		private static Services.RecycleBinEntry BuildRecycleEntry(FileSystemNodeViewModel item)
		{
			return new Services.RecycleBinEntry
			{
				Name = item.Name,
				IsDirectory = item.IsDirectory,
				OriginalFullPath = item.FullPath,
				OriginalLocation = item.RecycleOriginalLocation,
				Size = item.ExactSize,
				ModifiedUtc = item.LastModifiedTime,
				Index = item.RecycleEntryIndex
			};
		}

		/// <summary>在超时前等待条件成立（用于校验还原/彻底删除是否真正生效）。</summary>
		private static async Task<bool> WaitUntilAsync(Func<bool> predicate, int timeoutMs = 2500, int stepMs = 150)
		{
			var sw = System.Diagnostics.Stopwatch.StartNew();
			while (sw.ElapsedMilliseconds < timeoutMs)
			{
				bool ok;
				try { ok = predicate(); }
				catch { ok = false; }
				if (ok) return true;
				await Task.Delay(stepMs);
			}
			return false;
		}

		/// <summary>还原单个回收站条目：建操作岛卡，按“原位置是否真的出现文件”判定成功。</summary>
		public async Task RestoreRecycleItemAsync(FileSystemNodeViewModel item)
		{
			if (item == null || !item.IsRecycleEntry) return;

			var entry = BuildRecycleEntry(item);
			var op = CreateReportedOperation($"{ML.RecycleRestore} {item.Name}", "\uE8E5", fileCount: 1);

			// 原位置已有同名项：明确报错交给用户处理，不静默覆盖
			var targetPath = string.IsNullOrEmpty(entry.OriginalLocation)
				? null
				: Path.Combine(entry.OriginalLocation, entry.Name);
			if (!string.IsNullOrEmpty(targetPath) &&
				(File.Exists(targetPath) || Directory.Exists(targetPath)))
			{
				FailOperation(op, new IOException(ML.RecycleRestoreConflict));
				return;
			}

			bool verbOk;
			try
			{
				verbOk = await Task.Run(() => Services.RecycleBinService.RestoreEntry(entry));
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[RecycleBin] 还原异常: {ex.Message}");
				FailOperation(op, ex);
				return;
			}

			if (!verbOk)
			{
				FailOperation(op, new IOException(ML.RecycleRestoreFailed));
				return;
			}

			// 校验：原位置出现条目（或原位置未知时，确认回收站里该物理项已消失）
			bool restored = !string.IsNullOrEmpty(targetPath)
				? await WaitUntilAsync(() => File.Exists(targetPath) || Directory.Exists(targetPath))
				: await WaitUntilAsync(() =>
				{
					var entries = Services.RecycleBinService.EnumerateEntries();
					return !entries.Any(e => string.Equals(e.OriginalFullPath, entry.OriginalFullPath, StringComparison.OrdinalIgnoreCase));
				});

			if (!restored)
			{
				FailOperation(op, new IOException(ML.RecycleRestoreFailed));
				return;
			}

			CompleteOperation(op, 1, 0);
			await _uiDispatcherQueue.EnqueueAsync(() => RemoveRecycleItemFromView(item));
		}

		/// <summary>彻底删除一个或多个回收站条目（不可恢复）：逐项建卡并按真实结果推进状态。</summary>
		public async Task PermanentDeleteRecycleItemsAsync(IReadOnlyList<FileSystemNodeViewModel>? items)
		{
			if (items == null || items.Count == 0) return;
			var list = items.Where(i => i.IsRecycleEntry).ToList();
			if (list.Count == 0) return;

			foreach (var item in list)
			{
				var entry = BuildRecycleEntry(item);
				var op = CreateReportedOperation($"{ML.CmdPermanentDelete} {item.Name}", "\uE74D", fileCount: 1);

				bool verbOk = false;
				Exception? error = null;
				try
				{
					verbOk = await Task.Run(() => Services.RecycleBinService.DeleteEntry(entry));
				}
				catch (Exception ex)
				{
					error = ex;
					Debug.WriteLine($"[RecycleBin] 彻底删除异常: {ex.Message}");
				}

				bool gone = verbOk && await WaitUntilAsync(() =>
				{
					var remaining = Services.RecycleBinService.EnumerateEntries();
					return !remaining.Any(e => string.Equals(e.OriginalFullPath, entry.OriginalFullPath, StringComparison.OrdinalIgnoreCase));
				}, timeoutMs: 3000, stepMs: 250);

				if (!gone)
				{
					FailOperation(op, error ?? new IOException(ML.RecycleDeleteFailed));
					continue;
				}

				CompleteOperation(op, 1, 0);
				await _uiDispatcherQueue.EnqueueAsync(() => RemoveRecycleItemFromView(item));
			}
		}

		/// <summary>清空回收站（调用方确认后调用）：走操作岛并显示真实结果。</summary>
		public async Task EmptyRecycleBinAsync()
		{
			var node = SelectedFolder;
			if (node == null || node.NodeKind != FileNodeKind.RecycleBin) return;

			var op = CreateReportedOperation(ML.RecycleEmpty, "\uE74D", fileCount: 0);

			bool ok = false;
			Exception? error = null;
			try
			{
				ok = await Task.Run(() => Services.RecycleBinService.EmptyRecycleBin());
			}
			catch (Exception ex)
			{
				error = ex;
				Debug.WriteLine($"[RecycleBin] 清空异常: {ex.Message}");
			}

			if (!ok)
			{
				FailOperation(op, error ?? new IOException(ML.RecycleEmptyFailed));
				return;
			}

			CompleteOperation(op, 0, 0);
			await _uiDispatcherQueue.EnqueueAsync(async () =>
			{
				await node.ReloadChildrenAsync();
				_displayedFolderNode = null;
				await UpdateCurrentFolderContentAsync(node, version: null);
				NotifyViewCountChanged();
			});
		}

		private void RemoveRecycleItemFromView(FileSystemNodeViewModel item)
		{
			CurrentFolderContent.Remove(item);
			if (SelectedFolder != null)
				SelectedFolder.Children.Remove(item);
			NotifyViewCountChanged();
		}

		private Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcherQueue;
		[ObservableProperty] private FileSystemNodeViewModel? _selectedFolder;
		// 当前表格正在展示其内容的文件夹节点：重复进入同一文件夹时跳过无谓的重建
		private FileSystemNodeViewModel? _displayedFolderNode;

		// 后台构建完成的表格数据源（GroupedFileList），由 MiddleFilesView.UpdateGroupedSource 消费，
		// 避免在 UI 线程上做分组构建（SetItems/RebuildFlat）。
		internal GroupedFileList? PendingPrebuiltSource { get; set; }
		internal int PendingPrebuiltVersion { get; set; }

		public bool IsCurrentFolderSpecial => SelectedFolder?.WillSplitToDifferentSorts ?? false;

		partial void OnSelectedFolderChanged(FileSystemNodeViewModel? value)
		{
			var tab = CurrentTab;
			var version = tab != null ? ++tab.NavigationVersion : 1;
			if (tab != null && tab.FolderToRelease != null && tab.FolderToRelease != value)
			{
				// 释放子项缓存并重置加载状态，避免退回该文件夹时显示为空
				tab.FolderToRelease.ReleaseChildren();
				tab.FolderToRelease = null;
			}
			Debug.WriteLine($"\n----Selected:{value?.Name}\n");
			Debug.WriteLine($"OnSelectedFolderChanged called with value: {value?.FullPath ?? "null"}");
			Debug.WriteLine($"Is UI thread? {_uiDispatcherQueue.HasThreadAccess}");
			if (value != null)
			{
				if (tab != null && !tab.IsNavigatingFromHistory && tab.PreviousPath != null && tab.PreviousPath != value.FullPath)
				{
					tab.BackStack.Add(tab.PreviousPath);
					if (tab.BackStack.Count > MaxBackDepth) tab.BackStack.RemoveAt(0);
					tab.ForwardStack.Clear();
				}
				if (tab != null)
				{
					tab.PreviousPath = value.FullPath;
					tab.Path = value.FullPath;
					tab.FolderNode = value;
					tab.Title = GetTabTitle(value.FullPath);
				}
				CanGoBack = tab != null && tab.BackStack.Count > 0;
				CanGoForward = tab != null && tab.ForwardStack.Count > 0;
				_ = UpdateCurrentFolderContentAsync(value, version);
				// 保存上次访问路径（防抖，避免频繁写入磁盘）
				DebounceSaveLastVisitedPath(value.FullPath);
			}
			else
			{
				CurrentFolderContent.Clear();
				NotifyViewCountChanged();
			}
		}

		public async Task UpdateCurrentFolderContentAsync(FileSystemNodeViewModel? folder, int? version)
		{
			if (folder == null)
			{
				_uiDispatcherQueue.TryEnqueue(() =>
				{
					CurrentFolderContent.Clear();
					NotifyViewCountChanged();
				});
				return;
			}

			CancelRename();

			// 守卫0: 表格已在展示同一个文件夹且子项已加载（且无待选中项）时，
			// 内容与 Children 保持一致，跳过 Clear+Add 重建，避免点击当前目录等场景卡顿
			if (ReferenceEquals(folder, _displayedFolderNode) && folder.IsLoaded && CurrentTab?.PendingSelectPath == null)
				return;

			// 守卫1: 开始异步加载前先检查——过期任务跳过磁盘 I/O
			if (version.HasValue && version.Value != (CurrentTab?.NavigationVersion ?? -1)) return;

			try
			{
				// 导航开始：先更新面包屑等轻量状态；旧表内容保留到新内容就绪后一次性瞬间切换，
				// 避免“先清空 → 空白等待 → 内容才出现”造成的卡感。
				await _uiDispatcherQueue.EnqueueAsync(() =>
				{
					if (version.HasValue && version.Value != (CurrentTab?.NavigationVersion ?? -1)) return;
					CurrentBreadcrumbPath = folder.FullPath;
				});

				// 确保子项已加载（后台有序枚举 + 后台构建节点）
				if (!folder.IsLoaded)
				{
					await folder.LoadChildrenAsync();
					}
				else
				{
					}

				// 先在 UI 线程快照 Children（轻量引用拷贝），再在后台构建分组扁平源，
				// 最后回到 UI 一次性挂载。避免分组构建（SetItems/RebuildFlat）占用 UI 线程造成迟滞。
				var items = folder.Children.Where(n => !n.IsPlaceholder).ToList();
				var special = folder.WillSplitToDifferentSorts;
				var v = version ?? (CurrentTab?.NavigationVersion ?? 1);

				var (prebuilt, newContent) = await Task.Run(() =>
				{
					var g = new GroupedFileList();
					g.SetItems(items, special);
					var nc = new ObservableCollection<FileSystemNodeViewModel>(items);
					return (g, nc);
				});

				await _uiDispatcherQueue.EnqueueAsync(() =>
				{
					if (v != (CurrentTab?.NavigationVersion ?? -1)) return;
					PendingPrebuiltSource = prebuilt;
					PendingPrebuiltVersion = v;
					CurrentFolderContent = newContent;
					OnPropertyChanged(nameof(IsCurrentFolderSpecial));
					NotifyViewCountChanged();
					_displayedFolderNode = folder;
					var tab = CurrentTab;
					if (tab?.PendingSelectPath != null)
					{
						var pending = tab.PendingSelectPath;
						tab.PendingSelectPath = null;
						var target = CurrentFolderContent.FirstOrDefault(n => string.Equals(n.FullPath, pending, StringComparison.OrdinalIgnoreCase));
						if (target != null)
							SelectItemRequested?.Invoke(target);
					}
				});
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[UpdateCurrentFolderContent] Error: {ex.Message}");
			}
		}

		public void OpenFileLocation(FileSystemNodeViewModel item)
		{
			var parent = Path.GetDirectoryName(item.FullPath);
			if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;
			if (CurrentTab != null) CurrentTab.PendingSelectPath = item.FullPath;
			ExitSearchMode();
			NavigateToPath(parent);
		}

		public void OpenItem(FileSystemNodeViewModel item)
		{
			// 回收站条目没有真实可打开路径：请用右键“还原/彻底删除”
			if (item.IsRecycleEntry) return;
			if (IsSearchMode)
			{
				if (item.IsDirectory) { ExitSearchMode(); NavigateToPath(item.FullPath); }
				else OpenWithDefaultProgram(item.FullPath);
				return;
			}
			if (item.IsDirectory)
			{
				// 相同引用时 [ObservableProperty] 会跳过通知，需手动强制刷新
				if (ReferenceEquals(item, SelectedFolder))
				{
					_ = UpdateCurrentFolderContentAsync(item, version: null);
					return;
				}
				if (SelectedFolder?.IsStandalone == true && CurrentTab != null)
					CurrentTab.FolderToRelease = SelectedFolder;
				SelectedFolder = item;

			}
			else if (TryOpenAsArchive(item))
			{
				// 已作为压缩包预览进入
			}
			else if (item.IsArchiveEntry)
			{
				_ = OpenArchiveEntryFileAsync(item);
			}
			else if (!item.IsDirectory)
			{
				OpenWithDefaultProgram(item.FullPath);
			}
		}

		// 若为受支持的压缩包文件，则进入其内部预览（地址栏变为 xxx.zip\）
		private bool TryOpenAsArchive(FileSystemNodeViewModel item)
		{
			if (item.IsArchiveEntry) return false;
			if (item.IsDirectory) return false;
			if (!ArchiveHelper.IsArchiveExtension(item.Extension)) return false;

			var browser = App.PluginManager?.GetArchiveBrowsers()
				.FirstOrDefault(b => b.CanBrowse(item.FullPath));
			if (browser == null) return false;

			var node = FileSystemNodeViewModel.CreateArchiveRoot(item.FullPath, AppConfigs!, _uiDispatcherQueue);
			SelectedFolder = node;
			return true;
		}

		// 打开压缩包内部的文件：先解压到临时目录再用默认程序打开
		private async Task OpenArchiveEntryFileAsync(FileSystemNodeViewModel item)
		{
			try
			{
				var browser = App.PluginManager?.GetArchiveBrowsers()
					.FirstOrDefault(b => b.CanBrowse(item.ArchiveFilePath));
				if (browser == null) return;

				var tempPath = await browser.ExtractEntryToTempAsync(item.ArchiveFilePath, item.ArchiveRelativePath);
				if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
				{
					OpenWithDefaultProgram(tempPath);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[OpenArchiveEntryFile] Failed: {ex.Message}");
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
			if (IsSearchMode) ExitSearchMode();
			// 支持相对路径：以当前文件夹为基准解析为绝对路径（如地址栏输入 ..新建文件夹）
			if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path) && SelectedFolder != null && !IsVirtualRootToken(path))
				path = Path.GetFullPath(Path.Combine(SelectedFolder.FullPath, path));
			if (ArchiveHelper.IsArchiveVirtualPath(path, out var archiveFile, out var relative))
			{
				NavigateToArchivePath(archiveFile, relative);
				return;
			}
			var target = FindNodeByPathFast(path);
			if (target != null)
			{
				// 相同引用时 [ObservableProperty] 会跳过通知：仅做轻量重绘（已展示时会被守卫跳过），
				// 不做磁盘重载——刷新按钮走 RefreshCurrentFolderAsync
				if (ReferenceEquals(target, SelectedFolder))
					_ = UpdateCurrentFolderContentAsync(target, version: null);
				else
					SelectedFolder = target;
			}
			else
				NavigateToNewPath(path);
		}

		/// <summary>
		/// 为固定栏等独立节点寻找最优导航目标：
		/// 当前文件夹 → 直接子项 → 同目录（父目录子项，固定栏同目录切换最常见）→ 祖先链。
		/// 全部为 O(子项数)/O(深度)，不做全树递归；未命中时回退到传入的节点自身
		/// （其可能已在之前的访问中加载过）。
		/// </summary>
		public FileSystemNodeViewModel? FindBestNodeForNavigation(FileSystemNodeViewModel fallback)
		{
			if (fallback == null || !fallback.IsDirectory) return fallback;
			var path = fallback.FullPath;
			var current = SelectedFolder;
			if (current != null && string.Equals(current.FullPath, path, StringComparison.OrdinalIgnoreCase))
				return current;

			// 当前文件夹的直接子项
			if (current?.IsLoaded == true)
			{
				foreach (var child in current.Children)
				{
					if (!child.IsPlaceholder && string.Equals(child.FullPath, path, StringComparison.OrdinalIgnoreCase))
						return child;
				}
			}

			// 同目录切换：当前文件夹的父目录中的同级节点（固定栏在同目录内切换两个文件夹）
			var parent = current?.Parent;
			if (parent?.IsLoaded == true)
			{
				foreach (var child in parent.Children)
				{
					if (!child.IsPlaceholder && string.Equals(child.FullPath, path, StringComparison.OrdinalIgnoreCase))
						return child;
				}
			}

			// 祖先链（向上/后退）
			for (var ancestor = parent; ancestor != null; ancestor = ancestor.Parent)
			{
				if (string.Equals(ancestor.FullPath, path, StringComparison.OrdinalIgnoreCase))
					return ancestor;
			}

			return fallback;
		}

		/// <summary>
		/// 查找导航目标节点。先走 O(1)/O(子项数)/O(深度) 的快速路径（当前文件夹、直接子项、祖先链），
		/// 避免面包屑/后退/前进/地址栏等每次导航都对整棵已加载目录树做递归搜索造成小卡顿；
		/// 快速路径未命中才回退到全树递归查找（用于树中其它分支的节点）。
		/// </summary>
		private FileSystemNodeViewModel? FindNodeByPathFast(string fullPath)
		{
			var current = SelectedFolder;
			if (current != null && string.Equals(current.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
				return current;

			// 最常见场景：导航到当前文件夹的直接子项（面包屑下一级/后退/地址栏）
			if (current != null && current.IsLoaded)
			{
				foreach (var child in current.Children)
				{
					if (!child.IsPlaceholder && string.Equals(child.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
						return child;
				}
			}

			// 祖先链（向上按钮/后退/面包屑上级）：沿 Parent 指针逐级向上，O(深度)
			for (var ancestor = current?.Parent; ancestor != null; ancestor = ancestor.Parent)
			{
				if (string.Equals(ancestor.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
					return ancestor;
			}

			return FindNodeByPath(fullPath);
		}

		private void NavigateToArchivePath(string archiveFile, string relative)
		{
			var browser = App.PluginManager?.GetArchiveBrowsers()
				.FirstOrDefault(b => b.CanBrowse(archiveFile));
			if (browser == null)
			{
				OpenWithDefaultProgram(archiveFile);
				return;
			}
			if (SelectedFolder?.IsStandalone == true && CurrentTab != null)
				CurrentTab.FolderToRelease = SelectedFolder;
			var node = FileSystemNodeViewModel.CreateArchiveDirectory(archiveFile, relative, AppConfigs!, _uiDispatcherQueue);
			node.IsStandalone = true;
			if (CurrentTab != null) CurrentTab.PreviousPath = null;
			SelectedFolder = node;
		}

		private void NavigateToNewPath(string path)
		{
			if (!Directory.Exists(path)) return;

			// 盘符根目录（C:\、D:\…）复用“此电脑”下的缓存节点，保证 Parent 指向此电脑
			if (path.Length == 3 && path.EndsWith(":\\"))
			{
				var cachedDrive = GetDriveNodeByPath(path);
				if (cachedDrive != null)
				{
					cachedDrive.IsStandalone = false;
					SelectedFolder = cachedDrive;
					return;
				}
			}

			if (SelectedFolder?.IsStandalone == true && CurrentTab != null)
				CurrentTab.FolderToRelease = SelectedFolder;
			// lazyLoad: true —— 目录枚举统一由 UpdateCurrentFolderContentAsync → LoadChildrenAsync 完成一次，
			// 避免构造函数里 StartAsyncCount 再全量枚举一遍（面包屑/地址栏/后退进入新路径更跟手）
			var node = new FileSystemNodeViewModel(path, true, false, _appConfigs, _uiDispatcherQueue, true);
			node.IsStandalone = true;
			if (CurrentTab != null) CurrentTab.PreviousPath = null;
			SelectedFolder = node;
		}

		private void GoBack()
		{
			var tab = CurrentTab;
			if (tab == null || tab.BackStack.Count == 0) return;
			tab.IsNavigatingFromHistory = true;
			tab.ForwardStack.Add(tab.PreviousPath ?? _selectedFolder?.FullPath ?? "");
			if (tab.ForwardStack.Count > MaxBackDepth) tab.ForwardStack.RemoveAt(0);
			var path = tab.BackStack[^1]; tab.BackStack.RemoveAt(tab.BackStack.Count - 1);
			tab.PreviousPath = null;
			NavigateToPath(path);
			CanGoBack = tab.BackStack.Count > 0;
			CanGoForward = tab.ForwardStack.Count > 0;
			tab.IsNavigatingFromHistory = false;
		}

		private void GoForward()
		{
			var tab = CurrentTab;
			if (tab == null || tab.ForwardStack.Count == 0) return;
			tab.IsNavigatingFromHistory = true;
			tab.BackStack.Add(tab.PreviousPath ?? _selectedFolder?.FullPath ?? "");
			if (tab.BackStack.Count > MaxBackDepth) tab.BackStack.RemoveAt(0);
			var path = tab.ForwardStack[^1]; tab.ForwardStack.RemoveAt(tab.ForwardStack.Count - 1);
			tab.PreviousPath = null;
			NavigateToPath(path);
			CanGoBack = tab.BackStack.Count > 0;
			CanGoForward = tab.ForwardStack.Count > 0;
			tab.IsNavigatingFromHistory = false;
		}

		private void GoUp()
		{
			var node = _selectedFolder;
			if (node == null) return;

			// 树形导航优先：驱动器的父级是“此电脑”，普通文件夹的父级是同树节点
			if (node.Parent != null && node.Parent.IsDirectory && !ReferenceEquals(node.Parent, node))
			{
				if (ReferenceEquals(SelectedFolder, node.Parent))
					_ = UpdateCurrentFolderContentAsync(node.Parent, version: null);
				else
					SelectedFolder = node.Parent;
				return;
			}

			var parentPath = GetParentPath(node.FullPath);
			if (parentPath == null) return;
			NavigateToPath(parentPath);
		}

		private string? GetParentPath(string path)
		{
			if (string.IsNullOrEmpty(path)) return null;
			if (ArchiveHelper.IsArchiveVirtualPath(path, out var archiveFile, out var relative))
			{
				if (string.IsNullOrEmpty(relative))
					return ArchiveHelper.GetParentOfArchiveRoot(archiveFile);
				var parts = relative.TrimEnd('\\').Split('\\');
				if (parts.Length <= 1)
					return archiveFile;
				return ArchiveHelper.CombineArchiveVirtualPath(archiveFile, string.Join("\\", parts.Take(parts.Length - 1)));
			}
			if (path.EndsWith(":\\") || path == "\\\\")
				return null;
			if (path.StartsWith("\\\\"))
			{
				var parts = path.TrimEnd('\\').Split('\\');
				if (parts.Length <= 2) return GetWslServerParent(parts[0]);
				return string.Join("\\", parts.Take(parts.Length - 1));
			}
			var parent = Directory.GetParent(path);
			return parent?.FullName;
		}

		/// <summary>\wsl$ / \wsl.localhost 下的发行版根向上一级时回到 Linux 侧栏节点。</summary>
		private string? GetWslServerParent(string serverName)
		{
			if (string.IsNullOrEmpty(serverName)) return null;
			var server = "\\\\" + serverName;
			foreach (var root in RootDirectories)
			{
				if (root.NodeKind == FileNodeKind.Wsl &&
					string.Equals(root.FullPath.TrimEnd('\\'), server, StringComparison.OrdinalIgnoreCase))
					return root.FullPath;
			}
			return null;
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


		/// <summary>目标文件夹当前是否已固定到系统快速访问。</summary>
		public bool IsFolderPinned(FileSystemNodeViewModel? folder)
			=> folder != null && !folder.IsPlaceholder && folder.IsDirectory
				&& !string.IsNullOrEmpty(folder.FullPath) && QuickAccessHelper.IsPinned(folder.FullPath);

		/// <summary>固定/取消固定到系统快速访问（右键菜单与 Ctrl+P 共用入口）。</summary>
		public async Task TogglePinnedFolderAsync(FileSystemNodeViewModel? folder)
		{
			if (folder == null || folder.IsPlaceholder || !folder.IsDirectory) return;
			if (string.IsNullOrEmpty(folder.FullPath) || !Directory.Exists(folder.FullPath)) return;
			try
			{
				await Task.Run(() => QuickAccessHelper.TogglePin(folder.FullPath));
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[PinToggle] 失败: {ex.Message}");
			}
			await RefreshPinnedShortcutsAsync();
		}

		/// <summary>重新从系统快速访问读取并刷新左侧“已固定”栏。</summary>
		public async Task RefreshPinnedShortcutsAsync()
		{
			List<string> pinnedPaths;
			try { pinnedPaths = await Task.Run(() => QuickAccessHelper.GetPinnedFolderPaths()); }
			catch (Exception ex) { Debug.WriteLine($"[PinToggle] 读取快速访问失败: {ex.Message}"); return; }
			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				PinnedShortcuts.Clear();
				foreach (var path in pinnedPaths)
				{
					if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
						PinnedShortcuts.Add(new FileSystemNodeViewModel(path, true, false, AppConfigs!, _uiDispatcherQueue, true));
				}
			});
		}

		private void InitializePinnedShortcuts(Configs configs, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcherQueue)
		{
			var pinnedPaths = GetQuickAccessPinnedFolders();
			foreach (var path in pinnedPaths)
			{
				if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
				{
					var node = new FileSystemNodeViewModel(path, true, false, configs, uiDispatcherQueue, true);
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
