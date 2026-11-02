using FastFluentFilesFolders.Models;
using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.Specialized;
using System.ComponentModel;
using Windows.System;

namespace FastFluentFilesFolders.Views
{
    public sealed partial class ExplorerPage : Page
    {
        private MainWindowViewModel VM => App.SharedViewModel;

        /// <summary>正在按 VM 同步 TabItems（期间忽略 TabView 回抛的选中变更）。</summary>
        private bool _syncingTabItems;

        public ExplorerPage()
        {
            InitializeComponent();
            DataContext = App.SharedViewModel;

            // Ctrl+T 新建标签页
            var newTabAccel = new KeyboardAccelerator { Key = VirtualKey.T, Modifiers = VirtualKeyModifiers.Control };
            newTabAccel.Invoked += (_, args) =>
            {
                args.Handled = true;
                VM.NewTabCommand.Execute(null);
                SyncTabSelection();
            };
            RootGrid.KeyboardAccelerators.Add(newTabAccel);

            // Ctrl+W 关闭当前标签页
            var closeTabAccel = new KeyboardAccelerator { Key = VirtualKey.W, Modifiers = VirtualKeyModifiers.Control };
            closeTabAccel.Invoked += (_, args) =>
            {
                args.Handled = true;
                VM.CloseCurrentTab();
                SyncTabSelection();
            };
            RootGrid.KeyboardAccelerators.Add(closeTabAccel);

            this.Loaded += (_, _) =>
            {
                VM.Tabs.CollectionChanged += OnTabsCollectionChanged;
                SyncTabItems();
                SyncTabSelection();
            };
            this.Unloaded += (_, _) => VM.Tabs.CollectionChanged -= OnTabsCollectionChanged;
        }

        private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            SyncTabItems();
            SyncTabSelection();
        }

        /// <summary>
        /// 按 <see cref="MainWindowViewModel.Tabs"/> 显式维护 <see cref="TabView.TabItems"/>。
        /// 不用 <c>TabItemsSource</c> 绑定托管集合：Native AOT 下 TabView 把 ItemsSource
        /// 转成 IBindableIterable / IIterable 会失败，布局期抛 COMException 0x80004005
        /// （microsoft-ui-xaml#11248 / #10409 / #11065）。TabViewItem 是原生类型，
        /// 一个个加到 TabItems 里不经过那条 ABI 转换路径。
        /// </summary>
        private void SyncTabItems()
        {
            if (_syncingTabItems) return;
            _syncingTabItems = true;
            try
            {
                var tabs = VM.Tabs;

                // 1) 移除 VM 里已经不存在的标签
                for (int i = TabsCtrl.TabItems.Count - 1; i >= 0; i--)
                {
                    if (TabsCtrl.TabItems[i] is not TabViewItem item || item.Tag is not ExplorerTab tab || !tabs.Contains(tab))
                    {
                        if (TabsCtrl.TabItems[i] is TabViewItem removed && removed.Tag is ExplorerTab removedTab)
                            removedTab.PropertyChanged -= OnTabViewModelChanged;
                        TabsCtrl.TabItems.RemoveAt(i);
                    }
                }

                // 2) 追加新出现的标签（按 VM 顺序插到对应位置）
                for (int i = 0; i < tabs.Count; i++)
                {
                    var tab = tabs[i];
                    if (IndexOfTabItem(tab) >= 0) continue;
                    tab.PropertyChanged += OnTabViewModelChanged;
                    TabsCtrl.TabItems.Insert(System.Math.Min(i, TabsCtrl.TabItems.Count), CreateTabItem(tab));
                }
            }
            finally
            {
                _syncingTabItems = false;
            }
        }

        private int IndexOfTabItem(ExplorerTab tab)
        {
            for (int i = 0; i < TabsCtrl.TabItems.Count; i++)
            {
                if (TabsCtrl.TabItems[i] is TabViewItem item && ReferenceEquals(item.Tag, tab))
                    return i;
            }
            return -1;
        }

        /// <summary>标签头：文件夹图标 + 标题（等价于原来 TabItemTemplate 的内容）。</summary>
        private static TabViewItem CreateTabItem(ExplorerTab tab)
        {
            var icon = new Image
            {
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Source = tab.FolderNode?.Icon
            };
            var title = new TextBlock
            {
                Text = tab.Title,
                VerticalAlignment = VerticalAlignment.Center
            };
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(icon);
            header.Children.Add(title);

            return new TabViewItem
            {
                Tag = tab,
                IsClosable = true,
                Header = header
            };
        }

        /// <summary>标题 / 所在文件夹变化时刷新标签头。</summary>
        private void OnTabViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ExplorerTab tab) return;
            if (e.PropertyName is not (nameof(ExplorerTab.Title) or nameof(ExplorerTab.FolderNode))) return;

            var index = IndexOfTabItem(tab);
            if (index < 0) return;
            if (TabsCtrl.TabItems[index] is TabViewItem { Header: StackPanel header } && header.Children.Count >= 2)
            {
                if (header.Children[0] is Image icon) icon.Source = tab.FolderNode?.Icon;
                if (header.Children[1] is TextBlock title) title.Text = tab.Title;
            }
        }

        /// <summary>让 TabView 的选中项与 VM 保持一致</summary>
        private void SyncTabSelection()
        {
            var selected = VM.SelectedTab;
            if (selected == null) return;
            var index = IndexOfTabItem(selected);
            if (index < 0) return;
            if (TabsCtrl.TabItems[index] is TabViewItem item && !ReferenceEquals(TabsCtrl.SelectedItem, item))
                TabsCtrl.SelectedItem = item;
        }

        private void OnTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingTabItems) return;
            if ((TabsCtrl.SelectedItem as TabViewItem)?.Tag is ExplorerTab tab)
                VM.SwitchToTab(tab);
        }

        private void OnTabsCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            if (args.Item is TabViewItem { Tag: ExplorerTab tab })
            {
                VM.CloseTab(tab);
                SyncTabItems();
                SyncTabSelection();
            }
        }

        private void OnTabsAddTabClick(TabView sender, object args)
        {
            VM.NewTabCommand.Execute(null);
            SyncTabItems();
            SyncTabSelection();
        }
    }
}
