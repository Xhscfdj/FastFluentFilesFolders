using LRS.Services;
using LRS.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace LRS.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MiddleFilesView : Page
    {
        public MiddleFilesView()
        {
			Configs configs = App.Services.GetRequiredService<Configs>();
			InitializeComponent();
			var uiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
			IIconProvider iconProvider = new WindowsIconProvider(); // 实际可从服务容器获取
            this.DataContext = App.SharedViewModel;
        }
		private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
		{
			// sender 是触发事件的 Grid，它的 DataContext 就是被双击的数据对象
			if (sender is FrameworkElement element && element.DataContext is FileSystemNodeViewModel item)
			{
				var vm = this.DataContext as MainWindowViewModel;
				vm?.OpenItem(item);
			}
		}
    }
}
