using CommunityToolkit.Mvvm.ComponentModel;
using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Threading;

namespace FastFluentFilesFolders.Models
{
    /// <summary>
    /// 单个标签页的独立状态：浏览位置、前进/后退历史、搜索状态。
    /// </summary>
    // {Binding} 需要 ICustomProperty；AOT / 关闭 BuiltInComInterop 时反射版不可用，
    // TabView 的 TabItemTemplate 正是用 {Binding Title} / {Binding FolderNode.Icon} 绑这个类型。
    [WinRT.GeneratedBindableCustomProperty]
    public partial class ExplorerTab : ObservableObject
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");

        [ObservableProperty] public partial string Title { get; set; } = string.Empty;
        [ObservableProperty] public partial string Path { get; set; } = string.Empty;

        /// <summary>当前标签页正在浏览的文件夹节点（可复用，避免反复重建）</summary>
        [ObservableProperty] public partial FileSystemNodeViewModel? FolderNode { get; set; }

        /// <summary>
        /// 标签头显示的文件夹图标。图标的加载是异步的（首次读 <c>FolderNode.Icon</c> 只发起请求、
        /// 立刻返回 null），所以这里把节点图标的“迟到”通知转发成 <see cref="Icon"/> 的属性通知，
        /// 标签头才能从空白补成真实图标。
        /// </summary>
        public ImageSource? Icon => FolderNode?.Icon;

        partial void OnFolderNodeChanged(FileSystemNodeViewModel? oldValue, FileSystemNodeViewModel? newValue)
        {
            if (oldValue != null) oldValue.PropertyChanged -= OnFolderNodePropertyChanged;
            if (newValue != null) newValue.PropertyChanged += OnFolderNodePropertyChanged;
        }

        private void OnFolderNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileSystemNodeViewModel.Icon))
                OnPropertyChanged(nameof(Icon));
        }

        /// <summary>标签页被关闭时断开对文件夹节点的订阅，避免已关闭的标签页跟着热缓存里的节点一起活着。</summary>
        public void Detach()
        {
            if (FolderNode != null) FolderNode.PropertyChanged -= OnFolderNodePropertyChanged;
        }

        /// <summary>该标签页独立的后退历史</summary>
        public List<string> BackStack { get; } = new();
        /// <summary>该标签页独立的前进历史</summary>
        public List<string> ForwardStack { get; } = new();
        public string? PreviousPath { get; set; }
        public int NavigationVersion { get; set; }
        public bool IsNavigatingFromHistory { get; set; }
        public FileSystemNodeViewModel? FolderToRelease { get; set; }
        public string? PendingSelectPath { get; set; }

        /// <summary>
        /// 该标签页独立的排序状态：SortPath 为表头列的 SortMemberPath（"Name"/"LastModifiedTime"/
        /// "FirstCreatedTime"/"ExactSize"），为空表示未排序（沿用配置里的默认排序方式）。
        /// 换目录、刷新、切换标签页都靠它恢复，不再依赖表格控件内部的残留字段。
        /// </summary>
        public string? SortPath { get; set; }
        public bool SortAscending { get; set; } = true;

        /// <summary>该标签页当前是否处于自定义排序状态</summary>
        public bool HasSort => !string.IsNullOrEmpty(SortPath);

        /// <summary>该标签页独立的搜索状态</summary>
        public bool IsSearchMode { get; set; }
        public string SearchText { get; set; } = string.Empty;
        public bool IsSearching { get; set; }
        public ObservableCollection<FileSystemNodeViewModel> SearchResults { get; set; } = new();
        public CancellationTokenSource? SearchCts { get; set; }
    }
}
