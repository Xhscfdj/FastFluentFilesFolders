using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LRS.Services
{
	public interface IFileOperator
	{
		Task CopyToClipBoard(IEnumerable<string> fullPaths, bool cut = false);
		Task<(IEnumerable<string> FilePaths, bool IsCut)> PasteClipboardFiles();
		Task CopyToAsync(string from, string to, bool overwrite = false);
		Task DeleteAsync(string fullPath);
		Task RenameAsync(string fullPath, string newName);
		Task MoveAsync(string from, string to);
	}
}
