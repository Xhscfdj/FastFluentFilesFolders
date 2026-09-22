using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FastFluentFilesFolders.Services
{
	/// <summary>
	/// 系统“快速访问”(Quick Access) 固定/取消固定帮助类。
	/// 读取：枚举 shell:::{679f85cb-0220-4080-b29b-5540cc05aab6} 命名空间（与资源管理器一致）。
	/// 切换：对目标文件夹调用 Shell 动词 "pintohome"（未固定→固定；已固定→取消固定），
	/// 已在 Win10/Win11 实测可用；未打包模式写入系统快速访问正常。
	/// </summary>
	public static class QuickAccessHelper
	{
		private const string QuickAccessClsidPath = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";
		private const string PinVerb = "pintohome";

		private static readonly object SyncLock = new();
		private static List<string>? _cachedPinned;
		private static DateTime _cacheTime;

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
				Debug.WriteLine($"[QuickAccess] 枚举快速访问失败: {ex.Message}");
			}
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
