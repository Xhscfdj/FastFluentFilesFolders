using FastFluentFilesFolders.Extensions;
using FastFluentFilesFolders.Extensions.Interfaces;
using FastFluentFilesFolders.Extensions.Extensions;
using FastFluentFilesFolders.Services;
using FastFluentFilesFolders.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;

namespace FastFluentFilesFolders
{
    public partial class App : Application
    {
        private Window? _window;
        public static Window? MainWindow { get; private set; }
        public static MainWindowViewModel SharedViewModel { get; private set; }
        public static IServiceProvider Services { get; private set; }
        public static LocalizationService LocalizationService { get; private set; }
        public static MultiLanguageStringsViewModel ML { get; private set; }
        public static PluginManager PluginManager { get; private set; }
        public static MainIconProvider SharedIconProvider { get; private set; }

        private static void CrashLog(string msg)
        {
            try
            {
                var path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FastFluentFilesFolders", "crash.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
            }
            catch { }
        }

        public App()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                CrashLog("InitializeComponent failed: " + ex);
                throw;
            }

            var services = new ServiceCollection();
            services.AddSingleton(new Configs());
            var iconCache = new IconCache(maxCapacity: 1500);
            services.AddSingleton(iconCache);
            services.AddSingleton<MainIconProvider>();
            services.AddSingleton<IIconProvider>(sp => sp.GetRequiredService<MainIconProvider>());
            services.AddSingleton<IFileOperator, FileOperator>();
            services.AddSingleton<LocalizationService>();
            services.AddSingleton<MultiLanguageStringsViewModel>();
            services.AddSingleton<PluginManager>();
            services.AddSingleton<IExtension, SamplePlugin>();
            services.AddSingleton<IExtension, ArchivePlugin>();
            Services = services.BuildServiceProvider();

            var configs = Services.GetRequiredService<Configs>();
            SharedIconProvider = Services.GetRequiredService<MainIconProvider>();
            var locService = Services.GetRequiredService<LocalizationService>();
            locService.SetLanguage(configs.Language);
            LocalizationService = locService;
            ML = Services.GetRequiredService<MultiLanguageStringsViewModel>();
            ShellIconHelper.Configs = configs;
            this.UnhandledException += (s, e) =>
            {
                Debug.WriteLine("未处理异常: " + e.Exception);
                CrashLog("UnhandledException: " + e.Exception);
                e.Handled = true;
            };
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                var dispatcher = DispatcherQueue.GetForCurrentThread();
                var configs = Services.GetRequiredService<Configs>();
                var fileOperator = Services.GetRequiredService<IFileOperator>();
                var iconProvider = Services.GetRequiredService<IIconProvider>();
                SharedViewModel = new MainWindowViewModel(iconProvider, dispatcher, configs, fileOperator, ML);

                PluginManager = Services.GetRequiredService<PluginManager>();
                PluginManager.SetDispatcherQueue(dispatcher);

                _window = new Views.MainWindowView();
                MainWindow = _window;
                _window.Activate();

                _ = PluginManager.LoadAllAsync();
                _ = SharedViewModel.DeferredInitializeAsync();
            }
            catch (Exception ex)
            {
                CrashLog("OnLaunched failed: " + ex);
                throw;
            }
        }
    }
}
