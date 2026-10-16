using CommunityToolkit.Mvvm.ComponentModel;
using FastFluentFilesFolders.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Threading;

namespace FastFluentFilesFolders.Models
{
    /// <summary>
    /// 单个标签页的独立状态：浏览位置、前进/后退历史、搜索状态。
    /// </summary>
    public partial class ExplorerTab : ObservableObject
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");

        [ObservableProperty] private string _title = string.Empty;
        [ObservableProperty] private string _path = string.Empty;

        /// <summary>当前标签页正在浏览的文件夹节点（可复用，避免反复重建）</summary>
        [ObservableProperty] private FileSystemNodeViewModel? _folderNode;

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
