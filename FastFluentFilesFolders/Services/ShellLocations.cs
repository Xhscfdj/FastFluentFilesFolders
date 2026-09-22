using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace FastFluentFilesFolders.Services
{
	/// <summary>一个已检测到的云盘本地根目录。</summary>
	public sealed record CloudRootInfo(string DisplayName, string Path);

	/// <summary>回收站内一个条目（COM 枚举快照，可安全在 UI 线程使用）。</summary>
	public sealed class RecycleBinEntry
	{
		public required string Name { get; init; }
		public required bool IsDirectory { get; init; }
		/// <summary>被删除前的完整路径（用于匹配 COM 条目与取扩展名图标）。</summary>
		public required string OriginalFullPath { get; init; }
		/// <summary>被删除前所在的文件夹（原位置）。</summary>
		public required string OriginalLocation { get; init; }
		public long Size { get; init; }
		public DateTime ModifiedUtc { get; init; }
		/// <summary>本次枚举序号（执行还原/删除时优先用索引匹配，避免同名歧义）。</summary>
		public int Index { get; init; }
	}

	/// <summary>
	/// 侧栏“网络 / Linux(WSL) / 云盘”等位置枚举辅助。
	/// 网络与回收站条目通过 Scripting Shell (Shell.Application) COM 枚举，
	/// 与快速访问固定文件夹的现有做法一致。
	/// </summary>
	public static class ShellLocations
	{
		private const int NamespaceNetwork = 18;
		// Windows 资源管理器“Linux”（WSL 发行版）命名空间 CLSID
		private const string LinuxNamespaceClsid = "::{B2B4A4D1-2754-4140-A2EB-9A76D9D7CDC6}";

		private static dynamic CreateShell()
		{
			var type = Type.GetTypeFromProgID("Shell.Application", true)!;
			return Activator.CreateInstance(type)!;
		}

		/// <summary>网络邻居中的计算机根（形如 \\HOST）。不可用时返回空列表。</summary>
		public static List<string> GetNetworkComputerPaths()
		{
			var result = new List<string>();
			try
			{
				dynamic shell = CreateShell();
				dynamic net = shell.NameSpace(NamespaceNetwork);
				if (net == null) return result;
				dynamic items = net.Items();
				int count = (int)items.Count;
				for (int i = 0; i < count; i++)
				{
					try
					{
						dynamic item = items.Item(i);
						string? path = item.Path as string;
						if (!string.IsNullOrEmpty(path) && path.StartsWith("\\", StringComparison.Ordinal))
							result.Add(path);
					}
					catch { }
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ShellLocations] 枚举网络失败: {ex.Message}");
			}
			return result;
		}

		/// <summary>
		/// 枚举 WSL 发行版根目录。优先使用 Shell 的“Linux”命名空间（与资源管理器一致：
		/// 即使 \\wsl$ / \\wsl.localhost 根本身无法通过 Directory.Exists 访问，也能列出发行版），
		/// 失败时再回退到对旧式 \\wsl$ 前缀的目录枚举。
		/// </summary>
		public static List<string> GetWslDistroRootPaths(string preferredPrefix)
		{
			var roots = new List<string>();

			try
			{
				dynamic shell = CreateShell();
				dynamic linux = shell.NameSpace(LinuxNamespaceClsid);
				if (linux != null)
				{
					dynamic items = linux.Items();
					int count = (int)items.Count;
					for (int i = 0; i < count; i++)
					{
						try
						{
							dynamic item = items.Item(i);
							string? path = item.Path as string;
							if (!string.IsNullOrEmpty(path) && !roots.Contains(path, StringComparer.OrdinalIgnoreCase))
								roots.Add(path);
						}
						catch { }
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ShellLocations] 枚举 Linux(WSL) 命名空间失败: {ex.Message}");
			}

			if (roots.Count == 0)
			{
				var prefixes = new List<string>();
				foreach (var p in new[] { preferredPrefix, @"\\wsl$", @"\\wsl.localhost" })
					if (!string.IsNullOrEmpty(p) && !prefixes.Contains(p, StringComparer.OrdinalIgnoreCase))
						prefixes.Add(p);

				foreach (var prefix in prefixes)
				{
					try
					{
						if (!Directory.Exists(prefix)) continue;
						foreach (var dir in Directory.EnumerateDirectories(prefix))
						{
							if (!roots.Contains(dir, StringComparer.OrdinalIgnoreCase))
								roots.Add(dir);
						}
						if (roots.Count > 0) break; // 找到可用的 WSL 前缀后不再尝试另一个
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"[ShellLocations] 枚举 WSL({prefix}) 失败: {ex.Message}");
					}
				}
			}

			roots.Sort(StringComparer.CurrentCultureIgnoreCase);
			return roots;
		}

		/// <summary>
		/// 选择 WSL 的 UNC 根前缀（\\wsl.localhost 或 \\wsl$）。
		/// 从 Linux 命名空间枚举出的真实发行版路径推导，保证与实际可访问前缀一致。
		/// </summary>
		public static string PickWslRootPath()
		{
			try
			{
				var roots = GetWslDistroRootPaths(string.Empty);
				if (roots.Count > 0 && roots[0].StartsWith(@"\\", StringComparison.Ordinal))
				{
					int idx = roots[0].IndexOf('\\', 2);
					if (idx > 2)
						return roots[0].Substring(0, idx);
				}
			}
			catch { }

			if (Directory.Exists(@"\\wsl.localhost")) return @"\\wsl.localhost";
			if (Directory.Exists(@"\\wsl$")) return @"\\wsl$";
			return @"\\wsl$";
		}

		/// <summary>
		/// 驱动器显示名，例如 “本地磁盘 (C:)”“U盘 (E:)”。
		/// ml 是一个按 key 取本地化文本的委托（通常传 App.ML[key]）。
		/// </summary>
		public static string FormatDriveDisplayName(DriveInfo drive, Func<string, string> ml)
		{
			string typeLabel = drive.DriveType switch
			{
				DriveType.Fixed => ml("Drive.LocalDisk"),
				DriveType.Removable => ml("Drive.Removable"),
				DriveType.Network => ml("Drive.Network"),
				DriveType.CDRom => ml("Drive.CD"),
				DriveType.Ram => ml("Drive.Ram"),
				_ => ml("Drive.Unknown")
			};
			string volume = string.Empty;
			try { volume = drive.VolumeLabel; } catch { }
			if (!string.IsNullOrWhiteSpace(volume))
				return $"{volume} ({drive.Name[0]}:)";
			return $"{typeLabel} ({drive.Name[0]}:)";
		}
	}

	/// <summary>自动检测本机常见云盘同步目录。检测不到的自动隐藏。</summary>
	public static class CloudDriveDetector
	{
		public static List<CloudRootInfo> DetectCloudRoots()
		{
			var result = new List<CloudRootInfo>();
			var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if (string.IsNullOrEmpty(profile)) return result;

			void Add(string displayName, string path)
			{
				try
				{
					if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) &&
						!result.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
						result.Add(new CloudRootInfo(displayName, path));
				}
				catch { }
			}

			// OneDrive 个人版
			Add("OneDrive", Path.Combine(profile, "OneDrive"));

			// OneDrive 商业版/学校版：OneDrive - xxx
			try
			{
				foreach (var dir in Directory.EnumerateDirectories(profile))
				{
					var name = Path.GetFileName(dir);
					if (name.StartsWith("OneDrive - ", StringComparison.OrdinalIgnoreCase))
						Add(name, dir);
				}
			}
			catch { }

			// Dropbox
			Add("Dropbox", Path.Combine(profile, "Dropbox"));

			// Google Drive：真实根是 “Google Drive\My Drive”
			var googleRoot = Path.Combine(profile, "Google Drive");
			if (Directory.Exists(googleRoot))
			{
				var myDrive = Path.Combine(googleRoot, "My Drive");
				Add("Google Drive", Directory.Exists(myDrive) ? myDrive : googleRoot);
			}

			// iCloud Drive（新旧客户端目录名不同）
			Add("iCloud Drive", Path.Combine(profile, "iCloudDrive"));
			Add("iCloud Drive", Path.Combine(profile, "iCloud Drive"));

			// 坚果云 Nutstore
			Add("Nutstore", Path.Combine(profile, "Nutstore"));
			Add("Nutstore", Path.Combine(profile, "我的坚果云"));

			// MEGA
			Add("MEGA", Path.Combine(profile, "MEGA"));

			// 百度网盘（本地下载目录常见名）
			Add("Baidu Netdisk", Path.Combine(profile, "BaiduNetdiskDownload"));

			return result;
		}
	}

	/// <summary>
	/// 回收站操作服务：枚举条目 + 还原/彻底删除/清空。
	/// 底层使用 Shell.Application 的 FolderItem COM 对象；所有 COM 调用都发生在调用线程上，
	/// UI 只接触普通数据对象（RecycleBinEntry），不跨线程持有 COM 引用。
	/// </summary>
	public static class RecycleBinService
	{
		private const uint SHERB_NOCONFIRMATION = 0x00000001;
		private const uint SHERB_NOPROGRESSUI = 0x00000002;
		private const uint SHERB_NOSOUND = 0x00000004;

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

		private enum RecycleVerbKind { Restore, Delete }

		private static dynamic CreateShell()
		{
			var type = Type.GetTypeFromProgID("Shell.Application", true)!;
			return Activator.CreateInstance(type)!;
		}

		private static dynamic GetRecycleBin() => CreateShell().NameSpace(10);

		public static List<RecycleBinEntry> EnumerateEntries()
		{
			var result = new List<RecycleBinEntry>();
			try
			{
				dynamic bin = GetRecycleBin();
				dynamic items = bin.Items();
				int count = (int)items.Count;
				for (int i = 0; i < count; i++)
				{
					try
					{
						dynamic item = items.Item(i);
						string name = (item.Name as string) ?? string.Empty;
						bool isDir = false;
						try { isDir = (bool)item.IsFolder; } catch { }

						string? originalFull = null;
						try { originalFull = item.Path as string; } catch { }

						string? deletedFrom = null;
						try
						{
							object? ext = item.ExtendedProperty("System.Recycle.DeletedFrom");
							deletedFrom = ext?.ToString();
						}
						catch { }

						string originalLocation = !string.IsNullOrWhiteSpace(deletedFrom)
							? deletedFrom.Trim()
							: (string.IsNullOrWhiteSpace(originalFull) ? string.Empty : Path.GetDirectoryName(originalFull) ?? string.Empty);

						long size = -1;
						try { size = Convert.ToInt64(item.Size); } catch { }

						DateTime modified = DateTime.MinValue;
						try { modified = Convert.ToDateTime(item.ModifyDate); } catch { }

						result.Add(new RecycleBinEntry
						{
							Name = name,
							IsDirectory = isDir,
							OriginalFullPath = originalFull ?? string.Empty,
							OriginalLocation = originalLocation,
							Size = size < 0 ? 0 : size,
							ModifiedUtc = modified == DateTime.MinValue ? DateTime.UtcNow : modified.ToUniversalTime(),
							Index = i
						});
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"[RecycleBin] item {i} 读取失败: {ex.Message}");
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[RecycleBin] 枚举失败: {ex.Message}");
			}
			return result;
		}

		/// <summary>还原回收站条目。</summary>
		public static bool RestoreEntry(RecycleBinEntry entry)
		{
			if (entry == null) return false;
			return InvokeVerbOnEntry(entry, RecycleVerbKind.Restore);
		}

		/// <summary>
		/// 彻底删除回收站条目（不可恢复）。
		/// 直接删除回收站内的物理项（$R 文件/文件夹）及配套的 $I 元数据：
		/// 不走 Shell 动词，避免系统再弹一次确认框、也不会因等待确认而超时误报失败。
		/// </summary>
		public static bool DeleteEntry(RecycleBinEntry entry)
		{
			if (entry == null || string.IsNullOrWhiteSpace(entry.OriginalFullPath)) return false;

			try
			{
				var rPath = entry.OriginalFullPath; // 回收站内的物理路径（...\$RXXXX.ext）
				var dir = Path.GetDirectoryName(rPath);
				if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;

				bool deleted = false;
				if (File.Exists(rPath))
				{
					File.Delete(rPath);
					deleted = true;
				}
				else if (Directory.Exists(rPath))
				{
					Directory.Delete(rPath, true);
					deleted = true;
				}

				// 删除配套的 $I 元数据，否则回收站里会残留一个指向已删文件的“幽灵条目”
				var name = Path.GetFileName(rPath);
				if (name.StartsWith("$R", StringComparison.OrdinalIgnoreCase))
				{
					var iPath = Path.Combine(dir, "$I" + name.Substring(2));
					try
					{
						if (File.Exists(iPath)) File.Delete(iPath);
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"[RecycleBin] 删除元数据失败 {iPath}: {ex.Message}");
					}
				}

				return deleted;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[RecycleBin] 彻底删除失败: {ex.Message}");
				return false;
			}
		}

		private static bool InvokeVerbOnEntry(RecycleBinEntry entry, RecycleVerbKind kind)
		{
			// Shell 动词（还原/彻底删除）必须在 STA 线程上执行；
			// 在 MTA 线程（Task.Run）上调用可能“不抛异常但什么都不发生”。
			bool result = false;
			Exception? error = null;
			var thread = new Thread(() =>
			{
				try { result = InvokeVerbOnEntryCore(entry, kind); }
				catch (Exception ex) { error = ex; }
			});
			thread.IsBackground = true;
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();

			var label = kind == RecycleVerbKind.Restore ? "还原" : "彻底删除";
			if (!thread.Join(TimeSpan.FromSeconds(20)))
			{
				Debug.WriteLine($"[RecycleBin] {label}超时: {entry.Name}");
				return false;
			}
			if (error != null)
			{
				Debug.WriteLine($"[RecycleBin] {label}异常: {error.Message}");
				return false;
			}
			return result;
		}

		private static bool InvokeVerbOnEntryCore(RecycleBinEntry entry, RecycleVerbKind kind)
		{
			try
			{
				dynamic bin = GetRecycleBin();
				dynamic items = bin.Items();
				int count = (int)items.Count;

				dynamic? preferred = null;
				dynamic? fallback = null;
				for (int i = 0; i < count; i++)
				{
					dynamic candidate = items.Item(i);
					string candidatePath = string.Empty;
					try { candidatePath = (candidate.Path as string) ?? string.Empty; } catch { }

					bool pathMatches = string.Equals(candidatePath, entry.OriginalFullPath, StringComparison.OrdinalIgnoreCase);
					if (pathMatches && i == entry.Index) { preferred = candidate; break; }
					if (pathMatches && fallback == null) fallback = candidate;
				}

				dynamic target = preferred ?? fallback;
				if (target == null) return false;

				// 关键：用 FolderItemVerb.DoIt() 执行，而不是 InvokeVerb(动词名)。
				// 本地化系统上 InvokeVerb 传名字可能不报错但也不执行任何操作。
				dynamic verbs = target.Verbs();
				int vcount = (int)verbs.Count;
				for (int i = 0; i < vcount; i++)
				{
					dynamic verb = verbs.Item(i);
					string verbName = (verb.Name as string) ?? string.Empty;
					bool matches = kind == RecycleVerbKind.Restore
						? (verbName.IndexOf("estore", StringComparison.OrdinalIgnoreCase) >= 0 || verbName.IndexOf("还原", StringComparison.OrdinalIgnoreCase) >= 0)
						: (verbName.IndexOf("elete", StringComparison.OrdinalIgnoreCase) >= 0 || verbName.IndexOf("删除", StringComparison.OrdinalIgnoreCase) >= 0);
					if (!matches) continue;

					try
					{
						verb.DoIt();
						return true;
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"[RecycleBin] 执行动词失败 {verbName}: {ex.Message}");
					}
				}
				return false;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[RecycleBin] {(kind == RecycleVerbKind.Restore ? "还原" : "彻底删除")}失败: {ex.Message}");
				return false;
			}
		}

		/// <summary>清空回收站（调用方需先自行弹确认框）。</summary>
		public static bool EmptyRecycleBin()
		{
			try
			{
				int hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
				return hr == 0;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[RecycleBin] 清空失败: {ex.Message}");
				return false;
			}
		}
	}
}