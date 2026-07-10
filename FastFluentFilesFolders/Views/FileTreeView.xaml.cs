using FastFluentFilesFolders.Services;
using FastFluentFilesFolders.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.Views
{
    public sealed partial class FileTreeView : Page
    {
        private MainWindowViewModel VM => App.SharedViewModel;

        public FileTreeView()
        {
            Configs configs = App.SharedViewModel.AppConfigs;
            try
            {
                InitializeComponent();
                var uiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                IIconProvider iconProvider = new WindowsIconProvider();
                this.DataContext = VM;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeComponent failed: {ex}");
                throw;
            }
        }

        //private void ToggleSettings(object sender, RoutedEventArgs e)
        //{
        //    VM.IsSettingsOpen = !VM.IsSettingsOpen;

        //    if (VM.IsSettingsOpen)
        //    {
        //        SettingsBtnIcon.Glyph = "\uE72B";
        //        SettingsBtnLabel.Text = "返回";
        //    }
        //    else
        //    {
        //        SettingsBtnIcon.Glyph = "\uE713";
        //        SettingsBtnLabel.Text = "设置";
        //    }
        //}

        private async void TreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
        {
            Debug.WriteLine($"[TreeView_SelectionChanged] Entered. AddedItems count: {args.AddedItems.Count}");
            var selectedItem = args.AddedItems.FirstOrDefault() as FileSystemNodeViewModel;
            if (selectedItem is null)
            {
                Debug.WriteLine("[TreeView_SelectionChanged] No FileSystemNodeViewModel selected.");
                return;
            }
            await Task.Delay(50);
            Debug.WriteLine($"[TreeView_SelectionChanged] Selected item: {selectedItem.Name}, Type: {selectedItem.NodeTypeName}");
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (selectedItem is FileSystemNodeViewModel folder)
                {
                    Debug.WriteLine($"[TreeView_SelectionChanged] Setting SelectedFolder to {folder.FullPath}");
                    var vm = DataContext as MainWindowViewModel;
                    if (vm != null) vm.SelectedFolder = folder;
                }
                else if (!selectedItem.IsDirectory)
                {
                    Debug.WriteLine("[TreeView_SelectionChanged] Selected item is not a folder. Setting SelectedFolder to null.");
                }
            });
        }
    }
}
