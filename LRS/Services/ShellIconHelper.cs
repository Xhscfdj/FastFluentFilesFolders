using LRS.Services;
using Microsoft.Extensions.FileProviders;
// Services/ShellIIconHelper.cs
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
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
		/// 获取文件/文件夹的系统图标（始终返回固定类型图标，不是内容缩略图）
		/// </summary>
		/// <param name="path">文件或文件夹路径</param>
		/// <param name="isFolder">是否是文件夹（帮助API选择正确标志）</param>
		/// <param name="dispatcherQueue">UI线程调度器</param>
		/// <param name="useLargeIcon">是否使用大图标（默认true，32x32；false为16x16）</param>
		/// <returns>ImageSource 或 null</returns>
		public async Task<ImageSource?> GetIconAsync(string path, bool isFolder, DispatcherQueue dispatcherQueue, uint size = 32)
		{
			bool useLargeIcon = false;
			if (size >= 32)
			{
				useLargeIcon = true;
			}
			return await GetSystemIconAsync(path, isFolder, useLargeIcon, dispatcherQueue);
		}

		private async Task<ImageSource?> GetSystemIconAsync(string path, bool isFolder, bool useLargeIcon, DispatcherQueue dispatcherQueue)
		{
			Debug.WriteLine($"[ShellIconHelper] GetSystemIconAsync called with path='{path}', isFolder={isFolder}, useLargeIcon={useLargeIcon}");

			if (string.IsNullOrEmpty(path))
			{
				Debug.WriteLine("[ShellIconHelper] Path is null or empty, returning null.");
				return null;
			}

			var shfi = new SHFILEINFO();
			uint flags = SHGFI_ICON | (useLargeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON);
			uint dwAttributes = 0;

			// 对于文件夹或无效路径，使用 USEFILEATTRIBUTES 避免实际访问文件
			if (isFolder || Directory.Exists(path) || !File.Exists(path))
			{
				flags |= SHGFI_USEFILEATTRIBUTES;
				if (isFolder)
				{
					dwAttributes = 0x10; // FILE_ATTRIBUTE_DIRECTORY
					Debug.WriteLine("[ShellIconHelper] isFolder = true, set dwAttributes = 0x10");
				}
				Debug.WriteLine($"[ShellIconHelper] Using SHGFI_USEFILEATTRIBUTES (flags = 0x{flags:X})");
			}
			else
			{
				Debug.WriteLine("[ShellIconHelper] Not using USEFILEATTRIBUTES, will attempt to access actual file.");
			}

			Debug.WriteLine($"[ShellIconHelper] Calling SHGetFileInfo with path='{path}', dwAttributes=0x{dwAttributes:X}, flags=0x{flags:X}");

			// 获取图标句柄
			IntPtr hIcon = SHGetFileInfo(path, dwAttributes, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
			int lastError = Marshal.GetLastWin32Error();

			Debug.WriteLine($"[ShellIconHelper] SHGetFileInfo returned hIcon = 0x{hIcon.ToInt64():X}, lastError = {lastError} (0x{lastError:X})");
			Debug.WriteLine($"[ShellIconHelper] SHFILEINFO.hIcon = 0x{shfi.hIcon.ToInt64():X}, iIcon = {shfi.iIcon}, dwAttributes = 0x{shfi.dwAttributes:X}");
				
			if (shfi.hIcon == IntPtr.Zero)
			{
				Debug.WriteLine($"[ShellIconHelper] shfi.hIcon is zero, returning null. (Possible causes: missing System.Drawing.Common, or SHGetFileInfo failed)");
				return null;
			}

			try
			{
				Debug.WriteLine("[ShellIconHelper] Converting HICON to Icon and Bitmap...");
				// 将 HICON 转为 Bitmap 并保存到内存流
				using (var icon = Icon.FromHandle(shfi.hIcon))
				{
					Debug.WriteLine($"[ShellIconHelper] Icon created, Size = {icon.Width}x{icon.Height}");
					using (var bitmap = icon.ToBitmap())
					{
						Debug.WriteLine($"[ShellIconHelper] Bitmap created, Size = {bitmap.Width}x{bitmap.Height}, PixelFormat = {bitmap.PixelFormat}");
						var stream = new InMemoryRandomAccessStream();
						bitmap.Save(stream.AsStream(), ImageFormat.Png);
						Debug.WriteLine($"[ShellIconHelper] Bitmap saved as PNG, stream length = {stream.Size}");
						stream.Seek(0);

						var tcs = new TaskCompletionSource<ImageSource?>();
						dispatcherQueue.TryEnqueue(async () =>
						{
							try
							{
								//Debug.WriteLine("[ShellIconHelper] UI thread: Creating BitmapImage...");
								//var bitmapImage = new BitmapImage();
								//await bitmapImage.SetSourceAsync(stream);
								//Debug.WriteLine("[ShellIconHelper] BitmapImage.SetSourceAsync completed successfully.");
								//tcs.SetResult(bitmapImage);
								var imageSource = ConvertIconToWriteableBitmap(icon);
								tcs.SetResult(imageSource);
							}
							catch (Exception ex)
							{
								Debug.WriteLine($"[ShellIconHelper] Exception in UI thread: {ex.Message}\n{ex.StackTrace}");
								tcs.SetResult(null);
							}
							finally
							{
								stream.Dispose();
							}
						});

						var result = await tcs.Task;
						Debug.WriteLine($"[ShellIconHelper] Returning ImageSource: {(result == null ? "null" : "non-null")}");
						return result;
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ShellIconHelper] Exception during bitmap conversion: {ex.Message}\n{ex.StackTrace}");
				return null;
			}
			finally
			{
				if (shfi.hIcon != IntPtr.Zero)
				{
					bool destroyed = DestroyIcon(shfi.hIcon);
					Debug.WriteLine($"[ShellIconHelper] DestroyIcon called, result = {destroyed}");
				}
			}
		}
		private ImageSource? ConvertIconToWriteableBitmap(Icon icon)
		{
			using(var bitmap = icon.ToBitmap())
			{
				int width = bitmap.Width;
				int height = bitmap.Height;
				var writeableBitmap = new WriteableBitmap(width, height);

				var bmpData = bitmap.LockBits(
					new System.Drawing.Rectangle(0, 0, width, height),
					ImageLockMode.ReadOnly,
					System.Drawing.Imaging.PixelFormat.Format32bppArgb);

				try
				{
					// 计算缓冲区大小
					int stride = bmpData.Stride;
					int bufferSize = stride * height;
					byte[] pixels = new byte[bufferSize];
					Marshal.Copy(bmpData.Scan0, pixels, 0, bufferSize);

					// WriteableBitmap 的像素格式是 BGRA，而 bmpData 是 ARGB，需要转换
					using (var stream = writeableBitmap.PixelBuffer.AsStream())
					{
						for (int y = 0; y < height; y++)
						{
							int rowStart = y * stride;
							for (int x = 0; x < width; x++)
							{
								int pixelStart = rowStart + x * 4;
								byte b = pixels[pixelStart + 0]; // B
								byte g = pixels[pixelStart + 1]; // G
								byte r = pixels[pixelStart + 2]; // R
								byte a = pixels[pixelStart + 3]; // A

								stream.WriteByte(b);
								stream.WriteByte(g);
								stream.WriteByte(r);
								stream.WriteByte(a);
							}
						}
					}
				}
				finally
				{
					bitmap.UnlockBits(bmpData);
				}

				return writeableBitmap;
			}
		}
	}
}
