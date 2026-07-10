using FastFluentFilesFolders.Extensions;
using FastFluentFilesFolders.Extensions.Interfaces;
using FastFluentFilesFolders.Extensions.Extensions;
using FastFluentFilesFolders.Services;
using FastFluentFilesFolders.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
//using Microsoft.UI.Xaml.Controls;
//using Microsoft.UI.Xaml.Controls.Primitives;
//using Microsoft.UI.Xaml.Data;
//using Microsoft.UI.Xaml.Input;
//using Microsoft.UI.Xaml.Media;
//using Microsoft.UI.Xaml.Navigation;
//using Microsoft.UI.Xaml.Shapes;
using System;
//using System.Collections.Generic;
using System.Diagnostics;
//using System.IO;
//using System.Linq;
//using System.Runtime.InteropServices.WindowsRuntime;
//using System.Threading.Tasks;
//using Windows.ApplicationModel;
//using Windows.ApplicationModel.Activation;
//using Windows.Foundation;
//using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FastFluentFilesFolders
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
		private Window? _window;
        public static Window? MainWindow { get; private set; }
        private IHost _host;
		public static MainWindowViewModel SharedViewModel { get; private set; }
		public static IServiceProvider Services { get; private set; }
        public static LocalizationService LocalizationService { get; private set; }
        public static MultiLanguageStringsViewModel ML { get; private set; }
        public static PluginManager PluginManager { get; private set; }
        private static IServiceProvider ConfigureServices()
        {
			var services = new ServiceCollection();

			services.AddSingleton<Configs>();
			services.AddSingleton<IIconProvider, WindowsIconProvider>();

			return services.BuildServiceProvider();
		}
		/// <summary>
		/// Initializes the singleton application object.  This is the first line of authored code
		/// executed, and as such is the logical equivalent of main() or WinMain().
		/// </summary>
		public App()
        {
            InitializeComponent();
            _host = Host.CreateDefaultBuilder().ConfigureServices((context, services) =>
            {
                services.AddSingleton(new Configs());
				services.AddSingleton<IIconProvider, WindowsIconProvider>();
				services.AddSingleton<IFileOperator, FileOperator>();
				services.AddSingleton<LocalizationService>();
				services.AddSingleton<MultiLanguageStringsViewModel>();
				services.AddSingleton<PluginManager>();
				services.AddSingleton<IExtension, SamplePlugin>();
				services.AddSingleton<IExtension, ArchivePlugin>();
			}).Build();
            Services = _host.Services;

            var configs = Services.GetRequiredService<Configs>();
            var locService = Services.GetRequiredService<LocalizationService>();
            locService.SetLanguage(configs.Language);
            LocalizationService = locService;
            ML = Services.GetRequiredService<MultiLanguageStringsViewModel>();
            ShellIconHelper.Configs = configs;
			this.UnhandledException += (s, e) =>
			{
				Debug.WriteLine($"未处理异常: {e.Exception}");
				e.Handled = true; // 防止立即崩溃，方便调试
			};
		}

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
		protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
			await _host.StartAsync();

			// 在 UI 线程上创建共享 ViewModel
			var dispatcher = DispatcherQueue.GetForCurrentThread();
			var configs = Services.GetRequiredService<Configs>();
			var fileOperator = Services.GetRequiredService<IFileOperator>();
			var iconProvider = Services.GetRequiredService<IIconProvider>();
			SharedViewModel = new MainWindowViewModel(iconProvider, dispatcher, configs, fileOperator, ML);

			PluginManager = Services.GetRequiredService<PluginManager>();
			PluginManager.SetDispatcherQueue(dispatcher);
			await PluginManager.LoadAllAsync();

			_window = new Views.MainWindowView();
			MainWindow = _window;
			_window.Activate();
		}
    }
}
