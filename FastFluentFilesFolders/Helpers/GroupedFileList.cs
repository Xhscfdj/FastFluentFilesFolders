using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace FastFluentFilesFolders.Helpers
{
    [WinRT.GeneratedBindableCustomProperty]
    public partial class FileGroupHeader : FileSystemNodeViewModel
    {
        public FileGroupHeader(string key) : base(key, false, true, null!, null!, true)
        {
            Name = key;
            SortByTime = key;
            IsGroupExpanded = true;
        }
    }

    /// <summary>
    /// 表格数据源（未分组时与普通列表等价，分组时自动插入组头）。
    /// <para>
    /// 必须 <c>partial</c>：它作为 <c>ItemsSource</c> 跨 WinRT ABI 传给表格，Native AOT /
    /// 关闭 <c>BuiltInComInterop</c> 的构建要靠 CsWinRT 源生成器为 partial 类型生成 AOT 安全的
    /// COM 虚表（CsWinRT1028）。
    /// </para>
    /// </summary>
    public partial class GroupedFileList : ObservableCollection<FileSystemNodeViewModel>
    {
        private readonly Dictionary<string, List<FileSystemNodeViewModel>> _groupChildren = new();
        private readonly Dictionary<string, FileSystemNodeViewModel> _groupHeaders = new();
        private DispatcherQueue? _dispatcher;
        private bool _isBatchUpdating;
        private bool _isGrouped;

        // ---- 等价短路用的输入指纹（见 SetItems）----
        // 只存哈希，不存节点引用：否则这份历史会把已经 ReleaseChildren 的节点一直钉住，反而制造泄漏。
        private int _lastInputSignature;
        private int _lastInputCount;
        private bool _hasLastInput;

        /// <summary>输入序列的引用指纹：顺序敏感、不持有引用，用于判断“内容其实没变，不必整批重建”。</summary>
        private static int ComputeSignature(IReadOnlyList<FileSystemNodeViewModel> list)
        {
            var hash = new HashCode();
            for (var i = 0; i < list.Count; i++)
                hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(list[i]));
            return hash.ToHashCode();
        }

        /// <summary>
        /// 分组键是否仍然有效：<see cref="FileSystemNodeViewModel.SortByTime"/> 应当等于按当前
        /// <see cref="FileSystemNodeViewModel.LastModifiedTime"/> 算出来的时间组。不相等说明文件
        /// 改过时间（或第一次还没写回），必须重新分组，不能走等价短路。
        /// </summary>
        private static bool GroupKeysStillValid(IReadOnlyList<FileSystemNodeViewModel> list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item.IsPlaceholder) continue;
                if (!string.Equals(item.SortByTime, GetTimeGroup(item.LastModifiedTime), StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

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
        /// 供 <c>MainWindowViewModel</c> 在后台预先排序用（避免前台再排一遍）。
        /// </summary>
        internal static Comparison<FileSystemNodeViewModel>? GetComparison(string sortPath, bool ascending)
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

        /// <summary>
        /// 整批替换内容（分组/排序在内部一次做完）。
        /// </summary>
        /// <returns>
        /// true = 内容确实重建了（行容器会重新实体化，入场动画照常由表格播放）；
        /// false = 命中等价短路：内容序列与分组形态都没变，数据源一行没动。
        /// 刷新时目录里没有任何变化就走这条路 —— 此时调用方必须自己补一次视觉反馈，
        /// 否则用户点了刷新看不到任何反应。
        /// </returns>
        public bool SetItems(IEnumerable<FileSystemNodeViewModel> items, bool grouped,
            string? sortPath = null, bool ascending = true)
        {
            var list = items.ToList();
            var wantsSort = !string.IsNullOrEmpty(sortPath);

            // ---- 等价短路（S4-3'）----
            // 刷新、以及“目录热缓存命中后回访”这两种场景，节点实例是按路径复用的、顺序也没变，
            // 此时再整批 Clear + Add 完全是浪费：会丢掉全部行容器、列表闪一下、面板还要重排一遍。
            // 元数据变化由 ApplyMetadata 就地改属性、绑定自动刷新，不需要重建内容。
            //
            // 判定只看「同一批实例 + 同样的顺序 + 同样的分组形态」：内容序列一致时，
            // 无论排序参数怎么变，重建都不会带来任何视觉差异。这里用「引用哈希的组合签名」
            // 而不是保存节点引用，避免这份历史把已 ReleaseChildren 的节点钉住（反而制造泄漏）。
            // （UpdateSourcePrebuilt 灌内容时不传排序参数，所以不能把排序状态放进判定条件里。）
            if (_hasLastInput
                && _isGrouped == grouped
                && _lastInputCount == list.Count
                && _lastInputSignature == ComputeSignature(list)
                // 分组目录还必须确认“时间组没变”：节点实例虽然复用，但 ApplyMetadata 会更新
                // LastModifiedTime，文件可能已经该换到别的时间组。分组键不刷新就会显示在旧组里。
                && (!grouped || GroupKeysStillValid(list)))
            {
                // 排序状态仍要同步（哪怕这次不重建），否则后续增量插入会按旧排序插错位置
                if (!wantsSort)
                    ClearActiveSort();
                else if (!_hasActiveSort
                         || !string.Equals(_sortPath, sortPath, StringComparison.Ordinal)
                         || _sortAscending != ascending)
                    SetActiveSort(sortPath!, ascending);

                return false;
            }

            _lastInputSignature = ComputeSignature(list);
            _lastInputCount = list.Count;
            _hasLastInput = true;

            _isGrouped = grouped;
            _groupChildren.Clear();
            _groupHeaders.Clear();

            if (wantsSort)
                SetActiveSort(sortPath!, ascending);
            else
                ClearActiveSort();

            var comparison = wantsSort ? GetComparison(sortPath!, ascending) : null;

            if (!grouped || list.Count == 0)
            {
                Clear();
                if (comparison != null && list.Count > 0)
                    list.Sort(comparison);
                foreach (var item in list)
                    Add(item);
                return true;
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
            return true;
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

    /// <summary>
    /// 后台预构建好的表格内容：**已经按当前排序排好**，分组目录的时间组键也已写回各节点。
    ///
    /// 以前后台会先 new 一个完整的 <see cref="GroupedFileList"/> 并 <c>SetItems</c> 一遍、
    /// 前台拿到后再 <c>SetItems</c> 一遍 —— 等于每次导航干两遍分组与重排。
    /// 现在后台只算“顺序 + 分组键”，构建交给前台那唯一一次 <c>SetItems</c>。
    /// </summary>
    internal sealed record PrebuiltContent(IReadOnlyList<FileSystemNodeViewModel> Items, bool Grouped);
}
