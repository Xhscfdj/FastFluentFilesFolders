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

        public event Action? FlatListChanged;

        public void SetDispatcher(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void SetItems(IEnumerable<FileSystemNodeViewModel> items, bool grouped)
        {
            _groupChildren.Clear();
            _groupHeaders.Clear();

            if (!grouped || !items.Any())
            {
                Clear();
                foreach (var item in items)
                    Add(item);
                return;
            }

            foreach (var item in items)
            {
                var key = string.IsNullOrEmpty(item.SortByTime)
                    ? GetTimeGroup(item.LastModifiedTime)
                    : item.SortByTime;
                if (!string.IsNullOrEmpty(key))
                    item.SortByTime = key;
                if (!_groupChildren.ContainsKey(key))
                    _groupChildren[key] = new List<FileSystemNodeViewModel>();
                _groupChildren[key].Add(item);
            }

            RebuildFlat();
        }

        public void AddItem(FileSystemNodeViewModel item)
        {
            var key = string.IsNullOrEmpty(item.SortByTime)
                ? GetTimeGroup(item.LastModifiedTime)
                : item.SortByTime;
            if (!string.IsNullOrEmpty(key))
                item.SortByTime = key;
            if (!_groupChildren.ContainsKey(key))
                _groupChildren[key] = new List<FileSystemNodeViewModel>();
            _groupChildren[key].Add(item);

            if (_isBatchUpdating) return;

            var header = GetOrCreateHeader(key, _groupChildren[key]);
            if (!_groupHeaders.ContainsKey(key))
            {
                _groupHeaders[key] = header;
                int headerIdx = GetHeaderInsertIndex(key);
                Insert(headerIdx, header);
                if (header.IsGroupExpanded)
                    Insert(headerIdx + 1 + _groupChildren[key].Count - 1, item);
            }
            else if (header.IsGroupExpanded)
            {
                int headerIdx = IndexOf(header);
                int insertIdx = headerIdx + _groupChildren[key].Count;
                Insert(insertIdx, item);
            }
        }

        public void RemoveItem(FileSystemNodeViewModel item)
        {
            var key = item.SortByTime;
            if (string.IsNullOrEmpty(key) || !_groupChildren.TryGetValue(key, out var list))
                return;
            list.Remove(item);

            if (_isBatchUpdating) return;

            int idx = IndexOf(item);
            if (idx >= 0)
                RemoveAt(idx);

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
            if (_groupHeaders.Count == 0) return;
            var allItems = _groupChildren.Values.SelectMany(c => c).ToList();
            SetItems(allItems, true);
        }

        public void SortWithinGroups(string sortPath, bool ascending)
        {
            foreach (var (key, children) in _groupChildren)
            {
                var sorted = ascending
                    ? SortByPath(children, sortPath).ToList()
                    : SortByPathDescending(children, sortPath).ToList();
                _groupChildren[key] = sorted;
            }

            RebuildFlat();
        }

        private static IEnumerable<FileSystemNodeViewModel> SortByPath(List<FileSystemNodeViewModel> items, string path)
        {
            return path switch
            {
                "Name" => items.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
                "LastModifiedTime" => items.OrderBy(f => f.LastModifiedTime),
                "FirstCreatedTime" => items.OrderBy(f => f.FirstCreatedTime),
                "ExactSize" => items.OrderBy(f => f.ExactSize),
                _ => items
            };
        }

        private static IEnumerable<FileSystemNodeViewModel> SortByPathDescending(List<FileSystemNodeViewModel> items, string path)
        {
            return path switch
            {
                "Name" => items.OrderByDescending(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
                "LastModifiedTime" => items.OrderByDescending(f => f.LastModifiedTime),
                "FirstCreatedTime" => items.OrderByDescending(f => f.FirstCreatedTime),
                "ExactSize" => items.OrderByDescending(f => f.ExactSize),
                _ => items
            };
        }

        public static string GetTimeGroup(DateTime dateTime)
        {
            var now = DateTime.Now;
            var local = dateTime.Kind == DateTimeKind.Utc ? dateTime.ToLocalTime() : dateTime;
            var today = now.Date;

            if (local.Date == today) return "今天";
            if (local.Date == today.AddDays(-1)) return "昨天";
            var diffDays = (today - local.Date).Days;
            if (diffDays < 7 && local.DayOfWeek < today.DayOfWeek) return "本周早些时候";
            if (diffDays < 14) return "上周";
            if (local.Year == now.Year && local.Month == now.Month) return "本月早些时候";
            if (new DateTime(now.Year, now.Month, 1).AddMonths(-1) == new DateTime(local.Year, local.Month, 1)) return "上个月";
            if (local.Year == now.Year) return "今年早些时候";
            if (local.Year == now.Year - 1) return "去年";
            return "很久以前";
        }
    }

    public static class TimeGroupSortConverter
    {
        public static int GetSortOrder(string groupName) => groupName switch
        {
            "今天" => 0, "昨天" => 1, "本周早些时候" => 2, "上周" => 3,
            "本月早些时候" => 4, "上个月" => 5, "今年早些时候" => 6,
            "去年" => 7, "很久以前" => 8, _ => 9
        };
    }
}
