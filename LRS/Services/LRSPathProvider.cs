using BetterBreadcrumbBar.Control;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LRS.Services
{
	public class LRSPathProvider : IPathProvider
	{
		// 模拟的根目录
		private readonly Dictionary<string, List<PathNode>> _fileSystem = new()
		{
			["C:"] = new()
		{
			new PathNode { FullPath = "C:\\Windows", Name = "Windows" },
			new PathNode { FullPath = "C:\\Program Files", Name = "Program Files" },
			new PathNode { FullPath = "C:\\Users", Name = "Users" },
		},
			["C:\\Windows"] = new()
		{
			new PathNode { FullPath = "C:\\Windows\\System32", Name = "System32" },
			new PathNode { FullPath = "C:\\Windows\\Temp", Name = "Temp" },
		},
			["C:\\Windows\\System32"] = new()
		{
			new PathNode { FullPath = "C:\\Windows\\System32\\drivers", Name = "drivers" },
			new PathNode { FullPath = "C:\\Windows\\System32\\config", Name = "config" },
		}
		};

		/// <summary>
		/// 获取指定节点的子节点列表。
		/// </summary>
		/// <param name="node">当前节点，包含FullPath和Name等信息。</param>
		/// <param name="ct">取消令牌。</param>
		/// <returns>子节点列表。</returns>
		public Task<IEnumerable<PathNode>> GetChildrenAsync(PathNode node, CancellationToken ct)
		{
			// 如果节点为 null 或 FullPath 为空，表示根目录，返回驱动器列表
			if (node == null || string.IsNullOrEmpty(node.FullPath))
			{
				var rootNodes = new List<PathNode>
			{
				new PathNode { FullPath = "C:", Name = "本地磁盘 (C:)" }
			};
				return Task.FromResult(rootNodes.AsEnumerable());
			}

			// 从模拟的文件系统中查找子节点
			if (_fileSystem.TryGetValue(node.FullPath, out var children))
			{
				return Task.FromResult(children.AsEnumerable());
			}

			// 如果没有子节点，返回空列表
			return Task.FromResult(Enumerable.Empty<PathNode>());
		}

		/// <summary>
		/// 根据用户输入获取路径建议（用于地址栏自动补全）。
		/// </summary>
		/// <param name="text">用户当前输入的文本。</param>
		/// <returns>建议的路径列表。</returns>
		public Task<IEnumerable<string>> GetSuggestionsAsync(string text)
		{
			// 模拟根据输入文本过滤路径
			var suggestions = _fileSystem.Keys
				.Where(FullPath => FullPath.Contains(text, System.StringComparison.OrdinalIgnoreCase))
				.ToList();

			return Task.FromResult(suggestions.AsEnumerable());
		}
	}
}