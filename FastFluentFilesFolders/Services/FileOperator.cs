using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using System.Collections.Specialized;
using FastFluentFilesFolders.Models;

namespace FastFluentFilesFolders.Services
{
	public class FileOperator : IFileOperator
	{
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct SHFILEOPSTRUCT
		{
			public IntPtr hwnd;
			public uint wFunc;
			[MarshalAs(UnmanagedType.LPWStr)]
			public string pFrom;
			[MarshalAs(UnmanagedType.LPWStr)]
			public string pTo;
			public ushort fFlags;
			public bool fAnyOperationsAborted;
			public IntPtr hNameMappings;
			[MarshalAs(UnmanagedType.LPWStr)]
			public string lpszProgressTitle;
		}

		[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
		private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

		private const uint FO_DELETE = 0x0003;
		private const ushort FOF_ALLOWUNDO = 0x0040;
		private const ushort FOF_NOCONFIRMATION = 0x0010;
		private const ushort FOF_SILENT = 0x0004;
		public async Task CopyToAsync(string sourcePath, string destinationPath, bool overwrite = false, Action<FileOperationProgress>? progress = null)
		{
			if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
				throw new ArgumentException("路径不能为空");

			if (Directory.Exists(sourcePath) && IsSameOrSubPath(destinationPath, sourcePath))
				throw new InvalidOperationException($"不能将文件夹复制到自身或其子文件夹中: {sourcePath}");

			await Task.Run(() =>
			{
				var state = new FileOperationProgress();
				if (File.Exists(sourcePath))
				{
					// 文件复制
					File.Copy(sourcePath, destinationPath, overwrite);
					state.CompletedFiles = 1;
					state.CompletedBytes = SafeGetFileLength(sourcePath);
					progress?.Invoke(state);
				}
				else if (Directory.Exists(sourcePath))
				{
					// 文件夹复制（递归）
					CopyDirectoryRecursive(sourcePath, destinationPath, overwrite, state, progress);
				}
				else
				{
					throw new FileNotFoundException($"源路径不存在: {sourcePath}");
				}
			});
		}

		/// <summary>
		/// 统计一组路径（文件或文件夹，递归）包含的文件总数与总字节数。
		/// </summary>
		public async Task<(int FileCount, long TotalBytes)> GetTransferStatsAsync(IEnumerable<string> paths)
		{
			if (paths == null) return (0, 0);
			return await Task.Run(() =>
			{
				int count = 0;
				long bytes = 0;
				foreach (var path in paths)
				{
					if (File.Exists(path))
					{
						count++;
						bytes += SafeGetFileLength(path);
					}
					else if (Directory.Exists(path))
					{
						EnumerateStats(path, ref count, ref bytes);
					}
				}
				return (count, bytes);
			});
		}

		private static void EnumerateStats(string dir, ref int count, ref long bytes)
		{
			try
			{
				foreach (string file in Directory.EnumerateFiles(dir))
				{
					count++;
					bytes += SafeGetFileLength(file);
				}
				foreach (string subDir in Directory.EnumerateDirectories(dir))
				{
					EnumerateStats(subDir, ref count, ref bytes);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[GetTransferStats] {dir}: {ex.Message}");
			}
		}

		private static long SafeGetFileLength(string path)
		{
			try { return new FileInfo(path).Length; }
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[FileOperator] 获取文件大小失败 {path}: {ex.Message}");
				return 0;
			}
		}

		private void CopyDirectoryRecursive(string sourceDir, string destDir, bool overwrite, FileOperationProgress state, Action<FileOperationProgress>? progress)
		{
			Directory.CreateDirectory(destDir);

			// 复制所有文件
			foreach (string file in Directory.GetFiles(sourceDir))
			{
				string fileName = Path.GetFileName(file);
				string destFile = Path.Combine(destDir, fileName);
				File.Copy(file, destFile, overwrite);
				state.CompletedFiles++;
				state.CompletedBytes += SafeGetFileLength(file);
				progress?.Invoke(state);
			}

			// 递归复制子目录
			foreach (string subDir in Directory.GetDirectories(sourceDir))
			{
				string dirName = Path.GetFileName(subDir);
				string destSubDir = Path.Combine(destDir, dirName);
				CopyDirectoryRecursive(subDir, destSubDir, overwrite, state, progress);
			}
		}

		public async Task MoveAsync(string sourcePath, string destinationPath, bool overwrite = false, Action<FileOperationProgress>? progress = null)
		{
			if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
				throw new ArgumentException("路径不能为空");

			if (string.Equals(sourcePath.TrimEnd('\\', '/'), destinationPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
				return;

			if (Directory.Exists(sourcePath) && IsSameOrSubPath(destinationPath, sourcePath))
				throw new InvalidOperationException($"不能将文件夹移动到自己或子文件夹中: {sourcePath}");

			await Task.Run(() =>
			{
				var state = new FileOperationProgress();
				if (File.Exists(sourcePath))
					MoveFileCore(sourcePath, destinationPath, overwrite, state, progress);
				else if (Directory.Exists(sourcePath))
					MoveDirectoryCore(sourcePath, destinationPath, overwrite, state, progress);
				else
					throw new FileNotFoundException($"源路径不存在: {sourcePath}");
			});
		}

		private static bool SameVolume(string a, string b)
			=> string.Equals(
				Path.GetPathRoot(Path.GetFullPath(a)),
				Path.GetPathRoot(Path.GetFullPath(b)),
				StringComparison.OrdinalIgnoreCase);

		/// <summary>candidate 是否等于 root 或位于 root 之下（用于阻止“移动/复制到自身子目录”）。</summary>
		private static bool IsSameOrSubPath(string candidate, string root)
		{
			try
			{
				var c = Path.GetFullPath(candidate).TrimEnd('\\', '/');
				var r = Path.GetFullPath(root).TrimEnd('\\', '/');
				if (string.Equals(c, r, StringComparison.OrdinalIgnoreCase)) return true;
				return c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return false;
			}
		}

		private static (int Count, long Bytes) CountTree(string dir)
		{
			int count = 0;
			long bytes = 0;
			EnumerateStats(dir, ref count, ref bytes);
			return (count, bytes);
		}

		private void MoveFileCore(string sourcePath, string destPath, bool overwrite, FileOperationProgress state, Action<FileOperationProgress>? progress)
		{
			long size = SafeGetFileLength(sourcePath);
			if (SameVolume(sourcePath, destPath))
			{
				// 同盘：直接移动（overwrite=true 时覆盖同名文件）
				File.Move(sourcePath, destPath, overwrite);
			}
			else
			{
				// 跨盘：先复制成功、再删除源，避免中途失败导致数据丢失
				File.Copy(sourcePath, destPath, overwrite);
				File.Delete(sourcePath);
			}

			state.CompletedFiles++;
			state.CompletedBytes += size;
			progress?.Invoke(state);
		}

		private void MoveDirectoryCore(string sourceDir, string destDir, bool overwrite, FileOperationProgress state, Action<FileOperationProgress>? progress)
		{
			bool sameVolume = SameVolume(sourceDir, destDir);

			if (!Directory.Exists(destDir))
			{
				if (sameVolume)
				{
					Directory.Move(sourceDir, destDir);
					var (count, bytes) = CountTree(destDir);
					state.CompletedFiles += count;
					state.CompletedBytes += bytes;
					progress?.Invoke(state);
				}
				else
				{
					CopyDirectoryRecursive(sourceDir, destDir, overwrite: false, state, progress);
					Directory.Delete(sourceDir, true);
				}
				return;
			}

			// 目标已存在：overwrite=true 表示“替换/合并”（文件夹合并，同名文件覆盖）
			if (!overwrite)
				throw new IOException($"目标文件夹已存在: {destDir}");

			if (sameVolume)
			{
				MergeDirectorySameVolume(sourceDir, destDir, state, progress);
				Directory.Delete(sourceDir, false); // 合并完成后源目录应为空
			}
			else
			{
				CopyDirectoryRecursive(sourceDir, destDir, overwrite: true, state, progress);
				Directory.Delete(sourceDir, true);
			}
		}

		/// <summary>同盘目录合并：逐项移动，同名文件直接覆盖（调用方已获得用户“替换”确认）。</summary>
		private void MergeDirectorySameVolume(string sourceDir, string destDir, FileOperationProgress state, Action<FileOperationProgress>? progress)
		{
			Directory.CreateDirectory(destDir);

			foreach (string file in Directory.GetFiles(sourceDir))
			{
				string destFile = Path.Combine(destDir, Path.GetFileName(file));
				long size = SafeGetFileLength(file);
				File.Move(file, destFile, overwrite: true);
				state.CompletedFiles++;
				state.CompletedBytes += size;
				progress?.Invoke(state);
			}

			foreach (string subDir in Directory.GetDirectories(sourceDir))
			{
				string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
				if (Directory.Exists(destSubDir))
				{
					MergeDirectorySameVolume(subDir, destSubDir, state, progress);
					Directory.Delete(subDir, false);
				}
				else
				{
					Directory.Move(subDir, destSubDir);
					var (count, bytes) = CountTree(destSubDir);
					state.CompletedFiles += count;
					state.CompletedBytes += bytes;
					progress?.Invoke(state);
				}
			}
		}

		public async Task DeleteAsync(string path)
		{
			var results = await DeleteManyAsync(new[] { path }, toRecycleBin: false);
			if (results.Count > 0 && !results[0].Success)
				throw new IOException(results[0].ErrorMessage);
		}

		public async Task DeleteToRecycleBinAsync(string path)
		{
			var results = await DeleteManyAsync(new[] { path }, toRecycleBin: true);
			if (results.Count > 0 && !results[0].Success)
				throw new IOException(results[0].ErrorMessage);
		}

		/// <summary>
		/// 批量删除（回收站或彻底删除）：逐项执行、单项失败不中断其余项，
		/// 返回每一项的真实结果，供 VM 决定操作岛状态与是否移除 UI 行。
		/// </summary>
		public async Task<IReadOnlyList<FileOperationResult>> DeleteManyAsync(IEnumerable<string> fullPaths, bool toRecycleBin)
		{
			var paths = fullPaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>();
			if (paths.Count == 0)
				return Array.Empty<FileOperationResult>();

			return await Task.Run(() =>
			{
				var results = new List<FileOperationResult>(paths.Count);
				foreach (var path in paths)
				{
					try
					{
						if (toRecycleBin)
							DeleteToRecycleBinCore(path);
						else
							DeletePermanentlyCore(path);
						results.Add(new FileOperationResult(path, true, null));
					}
					catch (Exception ex)
					{
						System.Diagnostics.Debug.WriteLine($"[FileOperator] 删除失败 {path}: {ex.Message}");
						results.Add(new FileOperationResult(path, false, ex.Message));
					}
				}
				return (IReadOnlyList<FileOperationResult>)results;
			});
		}

		private static void DeletePermanentlyCore(string path)
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			else if (Directory.Exists(path))
			{
				Directory.Delete(path, true); // 递归删除
			}
			// 路径已不存在时视为“已删除成功”（幂等），避免残留 UI 行
		}

		private void DeleteToRecycleBinCore(string path)
		{
			// 幂等：路径已不存在时无需再调用 Shell 删除（避免“源不存在”误报）。
			if (!File.Exists(path) && !Directory.Exists(path))
				return;

			var fileOp = new SHFILEOPSTRUCT
			{
				wFunc = FO_DELETE,
				pFrom = path + "\0\0",
				fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION
			};
			int hr = SHFileOperation(ref fileOp);
			if (hr != 0 || fileOp.fAnyOperationsAborted)
				throw new IOException($"删除到回收站失败（SHFileOperation 返回 {hr}）: {path}");
		}

		public async Task RenameAsync(string oldPath, string newName)
		{
			if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newName))
				throw new ArgumentException("路径或新名称不能为空");

			string directory = Path.GetDirectoryName(oldPath);
			string newPath = Path.Combine(directory, newName);

			// 重命名其实就是 Move 到同一目录下的新名称
			await MoveAsync(oldPath, newPath);
		}

