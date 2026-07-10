using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.Services
{
	public class MainIconProvider
	{
		private readonly Configs _configs;
		private IIconProvider _iconProvider;

		public MainIconProvider(Configs configs)
		{
			_configs = configs;
		}

		public Task<ImageSource>? GetIconAsync(string fullPath, bool isFolder, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, uint size = 32)
		{
			var useWin32 = _configs.IfUsesWin32APIToGetIcon;

			if (ShellIconHelper.IsSpecialFolder(fullPath) || !useWin32)
			{
				_iconProvider = new WindowsIconProvider();
			}
			else
			{
				_iconProvider = new ShellIconHelper();
			}
			return _iconProvider.GetIconAsync(fullPath, isFolder, dispatcherQueue, size);
		}

		/// <summary>
		/// 同步尝试命中图标缓存（仅 Win32/ShellIconHelper 路径有缓存），命中则可直接赋值，避免线程切换
		/// </summary>
		public bool TryGetCachedIcon(string fullPath, bool isFolder, out ImageSource? icon)
		{
			icon = null;
			var useWin32 = _configs.IfUsesWin32APIToGetIcon;
			// WindowsIconProvider（特殊文件夹或未启用 Win32）不做缓存
			if (ShellIconHelper.IsSpecialFolder(fullPath) || !useWin32)
				return false;
			return ShellIconHelper.TryGetCached(fullPath, isFolder, out icon);
		}
	}
}
