// Services/IIconProvider.cs
using Microsoft.UI.Xaml.Media;
using System.Threading.Tasks;

namespace LRS.Services
{
    public interface IIconProvider
    {
        /// <summary>
        /// 获取文件和文件夹的图标
        /// </summary>
        /// <param name="path">文件或文件夹路径</param>
        /// <param name="isFolder">是否为文件夹</param>
        /// <param name="dispatcherQueue">ui队列</param>
        /// <param name="size">图片大小</param>
        /// <returns></returns>
        Task<ImageSource>? GetIconAsync(string path, bool isFolder, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, uint size = 32);
    }
}