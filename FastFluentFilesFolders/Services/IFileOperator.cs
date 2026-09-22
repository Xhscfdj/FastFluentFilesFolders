using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FastFluentFilesFolders.Models;

namespace FastFluentFilesFolders.Services
{
	public interface IFileOperator
	{
		Task<int> CopyToClipBoard(IEnumerable<string> fullPaths, bool cut = false);
		Task<(IEnumerable<string> FilePaths, bool IsCut)> PasteClipboardFiles();
		Task CopyToAsync(string from, string to, bool overwrite = false, Action<FileOperationProgress>? progress = null);
		Task<(int FileCount, long TotalBytes)> GetTransferStatsAsync(IEnumerable<string> paths);
		Task<IReadOnlyList<FileOperationResult>> DeleteManyAsync(IEnumerable<string> fullPaths, bool toRecycleBin);
		Task DeleteAsync(string fullPath);
		Task DeleteToRecycleBinAsync(string fullPath);
		Task RenameAsync(string fullPath, string newName);
		Task MoveAsync(string from, string to, bool overwrite = false, Action<FileOperationProgress>? progress = null);
	}
}
