using FastFluentFilesFolders.Extensions;
using FastFluentFilesFolders.Extensions.Interfaces;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using FastFluentFilesFolders.ViewModels;
using CommunityToolkit.WinUI.Controls;
using System;
using System.Linq;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using System.Diagnostics;

namespace FastFluentFilesFolders.Views
{
    public sealed partial class SettingsView : Page
    {
        private int _fumoKeyIndex;
        private SettingsViewModel? _settingsVm;

        public SettingsView()
        {
            _settingsVm = new SettingsViewModel(App.ML);
            DataContext = _settingsVm;
            InitializeComponent();
            LoadPluginSettings();
            RefreshInstalledPluginsList();

            AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnSettingsViewKeyDown), true);

            AppVersionTextBlock.Text = Configs.Version ?? "?";
		}

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadPluginSettings();
            RefreshInstalledPluginsList();

            if (DataContext is SettingsViewModel vm &&
                App.LocalizationService.CurrentLanguage == "fumo")
                vm.UnlockFumoLanguage();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            AutoSaveSettings();
            // NavigationCacheMode=Disabled：页面销毁前必须退订，否则 VM 被单例 AppConfigs 泄漏
            _settingsVm?.Unsubscribe();
        }

        private void OnSettingsViewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var key = e.Key;
            if (key < VirtualKey.A || key > VirtualKey.Z) return;

            var ch = (char)('a' + (key - VirtualKey.A));
            var expected = "fumo";

            if (_fumoKeyIndex < expected.Length && ch == expected[_fumoKeyIndex])
            {
                _fumoKeyIndex++;
                if (_fumoKeyIndex == expected.Length)
                {
                    _fumoKeyIndex = 0;
                    if (DataContext is SettingsViewModel vm)
                        vm.UnlockFumoLanguage();
                }
            }
            else
            {
                _fumoKeyIndex = ch == expected[0] ? 1 : 0;
            }
        }

        private void LoadPluginSettings()
        {
            PluginSettingsStack.Children.Clear();
            var plugins = App.PluginManager?.GetSettingsPlugins();
            if (plugins == null) return;

            bool hasItems = false;
            foreach (var plugin in plugins)
            {
                var cards = plugin.CreateSettingsCards();
                foreach (var card in cards)
                {
                    PluginSettingsStack.Children.Add(card);
                    hasItems = true;
                }
            }

            PluginsSection.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshInstalledPluginsList()
        {
            var list = InstalledPluginsList;
            list.Items.Clear();

            var manifest = PluginImporter.LoadManifest();
            foreach (var pkg in manifest.Plugins)
            {
                list.Items.Add($"{pkg.Name}  v{pkg.Version}  ({pkg.Id})");
            }

            InstalledPluginsCard.Visibility = manifest.Plugins.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void OnImportPluginClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                var hwnd = WindowNative.GetWindowHandle(App.MainWindow!);
                InitializeWithWindow.Initialize(picker, hwnd);

                picker.SuggestedStartLocation = PickerLocationId.Downloads;
                picker.ViewMode = PickerViewMode.List;
                picker.FileTypeFilter.Add(".zip");
                picker.FileTypeFilter.Add(".lrsplug");

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                ImportPluginBtn.IsEnabled = false;
                ImportPluginBtn.Content = App.ML.Get("PluginImport.Importing");

                var (success, message, package) = await App.PluginManager.ImportAndLoadPluginAsync(file.Path);

                if (success)
                {
                    LoadPluginSettings();
                    RefreshInstalledPluginsList();
                    ShowMessageDialog(App.ML.Get("PluginImport.ImportSuccessTitle"),
                        $"{package?.Name ?? ""} v{package?.Version ?? ""}");
                }
                else
                {
                    ShowMessageDialog(App.ML.Get("PluginImport.ImportFailedTitle"),
                        App.ML.Get(message));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsView] Import error: {ex.Message}");
                ShowMessageDialog(App.ML.Get("PluginImport.ImportFailedTitle"), ex.Message);
            }
            finally
            {
                ImportPluginBtn.IsEnabled = true;
                ImportPluginBtn.Content = App.ML.PluginImportBtn;
            }
        }

        private async void OnUninstallPluginClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string displayText) return;

            var manifest = PluginImporter.LoadManifest();
            var idx = InstalledPluginsList.Items.IndexOf(displayText);
            if (idx < 0 || idx >= manifest.Plugins.Count) return;

            var pluginId = manifest.Plugins[idx].Id;

            var dialog = new ContentDialog
            {
                Title = App.ML.Get("PluginImport.UninstallConfirmTitle"),
                Content = string.Format(App.ML.Get("PluginImport.UninstallConfirmFmt"), displayText),
                PrimaryButtonText = App.ML.Get("PluginImport.UninstallBtn"),
                CloseButtonText = App.ML.Get("PluginImport.CancelBtn"),
                XamlRoot = App.MainWindow!.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var (success, message) = await App.PluginManager.UninstallPluginAsync(pluginId);
            if (success)
            {
                LoadPluginSettings();
                RefreshInstalledPluginsList();
            }
            else
            {
                ShowMessageDialog(App.ML.Get("PluginImport.UninstallFailedTitle"),
                    App.ML.Get(message));
            }
        }

        /// <summary>“检查更新”卡片：唤起更新弹窗（检查结果、下载进度、安装都在弹窗里）。</summary>
        private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
        {
            var service = App.SharedViewModel?.AutoUpdateService;
            if (service == null) return;

            var xamlRoot = App.MainWindow?.Content?.XamlRoot;
            if (xamlRoot == null) return;

            CheckUpdateBtn.IsEnabled = false;
            try
            {
                var dialog = new UpdateDialog(service) { XamlRoot = xamlRoot };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsView] 打开更新弹窗失败: {ex.Message}");
            }
            finally
            {
                CheckUpdateBtn.IsEnabled = true;
            }
        }

        private async void ShowMessageDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = App.ML.CmdOk,
                XamlRoot = App.MainWindow!.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }

        private void AutoSaveSettings()
        {
            App.SharedViewModel?.AppConfigs?.SaveConfig();
        }
    }
}
