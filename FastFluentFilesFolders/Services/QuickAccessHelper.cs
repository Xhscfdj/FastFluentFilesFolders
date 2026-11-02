using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FastFluentFilesFolders.Services
{
	/// <summary>
	/// 系统“快速访问”(Quick Access) 固定/取消固定帮助类。
	/// <para>普通构建：读取用时枚举 shell:::{679f85cb-0220-4080-b29b-5540cc05aab6} 命名空间，
	/// 切换时对目标文件夹调用 Shell 动词 "pintohome"（未固定→固定；已固定→取消固定）。</para>
	/// <para>Native AOT（无内置 COM）：改不动系统快速访问，固定项改为存在
	/// <see cref="Configs.AotPinnedFolders"/>（`user_configs.json` 的 `General:AotPinnedFolders`），
	/// 首次为空时以「已知文件夹」为初始内容。</para>
	/// </summary>
	public static class QuickAccessHelper
	{
		private const string QuickAccessClsidPath = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";
		private const string PinVerb = "pintohome";

		private static readonly object SyncLock = new();
		private static List<string>? _cachedPinned;
		private static DateTime _cacheTime;

		/// <summary>配置实例（由 <see cref="App"/> 注入），AOT 下固定项存在这里。</summary>
		public static ViewModels.Configs? Configs { get; set; }

		/// <summary>固定项发生变化后的回调（由 ViewModel 接到防抖落盘）。</summary>
		public static Action? PinsChanged { get; set; }

		/// <summary>当前“快速访问”中仍存在的文件夹路径列表（5 秒缓存，避免每次右键都枚举）。</summary>
		public static List<string> GetPinnedFolderPaths()
		{
			lock (SyncLock)
			{
				if (_cachedPinned != null && (DateTime.UtcNow - _cacheTime).TotalSeconds < 5)
					return _cachedPinned.ToList();
				_cachedPinned = EnumeratePinnedFolderPaths();
				_cacheTime = DateTime.UtcNow;
				return _cachedPinned.ToList();
			}
		}

		private static List<string> EnumeratePinnedFolderPaths()
		{
			// 首选：裸 vtable COM 调用（不经过托管↔COM 封送，AOT 下同样可用），
			// 结果与资源管理器的「快速访问」一致。
			var native = ShellQuickAccess.EnumeratePinnedFolders();
			if (native.Count > 0)
				return native;

			// 其次：内置 COM 可用时用 Shell.Application（覆盖裸调用偶发失败的情况）。
			if (IsShellComAvailable)
			{
				var result = new List<string>();
				try
				{
					Type shellType = Type.GetTypeFromProgID("Shell.Application", true)!;
					dynamic shell = Activator.CreateInstance(shellType)!;
					dynamic quickAccess = shell.NameSpace(QuickAccessClsidPath);
					if (quickAccess == null) return result;
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
				catch (Exception ex)
				{
					Debug.WriteLine($"[QuickAccess] Shell.Application 枚举失败: {ex.Message}");
				}
				if (result.Count > 0)
					return result;
			}

			// 最后：配置里的固定列表 / 已知文件夹（两者都不可用时的兜底，至少侧栏不是空的）。
			return GetAotPinnedPaths();
		}

		/// <summary>
		/// AOT 下的固定项：优先用配置里存的那份（用户在我们的应用里 pin/unpin 出来的），
		/// 配置为空时以「已知文件夹」作为初始内容（只读，不落盘，直到用户真正 pin/unpin 一次）。
		/// </summary>
		private static List<string> GetAotPinnedPaths()
		{
			var configured = Configs?.AotPinnedFolders;
			if (configured != null)
			{
				var list = configured
					.Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
					.ToList();
				if (list.Count > 0)
					return list;
			}
			return GetKnownFolderFallbackPaths();
		}

		/// <summary>AOT 下的固定/取消固定：在配置列表里增删（首次操作先把「已知文件夹」落成初始内容）。</summary>
		private static bool TogglePinInConfig(string fullPath)
		{
			var configs = Configs;
			if (configs?.AotPinnedFolders == null) return false;

			lock (SyncLock)
			{
				var list = configs.AotPinnedFolders.ToList();
				if (list.Count == 0)
					list = GetKnownFolderFallbackPaths();

				var target = Normalize(fullPath);
				var existing = list.FirstOrDefault(p => string.Equals(Normalize(p), target, StringComparison.OrdinalIgnoreCase));
				if (existing != null)
					list.Remove(existing);
				else
				{
					var toAdd = fullPath.TrimEnd('\\');
					list.Add(string.IsNullOrEmpty(toAdd) ? fullPath : toAdd);
				}

				configs.AotPinnedFolders = new System.Collections.ObjectModel.ObservableCollection<string>(list);
				_cachedPinned = list.Where(p => Directory.Exists(p)).ToList();
				_cacheTime = DateTime.UtcNow;
			}

			try { PinsChanged?.Invoke(); }
			catch (Exception ex) { Debug.WriteLine($"[QuickAccess] 保存固定项失败: {ex.Message}"); }
			return true;
		}

		/// <summary>
		/// 当前运行时是否还能用内置 COM（`Type.GetTypeFromProgID` / `dynamic`）。
		/// Native AOT 发布、以及任何把 <c>BuiltInComInterop.IsSupported</c> 关掉的构建都会返回 false。
		/// </summary>
		private static bool IsShellComAvailable
		{
			get
			{
				if (AppContext.TryGetSwitch(
						"System.Runtime.InteropServices.BuiltInComInterop.IsSupported", out var comSupported))
					return comSupported;
				return System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;
			}
		}

		/// <summary>无 Shell COM 时的兜底列表：桌面 / 下载 / 文档 / 图片 / 音乐 / 视频（顺序与资源管理器一致）。</summary>
		private static List<string> GetKnownFolderFallbackPaths()
		{
			var result = new List<string>();

			void AddIfExists(string? path)
			{
				if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && !result.Contains(path, StringComparer.OrdinalIgnoreCase))
					result.Add(path);
			}

			AddIfExists(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
			AddIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
			AddIfExists(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
			AddIfExists(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
			AddIfExists(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
			AddIfExists(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
			return result;
		}

		/// <summary>目标文件夹当前是否出现在快速访问中（固定 或 常用）。</summary>
		public static bool IsPinned(string? fullPath)
		{
			if (string.IsNullOrWhiteSpace(fullPath)) return false;
			var target = Normalize(fullPath);
			return GetPinnedFolderPaths().Any(p => string.Equals(Normalize(p), target, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>切换固定状态：未固定 → 固定；已固定 → 取消固定。返回是否成功发起。</summary>
		public static bool TogglePin(string? fullPath)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(fullPath) || !Directory.Exists(fullPath))
					return false;

				// 首选：裸 vtable COM 调用（AOT 下同样可用），真正写进系统「快速访问」。
				var currentlyPinned = IsPinned(fullPath);
				if (ShellQuickAccess.SetPinned(fullPath, !currentlyPinned))
				{
					InvalidateCache();
					return true;
				}

				// 其次：内置 COM 可用时走 Shell.Application 的 pintohome（覆盖裸调用偶发失败的情况）。
				if (IsShellComAvailable)
				{
					var trimmed = fullPath.TrimEnd('\\');
					var parent = Path.GetDirectoryName(trimmed);
					var name = Path.GetFileName(trimmed);
					if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) return false;

					Type shellType = Type.GetTypeFromProgID("Shell.Application", true)!;
					dynamic shell = Activator.CreateInstance(shellType)!;
					dynamic? folder = shell.NameSpace(parent);
					dynamic? item = folder?.ParseName(name);
					if (item == null) return false;
					item.InvokeVerb(PinVerb);
					InvalidateCache();
					return true;
				}

				// 最后：连 COM 都没有时，退化成操作我们自己的固定列表（写在 user_configs.json）
				return TogglePinInConfig(fullPath);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[QuickAccess] 切换固定失败: {ex.Message}");
				return false;
			}
		}

		/// <summary>清除缓存，下次读取时强制重新枚举（切换固定后立即刷新用）。</summary>
		public static void InvalidateCache()
		{
			lock (SyncLock) { _cachedPinned = null; }
		}

		private static string Normalize(string path) => path.TrimEnd('\\', '/');
	}
}
