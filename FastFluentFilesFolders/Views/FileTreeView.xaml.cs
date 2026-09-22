using FastFluentFilesFolders.Services;
using FastFluentFilesFolders.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Windows.System;

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
                this.DataContext = VM;
                VM.PropertyChanged += OnVmPropertyChanged;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeComponent failed: {ex}");
                throw;
            }
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedTab))
                SyncTreeSelectionToTab();
        }

        // 程序化同步选中（切换标签页时高亮目录树）期间不触发导航：
        // IsSelected 双向绑定会把 node.IsSelected=true 写回 TreeViewItem，
        // 再次触发 SelectionChanged → 与 ActivateTab 的导航重复执行并污染标签页历史栈
        private bool _suppressTreeNavigation;

        private void SyncTreeSelectionToTab()
        {
            var path = VM.CurrentTab?.Path;
            if (string.IsNullOrEmpty(path)) return;
            var node = VM.FindNodeByPath(path);
            if (node != null && !node.IsPlaceholder)
            {
                _suppressTreeNavigation = true;
                try { node.IsSelected = true; }
                finally { _suppressTreeNavigation = false; }
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

        // 目录树单击即导航：与文件表格双击走同一套 SelectedFolder 流程。
        // 原来这里人为 Delay(50ms) 再切换，导致“非双击进文件夹”每次都有小卡顿；
        // 直接导航即可，快速连点产生的过期刷新由 NavigationVersion 守卫丢弃。
        private void TreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
        {
            if (_suppressTreeNavigation) return;

            var selectedItem = args.AddedItems.FirstOrDefault() as FileSystemNodeViewModel;
            if (selectedItem is null)
                return;

            if (selectedItem is FileSystemNodeViewModel folder)
            {
                if (ReferenceEquals(folder, VM.SelectedFolder))
                    _ = VM.UpdateCurrentFolderContentAsync(folder, version: null);
                else
                    VM.SelectedFolder = folder;
            }
        }

        private void TreeView_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
                return;

            var isAltDown = ((int)Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & 1) != 0;
            var isCtrlDown = ((int)Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & 1) != 0;

            if (isAltDown || isCtrlDown)
                return;

            e.Handled = true;
            var treeView = sender as TreeView;
            var selectedItem = treeView?.SelectedItem as FileSystemNodeViewModel;
            if (selectedItem != null && !selectedItem.IsPlaceholder)
                VM.OpenItem(selectedItem);
        }
    }

    /// <summary>Icon == null → Visible（用于显示字形回退）。</summary>
    public sealed class NullToVisibleConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value == null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    /// <summary>Icon == null → Collapsed（用于隐藏空的 Image 占位）。</summary>
    public sealed class NullToCollapsedConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value == null ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
