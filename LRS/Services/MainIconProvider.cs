using LRS.ViewModels;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LRS.Services
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
	}
}
