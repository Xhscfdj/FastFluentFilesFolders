using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml.Media;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.Services
{
    public class MainIconProvider : IIconProvider
    {
        private readonly Configs _configs;
        private readonly ShellIconHelper _win32Provider;
        private readonly WindowsIconProvider _winrtProvider;

        public MainIconProvider(Configs configs, IconCache iconCache)
        {
            _configs = configs;
            _win32Provider = new ShellIconHelper(iconCache);
            _winrtProvider = new WindowsIconProvider(iconCache);
        }

        public async Task<ImageSource?> GetIconAsync(string fullPath, bool isFolder, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, uint size = 24)
        {
            var useWin32 = _configs.IfUsesWin32APIToGetIcon;

            // Shell 命名空间/Shell 项（“::{CLSID}”、“shellitem::”）必须走 Win32 接口，
            // 否则 StorageFolder 无法解析虚拟路径，特殊位置会一直回退到 Segoe 字形
            if (ShellIconHelper.IsShellItemIconPath(fullPath))
                return await _win32Provider.GetIconAsync(fullPath, isFolder, dispatcherQueue, size);

            if (ShellIconHelper.IsSpecialFolder(fullPath) || !useWin32)
            {
                return await _winrtProvider.GetIconAsync(fullPath, isFolder, dispatcherQueue, size);
            }
            else
            {
                return await _win32Provider.GetIconAsync(fullPath, isFolder, dispatcherQueue, size);
            }
        }

        public bool TryGetCachedIcon(string fullPath, bool isFolder, out ImageSource? icon)
        {
            icon = null;
            if (ShellIconHelper.IsShellItemIconPath(fullPath))
                return _win32Provider.TryGetCached(fullPath, isFolder, out icon);

            var useWin32 = _configs.IfUsesWin32APIToGetIcon;
            if (ShellIconHelper.IsSpecialFolder(fullPath) || !useWin32)
                return false;
            return _win32Provider.TryGetCached(fullPath, isFolder, out icon);
        }
    }
}
