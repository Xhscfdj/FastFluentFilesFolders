using FastFluentFilesFolders.Extensions;
using FastFluentFilesFolders.Extensions.Interfaces;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FastFluentFilesFolders.ViewModels;
using CommunityToolkit.WinUI.Controls;
using System;
using System.Linq;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.Diagnostics;

namespace FastFluentFilesFolders.Views
{
    public sealed partial class SettingsView : Page
    {
        public SettingsView()
        {
            DataContext = new SettingsViewModel(App.ML);
            InitializeComponent();
            LoadPluginSettings();
            RefreshInstalledPluginsList();
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

        private async void ShowMessageDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = App.MainWindow!.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }

        private void SaveSettings(object sender, RoutedEventArgs e)
        {
            App.SharedViewModel.AppConfigs.SaveConfig();
        }
    }
}
