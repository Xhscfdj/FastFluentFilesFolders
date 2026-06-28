using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using System.Collections.Specialized;

namespace LRS.Services
{
	public class FileOperator : IFileOperator
	{
		public async Task CopyToAsync(string sourcePath, string destinationPath, bool overwrite = false)
		{
			if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
				throw new ArgumentException("路径不能为空");

			await Task.Run(() =>
			{
				if (File.Exists(sourcePath))
				{
					// 文件复制
					File.Copy(sourcePath, destinationPath, overwrite);
				}
				else if (Directory.Exists(sourcePath))
				{
					// 文件夹复制（递归）
					CopyDirectoryRecursive(sourcePath, destinationPath, overwrite);
				}
				else
				{
					throw new FileNotFoundException($"源路径不存在: {sourcePath}");
				}
			});
		}

		private void CopyDirectoryRecursive(string sourceDir, string destDir, bool overwrite)
		{
			Directory.CreateDirectory(destDir);

			// 复制所有文件
			foreach (string file in Directory.GetFiles(sourceDir))
			{
				string fileName = Path.GetFileName(file);
				string destFile = Path.Combine(destDir, fileName);
				File.Copy(file, destFile, overwrite);
			}

			// 递归复制子目录
			foreach (string subDir in Directory.GetDirectories(sourceDir))
			{
				string dirName = Path.GetFileName(subDir);
				string destSubDir = Path.Combine(destDir, dirName);
				CopyDirectoryRecursive(subDir, destSubDir, overwrite);
			}
		}

		public async Task MoveAsync(string sourcePath, string destinationPath)
		{
			if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
				throw new ArgumentException("路径不能为空");

			await Task.Run(() =>
			{
				if (File.Exists(sourcePath))
				{
					File.Move(sourcePath, destinationPath);
				}
				else if (Directory.Exists(sourcePath))
				{
					Directory.Move(sourcePath, destinationPath);
				}
				else
				{
					throw new FileNotFoundException($"源路径不存在: {sourcePath}");
				}
			});
		}

		public async Task DeleteAsync(string path)
		{
			if (string.IsNullOrEmpty(path))
				throw new ArgumentException("路径不能为空");

			await Task.Run(() =>
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				else if (Directory.Exists(path))
				{
					Directory.Delete(path, true); // 递归删除
				}
				else
				{
					// 如果路径不存在，静默返回（或可选择抛出异常）
				}
			});
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

		public async Task CopyToClipBoard(IEnumerable<string> filePaths, bool cut = false)
		{
			if (filePaths == null || !filePaths.Any())
				return;

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

			if (!storageItems.Any())
				return;

			var dataPackage = new DataPackage();
			dataPackage.SetStorageItems(storageItems);
			// 关键：设置操作类型，支持剪切标记
			dataPackage.RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy;

			Clipboard.SetContent(dataPackage);
		}

		public async Task<(IEnumerable<string> FilePaths, bool IsCut)> PasteClipboardFiles()
		{
			var dataPackageView = Clipboard.GetContent();
			if (dataPackageView.Contains(StandardDataFormats.StorageItems))
			{
				try
				{
					var storageItems = await dataPackageView.GetStorageItemsAsync();
					var paths = storageItems.Select(item => item.Path).ToList();
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
