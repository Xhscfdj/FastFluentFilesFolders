using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using System;
using System.ComponentModel;
using System.Diagnostics;
using WinRT;


namespace FastFluentFilesFolders.Views
{
    public sealed partial class MainWindowView : Window
    {
        private MainWindowViewModel VM => App.SharedViewModel;
        private string _currentPage = "explorer";
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _configurationSource;

        public MainWindowView()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            SetWindowIcon();
            RootGrid.DataContext = VM;
            VM.PropertyChanged += OnVMPropertyChanged;

            if (VM.AppConfigs != null)
            {
                VM.AppConfigs.PropertyChanged += OnConfigPropertyChanged;
                UpdateBackDrop(VM.AppConfigs.SystemBackdropMode);
            }
        }

        /// <summary>设置窗口图标（未打包运行不会自动从包清单取图标，需显式设置）。</summary>
        private void SetWindowIcon()
        {
            try
            {
                var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                if (System.IO.File.Exists(iconPath))
                    AppWindow.SetIcon(iconPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] SetIcon failed: {ex.Message}");
            }
        }

        private void OnConfigPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Configs.SystemBackdropMode))
            {
                UpdateBackDrop(VM.AppConfigs?.SystemBackdropMode);
            }
        }

        private void OnVMPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsSettingsOpen))
            {
                if (VM.IsSettingsOpen)
                    NavigateTo("settings");
                else
                    NavigateTo("explorer");
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsReady) && VM.IsReady)
            {
                ExplorerArea.Visibility = Visibility.Visible;
                SplashFadeOut.Begin();
            }
        }

        private void OnSplashFadeOutCompleted(object sender, object e)
        {
            SplashOverlay.Visibility = Visibility.Collapsed;
        }

        private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                NavigateTo("settings");
                return;
            }

            var item = args.SelectedItemContainer ?? args.SelectedItem as NavigationViewItem;
            if (item?.Tag is string tag)
            {
                NavigateTo(tag);
            }
        }

        private void NavigateTo(string pageTag)
        {
            if (_currentPage == pageTag) return;

            _currentPage = pageTag;

            Debug.WriteLine($"[MainWindowView] NavigateTo: {pageTag}");

            if (pageTag == "explorer")
            {
                VM.IsSettingsOpen = false;
                ContentFrame.Visibility = Visibility.Collapsed;
                ExplorerArea.Visibility = Visibility.Visible;
            }
            else if (pageTag == "plugins")
            {
                ExplorerArea.Visibility = Visibility.Collapsed;
                ContentFrame.Visibility = Visibility.Visible;
                ContentFrame.Navigate(typeof(PluginsPage));
            }
            else if (pageTag == "settings")
            {
                ExplorerArea.Visibility = Visibility.Collapsed;
                ContentFrame.Visibility = Visibility.Visible;
                ContentFrame.Navigate(typeof(SettingsView));
            }

            UpdateNavSelection(pageTag);
        }

        private void UpdateNavSelection(string pageTag)
        {
            if (pageTag == "settings")
            {
                MainNav.SelectedItem = MainNav.SettingsItem;
                return;
            }

            foreach (var mi in MainNav.MenuItems)
            {
                if (mi is NavigationViewItem navItem && navItem.Tag as string == pageTag)
                {
                    MainNav.SelectedItem = navItem;
                    return;
                }
            }
        }

        private void UpdateBackDrop(string backdrop)
        {
            DisposeAcrylicController();

            if (backdrop is "Acrylic" or "AcrylicThin")
            {
                SystemBackdrop = null;
                if (DesktopAcrylicController.IsSupported())
                {
                    _configurationSource = new SystemBackdropConfiguration
                    {
                        IsInputActive = true
                    };
                    SetConfigurationSourceTheme();

                    Activated += OnWindowActivated;
                    Closed += OnWindowClosed;
                    if (Content is FrameworkElement fe)
                        fe.ActualThemeChanged += OnWindowThemeChanged;

                    _acrylicController = new DesktopAcrylicController
                    {
                        Kind = backdrop == "AcrylicThin"
                            ? DesktopAcrylicKind.Thin
                            : DesktopAcrylicKind.Base
                    };
                    _acrylicController.AddSystemBackdropTarget(
                        ((WinRT.IWinRTObject)this).As<ICompositionSupportsSystemBackdrop>());
                    _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
                }
            }
            else
            {
                SystemBackdrop = backdrop switch
                {
                    "Mica" => new MicaBackdrop { Kind = MicaKind.Base },
                    "MicaAlt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                    _ => null,
                };
            }
        }

        private void DisposeAcrylicController()
        {
            if (_acrylicController != null)
            {
                Activated -= OnWindowActivated;
                Closed -= OnWindowClosed;
                if (Content is FrameworkElement fe)
                    fe.ActualThemeChanged -= OnWindowThemeChanged;

                _acrylicController.Dispose();
                _acrylicController = null;
                _configurationSource = null;
            }
        }

        private void SetConfigurationSourceTheme()
        {
            if (_configurationSource == null) return;
            _configurationSource.Theme = Content is FrameworkElement fe
                ? fe.ActualTheme switch
                {
                    ElementTheme.Dark => SystemBackdropTheme.Dark,
                    ElementTheme.Light => SystemBackdropTheme.Light,
                    _ => SystemBackdropTheme.Default,
                }
                : SystemBackdropTheme.Default;
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            if (_configurationSource != null)
                _configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            DisposeAcrylicController();
            VM.ClosePropertiesWindow();
        }

        private void OnWindowThemeChanged(FrameworkElement sender, object args)
        {
            SetConfigurationSourceTheme();
        }
    }
}