		// ========== 剪贴板操作 ==========

		public async Task<int> CopyToClipBoard(IEnumerable<string> filePaths, bool cut = false)
		{
			if (filePaths == null || !filePaths.Any())
				return 0;

			// 注意：由于需要将路径转换为 StorageItem，这里使用异步转同步（仅用于UI线程安全）
			// 建议在实际调用时确保此方法在 UI 线程执行，或者改为异步方法。
			var storageItems = new List<IStorageItem>();
			foreach (var path in filePaths)
			{
				try
				{
					if (File.Exists(path))
					{
						var file = StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
						storageItems.Add(file);
					}
					else if (Directory.Exists(path))
					{
						var folder = StorageFolder.GetFolderFromPathAsync(path).AsTask().GetAwaiter().GetResult();
						storageItems.Add(folder);
					}
				}
				catch (Exception ex)
				{
					// 处理无法访问的路径（记录日志或忽略）
					System.Diagnostics.Debug.WriteLine($"无法添加 {path} 到剪贴板: {ex.Message}");
				}
			}

			if (storageItems.Count == 0)
				return 0;

			var dataPackage = new DataPackage();
			dataPackage.SetStorageItems(storageItems);
			// 关键：设置操作类型，支持剪切标记
			dataPackage.RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy;

			Clipboard.SetContent(dataPackage);
			return storageItems.Count;
		}

		public async Task<(IEnumerable<string> FilePaths, bool IsCut)> PasteClipboardFiles()
		{
			var dataPackageView = Clipboard.GetContent();
			if (dataPackageView.Contains(StandardDataFormats.StorageItems))
			{
				try
				{
					var storageItems = await dataPackageView.GetStorageItemsAsync();
					var paths = storageItems
						.Select(item => item?.Path)
						.Where(p => !string.IsNullOrWhiteSpace(p))
						.ToList();
					var operation = dataPackageView.RequestedOperation;
					bool isCut = (operation == DataPackageOperation.Move);
					return (paths, isCut);
				}
				catch (Exception ex)
				{
					// 剪贴板内容无法读取
					System.Diagnostics.Debug.WriteLine($"读取剪贴板失败: {ex.Message}");
					return (null, false);
				}
			}

			return (null, false);
		}
	}
}
