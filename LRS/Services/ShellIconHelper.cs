using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace LRS.Services
{
	public class ShellIconHelper : IIconProvider
	{
		// 图标缓存：键格式见 BuildCacheKey
		private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new();

		// 定义需要特殊处理（图标不固定）的扩展名
		private static readonly HashSet<string> SpecialExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".exe",
			".lnk",
			".url",
			".ico",
			".msi",
			".cpl",
			".scr"
		};

		// P/Invoke 定义
		private const uint SHGFI_ICON = 0x100;
		private const uint SHGFI_LARGEICON = 0x0;
		private const uint SHGFI_SMALLICON = 0x1;
		private const uint SHGFI_USEFILEATTRIBUTES = 0x10;

		[DllImport("shell32.dll", CharSet = CharSet.Auto)]
		private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		private struct SHFILEINFO
		{
			public IntPtr hIcon;
			public int iIcon;
			public uint dwAttributes;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
			public string szDisplayName;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
			public string szTypeName;
		}

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool DestroyIcon(IntPtr hIcon);

		/// <summary>
		/// 获取文件/文件夹的系统图标（异步，支持缓存）
		/// </summary>
		public async Task<ImageSource?> GetIconAsync(string fullPath, bool isFolder, DispatcherQueue dispatcherQueue, uint size = 32)
		{
			if (string.IsNullOrEmpty(fullPath))
				return null;
			size = 16;
			bool useLargeIcon = size >= 32;
			string cacheKey = BuildCacheKey(fullPath, isFolder, useLargeIcon);
			if (IsSpecialFolder(fullPath))
				

			// 1. 尝试从缓存获取
			if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
				return cachedIcon;

			// 2. 缓存未命中，加载图标
			var icon = await LoadIconCoreAsync(fullPath, isFolder, useLargeIcon, dispatcherQueue);
			if (icon != null)
			{
				_iconCache.TryAdd(cacheKey, icon);
			}
			return icon;
		}

		/// <summary>
		/// 构建缓存键
		/// </summary>
		private static string BuildCacheKey(string fullPath, bool isFolder, bool useLargeIcon)
		{
			string key;

			if (isFolder)
			{
				// 驱动器根目录单独缓存
				if (IsDriveRoot(fullPath))
					key = $"_drive_{fullPath}";
				else
					key = "_folder_";
			}
			else
			{
				string ext = Path.GetExtension(fullPath).ToLowerInvariant();
				// 特殊扩展名：使用完整路径作为键（确保每个文件独立）
				if (SpecialExtensions.Contains(ext))
				{
					// 将路径转为小写（Windows 路径不区分大小写）
					key = fullPath.ToLowerInvariant();
				}
				else
				{
					key = string.IsNullOrEmpty(ext) ? "_noext_" : ext;
				}
			}

			// 附加尺寸信息
			return $"{key}_{(useLargeIcon ? "32" : "16")}";
		}

		/// <summary>
		/// 判断路径是否为驱动器根目录
		/// </summary>
		private static bool IsDriveRoot(string Path)
		{
			if (string.IsNullOrEmpty(Path)) return false;
			// 检查形如 "C:\" 或 "D:\" 等（长度=3 且格式为 盘符:反斜杠）
			return Path.Length == 3 && Path[1] == ':' && Path[2] == '\\';
		}

		private async Task<ImageSource?> LoadIconCoreAsync(string Path, bool isFolder, bool useLargeIcon, DispatcherQueue dispatcherQueue)
		{
			var shfi = new SHFILEINFO();
			uint flags = SHGFI_ICON | (useLargeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON);
			uint dwAttributes = 0;

			// 对于文件夹或无效路径，使用 USEFILEATTRIBUTES 避免实际访问
			if (isFolder || Directory.Exists(Path) || !File.Exists(Path))
			{
				flags |= SHGFI_USEFILEATTRIBUTES;
				if (isFolder)
					dwAttributes = 0x10; // FILE_ATTRIBUTE_DIRECTORY
			}

			IntPtr hIcon = SHGetFileInfo(Path, dwAttributes, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
			if (shfi.hIcon == IntPtr.Zero)
				return null;

			try
			{
				// 在后台线程提取像素数据（避免阻塞 UI）
				var pixelData = await Task.Run(() =>
				{
					using (var icon = Icon.FromHandle(shfi.hIcon))
					{
						return ExtractIconPixelData(icon);
					}
				});

				if (pixelData == null)
					return null;

				// 在 UI 线程创建 WriteableBitmap 并填充数据
				var tcs = new TaskCompletionSource<ImageSource?>();
				dispatcherQueue.TryEnqueue(() =>
				{
					try
					{
						var bitmap = new WriteableBitmap(pixelData.Width, pixelData.Height);
						using (var stream = bitmap.PixelBuffer.AsStream())
						{
							stream.Write(pixelData.Pixels, 0, pixelData.Pixels.Length);
						}
						tcs.SetResult(bitmap);
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"[ShellIconHelper] UI thread error: {ex.Message}");
						tcs.SetResult(null);
					}
				});

				return await tcs.Task;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ShellIconHelper] LoadIconCoreAsync error: {ex.Message}");
				return null;
			}
			finally
			{
				DestroyIcon(shfi.hIcon);
			}
		}

		/// <summary>
		/// 从 Icon 提取 BGRA 像素数据（在后台线程执行）
		/// </summary>
		private IconPixelData? ExtractIconPixelData(Icon icon)
		{
			using (var bitmap = icon.ToBitmap())
			{
				int width = bitmap.Width;
				int height = bitmap.Height;
				var bmpData = bitmap.LockBits(
					new System.Drawing.Rectangle(0, 0, width, height),
					ImageLockMode.ReadOnly,
					System.Drawing.Imaging.PixelFormat.Format32bppArgb);

				try
				{
					int stride = bmpData.Stride;
					int bufferSize = stride * height;
					byte[] argbData = new byte[bufferSize];
					Marshal.Copy(bmpData.Scan0, argbData, 0, bufferSize);

					// 转换为 BGRA（WriteableBitmap 要求）
					byte[] bgraData = new byte[bufferSize];
					for (int i = 0; i < argbData.Length; i += 4)
					{
						bgraData[i] = argbData[i];     // B
						bgraData[i + 1] = argbData[i + 1]; // G
						bgraData[i + 2] = argbData[i + 2]; // R
						bgraData[i + 3] = argbData[i + 3]; // A
					}
					return new IconPixelData(width, height, bgraData);
				}
				finally
				{
					bitmap.UnlockBits(bmpData);
				}
			}
		}

		private class IconPixelData
		{
			public int Width { get; }
			public int Height { get; }
			public byte[] Pixels { get; } // BGRA 格式
			public IconPixelData(int width, int height, byte[] pixels)
			{
				Width = width;
				Height = height;
				Pixels = pixels;
			}
		}

		/// <summary>
		/// 清除图标缓存（例如在系统主题更改时调用）
		/// </summary>
		public static void ClearCache()
		{
			_iconCache.Clear();
		}
		public static bool IsSpecialFolder(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) return false;

			// 去除末尾的目录分隔符（Windows 和 Linux 都兼容）
			string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			// 取最后一级目录名
			string folderName = Path.GetFileName(trimmed);

			return string.Equals(folderName, "Documents", StringComparison.OrdinalIgnoreCase) ||
				   string.Equals(folderName, "Downloads", StringComparison.OrdinalIgnoreCase) ||
				   string.Equals(folderName, "Desktop", StringComparison.OrdinalIgnoreCase);
		}
	}
}