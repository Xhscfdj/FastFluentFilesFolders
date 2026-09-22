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
//using Windows.Storage.Streams;

namespace FastFluentFilesFolders.Services
{
	public class ShellIconHelper : IIconProvider
	{
		private readonly IconCache _iconCache;
		private readonly ConcurrentDictionary<string, Task<ImageSource?>> _inflight = new();

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

		// —— Shell 命名空间 / Shell 项图标 ——
		// SHGetFileInfo 无法解析 “::{CLSID}” 虚拟路径（返回 0），因此此电脑/网络/回收站/WSL
		// 等特殊位置长期回退到 Segoe 字形。改用 IShellItemImageFactory.GetImage 取真实系统图标。
		// 另设 "shellitem::" 前缀，让普通目录（如 OneDrive 云盘目录）也能用该接口取到
		// 带云盘提供商徽标的真实图标，并与 “_folder_” 通用缓存区分开。
		public const string ShellItemPrefix = "shellitem::";

		private const int SIIGBF_BIGGERSIZEOK = 0x01;
		private const int SIIGBF_ICONONLY = 0x04;

		[StructLayout(LayoutKind.Sequential)]
		private struct SIZE { public int cx; public int cy; }

		[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IShellItemImageFactory
		{
			[PreserveSig]
			int GetImage(SIZE size, int flags, out IntPtr phbm);
		}

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
		private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
			[MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

		[StructLayout(LayoutKind.Sequential)]
		private struct BITMAP
		{
			public int bmType, bmWidth, bmHeight, bmWidthBytes;
			public ushort bmPlanes, bmBitsPixel;
			public IntPtr bmBits;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct BITMAPINFOHEADER
		{
			public uint biSize;
			public int biWidth, biHeight;
			public ushort biPlanes, biBitCount;
			public uint biCompression, biSizeImage;
			public int biXPelsPerMeter, biYPelsPerMeter;
			public uint biClrUsed, biClrImportant;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct BITMAPINFO
		{
			public BITMAPINFOHEADER bmiHeader;
			public uint bmiColors;
		}

		[DllImport("gdi32.dll")]
		private static extern int GetObject(IntPtr hObject, int cbBuffer, ref BITMAP lpvObject);

		[DllImport("gdi32.dll")]
		private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFO lpbmi, uint usage);

		[DllImport("gdi32.dll")]
		private static extern bool DeleteObject(IntPtr hObject);

		[DllImport("user32.dll")]
		private static extern IntPtr GetDC(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

		/// <summary>是否为需要走 Shell 命名空间/Shell 项接口解析的图标路径。</summary>
		public static bool IsShellItemIconPath(string path) =>
			!string.IsNullOrEmpty(path) &&
			(path.StartsWith("::{", StringComparison.OrdinalIgnoreCase) ||
			 path.StartsWith(ShellItemPrefix, StringComparison.OrdinalIgnoreCase));

		/// <summary>把真实目录包装成走 Shell 项接口解析的图标路径（取带提供商徽标的图标）。</summary>
		public static string BuildShellItemIconPath(string realPath) => ShellItemPrefix + realPath;

		private static string GetShellItemParsingName(string path) =>
			path.StartsWith(ShellItemPrefix, StringComparison.OrdinalIgnoreCase)
				? path.Substring(ShellItemPrefix.Length)
				: path;

		public ShellIconHelper(IconCache iconCache)
		{
			_iconCache = iconCache;
		}

		/// <summary>
		/// 获取文件/文件夹的系统图标（异步，支持缓存）
		/// </summary>
		public async Task<ImageSource?> GetIconAsync(string fullPath, bool isFolder, DispatcherQueue dispatcherQueue, uint size = 24)
		{
			if (string.IsNullOrEmpty(fullPath))
				return null;
			bool useLargeIcon = size >= 20;
			string cacheKey = BuildCacheKey(fullPath, isFolder, useLargeIcon);

			// 1. 尝试从缓存获取
			if (_iconCache.TryGet(cacheKey, out var cachedIcon))
				return cachedIcon;

			// 2. 同一 key 只发起一次实际加载，其余共享同一个 Task，
			//    避免同扩展名/同图标的大量文件并发时重复解码造成卡顿
			var loadTask = _inflight.GetOrAdd(cacheKey,
				key => LoadAndCacheAsync(key, fullPath, isFolder, useLargeIcon, dispatcherQueue));
			try
			{
				return await loadTask;
			}
			finally
			{
				_inflight.TryRemove(cacheKey, out _);
			}
		}

		private async Task<ImageSource?> LoadAndCacheAsync(string cacheKey, string fullPath, bool isFolder, bool useLargeIcon, DispatcherQueue dispatcherQueue)
		{
			var icon = await LoadIconCoreAsync(fullPath, isFolder, useLargeIcon, dispatcherQueue);
			if (icon != null)
				_iconCache.Set(cacheKey, icon);
			return icon;
		}

		/// <summary>
		/// 同步尝试从缓存获取图标（不触发任何文件系统访问），用于命中缓存时的快速路径
		/// </summary>
		public bool TryGetCached(string fullPath, bool isFolder, out ImageSource? icon)
		{
			icon = null;
			if (string.IsNullOrEmpty(fullPath))
				return false;
			string cacheKey = BuildCacheKey(fullPath, isFolder, true);
			return _iconCache.TryGet(cacheKey, out icon);
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
				else if (IsShellItemIconPath(fullPath))
					key = fullPath.ToLowerInvariant(); // 此电脑/回收站/WSL 等虚拟根及云盘目录各有专属图标
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
			// “::{CLSID}” 虚拟位置 / “shellitem::” 云盘目录：走 Shell 项接口取真实系统图标
			if (IsShellItemIconPath(Path))
				return await LoadShellItemIconAsync(GetShellItemParsingName(Path), useLargeIcon, dispatcherQueue);

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

				return await CreateImageSourceAsync(pixelData, dispatcherQueue);
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
		/// 通过 IShellItemImageFactory 解析 Shell 命名空间（“::{CLSID}”）或普通目录的
		/// 真实系统图标（此电脑/网络/回收站/WSL/云盘等）。
		/// </summary>
		private async Task<ImageSource?> LoadShellItemIconAsync(string parsingName, bool useLargeIcon, DispatcherQueue dispatcherQueue)
		{
			IntPtr hBitmap = IntPtr.Zero;
			IShellItemImageFactory? factory = null;
			try
			{
				var iid = typeof(IShellItemImageFactory).GUID;
				SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out factory);
				if (factory == null)
					return null;

				int size = useLargeIcon ? 32 : 16;
				int hr = factory.GetImage(new SIZE { cx = size, cy = size },
					SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hBitmap);
				if (hr != 0 || hBitmap == IntPtr.Zero)
					return null;

				var pixelData = await Task.Run(() => ExtractHBitmapPixelData(hBitmap));
				if (pixelData == null)
					return null;

				return await CreateImageSourceAsync(pixelData, dispatcherQueue);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ShellIconHelper] LoadShellItemIconAsync({parsingName}) error: {ex.Message}");
				return null;
			}
			finally
			{
				if (hBitmap != IntPtr.Zero)
					DeleteObject(hBitmap);
				if (factory != null)
				{
					try { Marshal.ReleaseComObject(factory); } catch { }
				}
			}
		}

		/// <summary>
		/// 从 HBITMAP（GetImage 返回 32bpp 预乘 alpha）提取直通 alpha 的 BGRA 像素数据。
		/// </summary>
		private IconPixelData? ExtractHBitmapPixelData(IntPtr hBitmap)
		{
			var bm = new BITMAP();
			if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), ref bm) == 0)
				return null;

			int width = bm.bmWidth;
			int height = bm.bmHeight;
			if (width <= 0 || height <= 0)
				return null;

			var bmi = new BITMAPINFO();
			bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
			bmi.bmiHeader.biWidth = width;
			bmi.bmiHeader.biHeight = -height; // 负值 = 自上而下
			bmi.bmiHeader.biPlanes = 1;
			bmi.bmiHeader.biBitCount = 32;
			bmi.bmiHeader.biCompression = 0; // BI_RGB

			var buffer = new byte[width * height * 4];
			IntPtr hdc = GetDC(IntPtr.Zero);
			try
			{
				if (GetDIBits(hdc, hBitmap, 0, (uint)height, buffer, ref bmi, 0) == 0)
					return null;
			}
			finally
			{
				ReleaseDC(IntPtr.Zero, hdc);
			}

			// HBITMAP 为预乘 alpha，WriteableBitmap 需要直通 alpha：反预乘
			for (int i = 0; i < buffer.Length; i += 4)
			{
				byte a = buffer[i + 3];
				if (a != 0 && a != 255)
				{
					buffer[i] = (byte)Math.Min(255, buffer[i] * 255 / a);
					buffer[i + 1] = (byte)Math.Min(255, buffer[i + 1] * 255 / a);
					buffer[i + 2] = (byte)Math.Min(255, buffer[i + 2] * 255 / a);
				}
			}

			return new IconPixelData(width, height, buffer);
		}

		/// <summary>在 UI 线程用像素数据创建 WriteableBitmap。</summary>
		private static async Task<ImageSource?> CreateImageSourceAsync(IconPixelData pixelData, DispatcherQueue dispatcherQueue)
		{
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

		/// <summary>
		/// 清除图标缓存（例如在系统主题更改时调用）
		/// </summary>
		public void ClearCache()
		{
			_iconCache.Clear();
		}
		public static ViewModels.Configs? Configs { get; set; }

		public static bool IsSpecialFolder(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) return false;

			string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string folderName = Path.GetFileName(trimmed);

			if (string.Equals(folderName, "Documents", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(folderName, "Downloads", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(folderName, "Desktop", StringComparison.OrdinalIgnoreCase)   ||
			    string.Equals(folderName, "Music", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(folderName, "Pictures", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(folderName, "Videos", StringComparison.OrdinalIgnoreCase))
				return true;

			if (Configs != null && Configs.IsTimeGroupedFolder(path))
				return true;

			return false;
		}
	}
}