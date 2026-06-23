// Services/WindowsIconProvider.cs
using LRS.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace LRS.Services
{
    /// <summary>
    /// Windows 平台图标提供器，通过 Shell API 获取文件/文件夹的系统图标
    /// </summary>
    public class WindowsIconProvider : IIconProvider
    {
		/// <summary>
		/// 用于获取对应文件夹的图标的函数
		/// </summary>
		/// <param name="folderPath">文件夹路径</param>
		/// <param name="size">图标大小</param>
		/// <returns>返回ImageSource对象</returns>
		public static async Task<ImageSource>? GetFolderIconAsync(string folderPath, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, uint size = 32)
		{
			var tcs = new TaskCompletionSource<ImageSource?>();
			dispatcherQueue.TryEnqueue(async () =>
			{
				try
				{
					var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
					using (var thumbnail = await folder.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.UseCurrentScale))
					{
						if (thumbnail == null || thumbnail.Size == 0)
						{
							tcs.SetResult(null);
							return;
						}
						var bitmap = new BitmapImage();
						await bitmap.SetSourceAsync(thumbnail);
						tcs.SetResult(bitmap);
					}
				}
				catch (Exception ex)
				{
					tcs.SetResult(null); // 统一返回 null，不抛异常
				}
			});
			return await tcs.Task;
		}
		/// <summary>
		/// 根据文件路径获取对应的文件图标（ImageSource）
		/// </summary>
		/// <param name="filePath">文件完整路径</param>
		/// <param name="size">图标尺寸（默认 32px）</param>
		/// <returns>可用于 Image.Source 的 ImageSource 对象</returns>
		public static async Task<ImageSource>? GetFileIconAsync(string filePath, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, uint size = 32)
		{
			var tcs = new TaskCompletionSource<ImageSource>();
			dispatcherQueue.TryEnqueue(async () =>
			{
				try
				{
					var file = await StorageFile.GetFileFromPathAsync(filePath);
					using (var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.UseCurrentScale))
					{
						if (thumbnail == null || thumbnail.Size == 0)
						{
							tcs.SetResult(null);
							return;
						}

						var bitmap = new BitmapImage();
						await bitmap.SetSourceAsync(thumbnail);
						tcs.SetResult(bitmap);
					}
				}
				catch (Exception ex)
				{
					tcs.SetException(ex);
				}
			});
			return await tcs.Task;
		}
		/// <summary>
		/// 
		/// </summary>
		/// <param name="Path"></param>
		/// <param name="size"></param>
		/// <returns></returns>
		public async Task<ImageSource>? GetIconAsync(string Path, bool isFolder, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, uint size = 32)
		{
			if (isFolder && Directory.Exists(Path))
				return await GetFolderIconAsync(Path, dispatcherQueue, size);
			else
				return await GetFileIconAsync(Path, dispatcherQueue, size);
		}
		//    /// <summary>
		//    /// 获取文件对应的系统图标
		//    /// </summary>
		//    /// <param name="filePath">文件或文件夹的完整路径</param>
		//    /// <param name="isFolder">是否为文件夹（true 获取文件夹图标，false 获取文件图标）</param>
		//    /// <returns>WinUI ImageSource 格式的图标，获取失败返回 null</returns>
		//    public ImageSource? GetIcon(string filePath, bool isFolder = false)
		//    {
		//        if (string.IsNullOrWhiteSpace(filePath)) return null;

		//        SHFILEINFO shinfo = default;
		//        try
		//        {
		//            var fullPath = Path.GetFullPath(filePath);
		//            uint flags = SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES;
		//            uint dwAttributes = isFolder ? FILE_ATTRIBUTE_DIRECTORY : 0;

		//            var result = SHGetFileInfo(
		//                fullPath,
		//                dwAttributes,
		//                out shinfo,
		//                (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
		//                flags);

		//            if (shinfo.hIcon != IntPtr.Zero)
		//            {
		//                // Icon.FromHandle 创建的是浅拷贝，需 Clone 后再释放原生句柄
		//                using (var tempIcon = Icon.FromHandle(shinfo.hIcon))
		//                {
		//                    using (var clonedIcon = (Icon)tempIcon.Clone())
		//                    {
		//                        return ConvertIconToImageSource(clonedIcon);
		//                    }
		//                }
		//            }
		//            return null;
		//        }
		//        catch
		//        {
		//            return null;
		//        }
		//        finally
		//        {
		//            if (shinfo.hIcon != IntPtr.Zero)
		//                DestroyIcon(shinfo.hIcon);
		//        }
		//    }

		//    /// <summary>
		//    /// 将 System.Drawing.Icon 转换为 WinUI WriteableBitmap（同步）
		//    /// </summary>
		//    private ImageSource? ConvertIconToImageSource(Icon icon)
		//    {
		//        if (icon == null) return null;

		//        using (var bitmap = icon.ToBitmap())
		//        {
		//            int width = bitmap.Width;
		//            int height = bitmap.Height;

		//            var writeableBitmap = new WriteableBitmap(width, height);
		//            var bmpData = bitmap.LockBits(
		//                new System.Drawing.Rectangle(0, 0, width, height),
		//                ImageLockMode.ReadOnly,
		//                PixelFormat.Format32bppArgb);

		//            try
		//            {
		//                using (var stream = writeableBitmap.PixelBuffer.AsStream())
		//                {
		//                    int stride = bmpData.Stride;          // 通常为 width * 4
		//                    byte[] row = new byte[stride];

		//                    for (int y = 0; y < height; y++)
		//                    {
		//                        Marshal.Copy(bmpData.Scan0 + y * stride, row, 0, stride);

		//                        // 将 ARGB 转换为 BGRA（WriteableBitmap 像素格式）
		//                        for (int x = 0; x < width; x++)
		//                        {
		//                            int idx = x * 4;
		//                            byte b = row[idx];     // B
		//                            byte g = row[idx + 1]; // G
		//                            byte r = row[idx + 2]; // R
		//                            byte a = row[idx + 3]; // A

		//                            stream.WriteByte(b);
		//                            stream.WriteByte(g);
		//                            stream.WriteByte(r);
		//                            stream.WriteByte(a);
		//                        }
		//                    }
		//                }
		//            }
		//            finally
		//            {
		//                bitmap.UnlockBits(bmpData);
		//            }

		//            return writeableBitmap;
		//        }
		//    }

		//    #region Win32 API 定义

		//    private const uint SHGFI_ICON = 0x000000100;
		//    private const uint SHGFI_LARGEICON = 0x000000000;
		//    private const uint SHGFI_SMALLICON = 0x000000001;
		//    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
		//    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

		//    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		//    private struct SHFILEINFO
		//    {
		//        public IntPtr hIcon;
		//        public int iIcon;
		//        public uint dwAttributes;
		//        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		//        public string szDisplayName;
		//        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
		//        public string szTypeName;
		//    }

		//    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
		//    private static extern IntPtr SHGetFileInfo(
		//        string pszPath,
		//        uint dwFileAttributes,
		//        out SHFILEINFO psfi,
		//        uint cbFileInfo,
		//        uint uFlags);

		//    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		//    private static extern bool DestroyIcon(IntPtr hIcon);

		//    // 接口显式要求的另一个重载（原代码中存在）
		//    public ImageSource? GetIcon(string filePath)
		//    {
		//        throw new NotImplementedException();
		//    }

		//    #endregion
	}
}