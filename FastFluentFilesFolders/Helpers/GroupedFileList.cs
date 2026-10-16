using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace FastFluentFilesFolders.Helpers
{
    public class FileGroupHeader : FileSystemNodeViewModel
    {
        public FileGroupHeader(string key) : base(key, false, true, null!, null!, true)
        {
            Name = key;
            SortByTime = key;
            IsGroupExpanded = true;
        }
    }

    public class GroupedFileList : ObservableCollection<FileSystemNodeViewModel>
    {
        private readonly Dictionary<string, List<FileSystemNodeViewModel>> _groupChildren = new();
        private readonly Dictionary<string, FileSystemNodeViewModel> _groupHeaders = new();
        private DispatcherQueue? _dispatcher;
        private bool _isBatchUpdating;
        private bool _isGrouped;

        /// <summary>当前是否按时间分组（供外部在复用同一数据源时传递分组状态）。</summary>
        public bool IsGrouped => _isGrouped;

        // 当前排序状态：用于新增条目时“插到正确位置”，而不是重置整个 ItemsSource（会滚回顶部）
        private string? _sortPath;
        private bool _sortAscending;
        private bool _hasActiveSort;

        public event Action? FlatListChanged;

        public void SetDispatcher(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
        }

        /// <summary>当前生效的排序列（未排序时为 null）。用于判断新挂载的内容是否已按目标排序排好。</summary>
        public string? ActiveSortPath => _hasActiveSort ? _sortPath : null;
        public bool ActiveSortAscending => _sortAscending;

        /// <summary>记录当前排序（由 LrsTableView 在排序时设置）。</summary>
        public void SetActiveSort(string sortPath, bool ascending)
        {
            _sortPath = sortPath;
            _sortAscending = ascending;
            _hasActiveSort = true;
        }

        public void ClearActiveSort()
        {
            _hasActiveSort = false;
            _sortPath = null;
        }

        private Comparison<FileSystemNodeViewModel>? GetActiveComparison()
        {
            if (!_hasActiveSort || string.IsNullOrEmpty(_sortPath)) return null;
            return GetComparison(_sortPath, _sortAscending);
        }

        /// <summary>
        /// 构造排序比较器：第一优先级“目录在文件前面”（升/降序都不受影响），
        /// 第二优先级才按所选列比较。
        /// </summary>
        private static Comparison<FileSystemNodeViewModel>? GetComparison(string sortPath, bool ascending)
        {
            Comparison<FileSystemNodeViewModel>? keyCompare = sortPath switch
            {
                "Name" => (a, b) => ascending
                    ? string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase)
                    : string.Compare(b.Name, a.Name, StringComparison.CurrentCultureIgnoreCase),
                "LastModifiedTime" => (a, b) => ascending
                    ? a.LastModifiedTime.CompareTo(b.LastModifiedTime)
                    : b.LastModifiedTime.CompareTo(a.LastModifiedTime),
                "FirstCreatedTime" => (a, b) => ascending
                    ? a.FirstCreatedTime.CompareTo(b.FirstCreatedTime)
                    : b.FirstCreatedTime.CompareTo(a.FirstCreatedTime),
                "ExactSize" => (a, b) => ascending
                    ? a.ExactSize.CompareTo(b.ExactSize)
                    : b.ExactSize.CompareTo(a.ExactSize),
                _ => null
            };

            if (keyCompare == null) return null;

            return (a, b) =>
            {
                // 目录永远排在文件前面（true 当作 1）
                var dirCompare = (b.IsDirectory ? 1 : 0).CompareTo(a.IsDirectory ? 1 : 0);
                if (dirCompare != 0) return dirCompare;
                return keyCompare(a, b);
            };
        }

        /// <summary>二分查找插入位置（列表已是该比较下的有序序列）。</summary>
        private static int LowerBound(IReadOnlyList<FileSystemNodeViewModel> list, FileSystemNodeViewModel item, Comparison<FileSystemNodeViewModel> cmp)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (cmp(list[mid], item) < 0) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        public void SetItems(IEnumerable<FileSystemNodeViewModel> items, bool grouped,
            string? sortPath = null, bool ascending = true)
        {
            _isGrouped = grouped;
            _groupChildren.Clear();
            _groupHeaders.Clear();

            if (!string.IsNullOrEmpty(sortPath))
                SetActiveSort(sortPath, ascending);
            else
                ClearActiveSort();

            var list = items.ToList();
            var comparison = string.IsNullOrEmpty(sortPath) ? null : GetComparison(sortPath, ascending);

            if (!grouped || list.Count == 0)
            {
                Clear();
                if (comparison != null && list.Count > 0)
                    list.Sort(comparison);
                foreach (var item in list)
                    Add(item);
                return;
            }

            foreach (var item in list)
            {
                var key = string.IsNullOrEmpty(item.SortByTime)
                    ? GetTimeGroup(item.LastModifiedTime)
                    : item.SortByTime;
                // 仅在实际变化时写回，避免后台构建时对已设置过的 SortByTime 重复触发 PropertyChanged
                if (!string.IsNullOrEmpty(key) && item.SortByTime != key)
                    item.SortByTime = key;
                if (!_groupChildren.ContainsKey(key))
                    _groupChildren[key] = new List<FileSystemNodeViewModel>();
                _groupChildren[key].Add(item);
            }

            // 只在第一次 RebuildFlat 之前排好各组，避免“先建一遍、再排一遍”的重复构建
            if (comparison != null)
            {
                foreach (var (key, children) in _groupChildren)
                    children.Sort(comparison);
            }

            RebuildFlat();
        }

        public void AddItem(FileSystemNodeViewModel item)
        {
            var comparison = GetActiveComparison();

            // 平铺模式（非分组目录）：按当前排序插到正确位置，未排序时追加
            if (!_isGrouped)
            {
                if (comparison == null)
                {
                    Add(item);
                }
                else
                {
                    Insert(LowerBound(this, item, comparison), item);
                }
                return;
            }

            var key = string.IsNullOrEmpty(item.SortByTime)
                ? GetTimeGroup(item.LastModifiedTime)
                : item.SortByTime;
            if (!string.IsNullOrEmpty(key) && item.SortByTime != key)
                item.SortByTime = key;
            if (!_groupChildren.ContainsKey(key))
                _groupChildren[key] = new List<FileSystemNodeViewModel>();
            _groupChildren[key].Add(item);
            if (comparison != null)
                _groupChildren[key].Sort(comparison);

            if (_isBatchUpdating) return;

            var header = GetOrCreateHeader(key, _groupChildren[key]);
            if (!_groupHeaders.ContainsKey(key))
            {
                _groupHeaders[key] = header;
                int headerIdx = GetHeaderInsertIndex(key);
                Insert(headerIdx, header);
                if (header.IsGroupExpanded)
                {
                    int posInGroup = _groupChildren[key].IndexOf(item);
                    Insert(headerIdx + 1 + (posInGroup >= 0 ? posInGroup : _groupChildren[key].Count - 1), item);
                }
            }
            else if (header.IsGroupExpanded)
            {
                int headerIdx = IndexOf(header);
                int posInGroup = _groupChildren[key].IndexOf(item);
                Insert(headerIdx + 1 + (posInGroup >= 0 ? posInGroup : _groupChildren[key].Count - 1), item);
            }
        }

        public void RemoveItem(FileSystemNodeViewModel item)
        {
            // 平铺模式（非分组目录）：直接从列表中移除
            if (!_isGrouped)
            {
                int idx = IndexOf(item);
                if (idx >= 0)
                    RemoveAt(idx);
                return;
            }

            var key = item.SortByTime;
            if (string.IsNullOrEmpty(key) || !_groupChildren.TryGetValue(key, out var list))
                return;
            list.Remove(item);

            if (_isBatchUpdating) return;

            int idx2 = IndexOf(item);
            if (idx2 >= 0)
                RemoveAt(idx2);

            if (list.Count == 0)
            {
                _groupChildren.Remove(key);
                if (_groupHeaders.TryGetValue(key, out var header))
                {
                    int headerIdx = IndexOf(header);
                    if (headerIdx >= 0)
                        RemoveAt(headerIdx);
                    _groupHeaders.Remove(key);
                }
            }
        }

        private int GetHeaderInsertIndex(string key)
        {
            var sortedKeys = _groupChildren.Keys
                .OrderBy(k => TimeGroupSortConverter.GetSortOrder(k))
                .ThenBy(k => k)
                .ToList();
            var targetIdx = sortedKeys.IndexOf(key);
            int flatIdx = 0;
            for (int i = 0; i < targetIdx; i++)
            {
                var k = sortedKeys[i];
                if (_groupHeaders.TryGetValue(k, out var h))
                {
                    flatIdx++; // header itself
                    if (h.IsGroupExpanded)
                        flatIdx += _groupChildren[k].Count;
                }
            }
            return flatIdx;
        }

        private bool _rebuildPending;

        private void ScheduleRebuild()
        {
            if (_isBatchUpdating) return;
            if (_rebuildPending) return;
            _rebuildPending = true;
            _dispatcher?.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _rebuildPending = false;
                RebuildFlat();
                FlatListChanged?.Invoke();
            });
        }

        public void BeginBatchUpdate()
        {
            _isBatchUpdating = true;
        }

        public void EndBatchUpdate()
        {
            _isBatchUpdating = false;
            RebuildFlat();
            FlatListChanged?.Invoke();
        }

        public void ToggleGroup(FileSystemNodeViewModel header)
        {
            if (!header.IsPlaceholder) return;
            var key = header.SortByTime;
            if (!_groupChildren.ContainsKey(key)) return;

            header.IsGroupExpanded = !header.IsGroupExpanded;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(header.IsGroupExpanded)));

            var dispatcher = _dispatcher ?? DispatcherQueue.GetForCurrentThread();
            if (dispatcher == null) return;

            dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                RebuildFlat();
                FlatListChanged?.Invoke();
            });
        }

        public void RefreshHeaderNames()
        {
            foreach (var (key, header) in _groupHeaders)
                header.Name = GetLocalizedGroupName(key);
        }

        private void RebuildFlat()
        {
            Clear();

            if (_groupChildren.Count == 0) return;

            var sortedKeys = _groupChildren.Keys
                .OrderBy(k => TimeGroupSortConverter.GetSortOrder(k))
                .ThenBy(k => k);

            foreach (var key in sortedKeys)
            {
                var children = _groupChildren[key];
                var header = GetOrCreateHeader(key, children);
                _groupHeaders[key] = header;
                Add(header);

                if (header.IsGroupExpanded)
                {
                    foreach (var child in children)
                        Add(child);
                }
            }
        }

        private FileGroupHeader GetOrCreateHeader(string key, List<FileSystemNodeViewModel> children)
        {
            if (_groupHeaders.TryGetValue(key, out var existing))
                return (FileGroupHeader)existing;

            var header = new FileGroupHeader(key);
            header.Name = GetLocalizedGroupName(key);
            if (children.Count > 0)
            {
                header.LastModifiedTime = children.Max(c => c.LastModifiedTime);
                header.LastModifiedTimeString = key;
                header.FirstCreatedTime = children.Min(c => c.FirstCreatedTime);
                header.FirstCreatedTimeString = key;
                header.ExactSize = children.Sum(c => c.ExactSize);
                header.VisualSize = key;
            }
            return header;
        }

        public void ResetSort()
        {
            ClearActiveSort();
            if (_groupHeaders.Count == 0) return;
            var allItems = _groupChildren.Values.SelectMany(c => c).ToList();
            SetItems(allItems, true);
        }

        public void SortWithinGroups(string sortPath, bool ascending)
        {
            SetActiveSort(sortPath, ascending);
            var comparison = GetComparison(sortPath, ascending);
            if (comparison == null) return;

            if (_groupChildren.Count == 0)
            {
                // 平铺模式：直接对当前列表排序
                var all = this.ToList();
                all.Sort(comparison);
                Clear();
                foreach (var item in all)
                    Add(item);
                return;
            }

            foreach (var (key, children) in _groupChildren)
                children.Sort(comparison);

            RebuildFlat();
        }

        public static string GetTimeGroup(DateTime dateTime)
        {
            var now = DateTime.Now;
            var local = dateTime.Kind == DateTimeKind.Utc ? dateTime.ToLocalTime() : dateTime;
            var today = now.Date;

            if (local.Date == today) return "group_today";
            if (local.Date == today.AddDays(-1)) return "group_yesterday";
            var diffDays = (today - local.Date).Days;
            if (diffDays < 7 && local.DayOfWeek < today.DayOfWeek) return "group_earlier_this_week";
            if (diffDays < 14) return "group_last_week";
            if (local.Year == now.Year && local.Month == now.Month) return "group_earlier_this_month";
            if (new DateTime(now.Year, now.Month, 1).AddMonths(-1) == new DateTime(local.Year, local.Month, 1)) return "group_last_month";
            if (local.Year == now.Year) return "group_earlier_this_year";
            if (local.Year == now.Year - 1) return "group_last_year";
            return "group_long_ago";
        }

        public static string GetLocalizedGroupName(string key) => key switch
        {
            "group_today" => App.ML.TimeGroupToday,
            "group_yesterday" => App.ML.TimeGroupYesterday,
            "group_earlier_this_week" => App.ML.TimeGroupEarlierThisWeek,
            "group_last_week" => App.ML.TimeGroupLastWeek,
            "group_earlier_this_month" => App.ML.TimeGroupEarlierThisMonth,
            "group_last_month" => App.ML.TimeGroupLastMonth,
            "group_earlier_this_year" => App.ML.TimeGroupEarlierThisYear,
            "group_last_year" => App.ML.TimeGroupLastYear,
            "group_long_ago" => App.ML.TimeGroupLongAgo,
            _ => key
        };
    }

    public static class TimeGroupSortConverter
    {
        public static int GetSortOrder(string groupName) => groupName switch
        {
            "group_today" => 0, "group_yesterday" => 1, "group_earlier_this_week" => 2, "group_last_week" => 3,
            "group_earlier_this_month" => 4, "group_last_month" => 5, "group_earlier_this_year" => 6,
            "group_last_year" => 7, "group_long_ago" => 8, _ => 9
        };
    }
}
