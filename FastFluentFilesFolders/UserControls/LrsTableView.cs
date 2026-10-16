using FastFluentFilesFolders.Helpers;
using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using WinUI.TableView;
using SD = WinUI.TableView.SortDirection;
using VirtualKey = Windows.System.VirtualKey;

namespace FastFluentFilesFolders.UserControls
{
    public class LrsTableView : TableView
    {
        // 整个生命周期只用一个数据源实例：换文件夹时原地替换内容，而不是换 ItemsSource。
        // 换 ItemsSource 会让 ListView 重建整套行/单元格（实测在 400 项文件夹上来回切换
        // 每次导航工作集会涨 30MB 左右，属于 WinUI 原生 XAML 资源未回收），原地更新则可以复用容器。
        private readonly GroupedFileList _groupedSource = new();

        // 当前排序状态（列 + 方向），由标签页状态灌入；用户主动排序时反向通过 SortChanged 通知外部。
        // 之前只有 GroupedFileList 内部字段记着排序，换目录/刷新重新灌内容时不会重新套用，
        // 于是排序被“配置里的默认排序”覆盖 —— 这里把它变成显式状态。
        private string? _sortPath;
        private bool _sortAscending = true;

        public string? SortPath => _sortPath;
        public bool SortAscending => _sortAscending;

        /// <summary>用户主动排序（表头点击 / 工具栏排序菜单）时触发：列为 null 表示取消排序。</summary>
        public event Action<string?, bool>? SortChanged;

        public LrsTableView()
        {
            AllowLiveShaping = false;
            _groupedSource.FlatListChanged += OnFlatListChanged;
            ItemsSource = _groupedSource;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        static LrsTableView()
        {
            // 临时诊断：内容替换入场动画的排查日志（transition.log）；排查完可删掉这两行与 Helpers/TransitionLog.cs
            TableViewTransitionTrace.Sink = Helpers.TransitionLog.Write;

            // 构建指纹：日志第一行就写清“当前跑的是哪份产物”，避免再花时间猜是不是旧构建
            try
            {
                var appAssembly = System.Reflection.Assembly.GetExecutingAssembly();
                var libAssembly = typeof(TableView).Assembly;
                var appStamp = System.IO.File.Exists(appAssembly.Location)
                    ? System.IO.File.GetLastWriteTime(appAssembly.Location).ToString("MM-dd HH:mm") : "?";
                var libStamp = System.IO.File.Exists(libAssembly.Location)
                    ? System.IO.File.GetLastWriteTime(libAssembly.Location).ToString("MM-dd HH:mm") : "?";
                TableViewTransitionTrace.Write(
                    $"---- trace attached (app {appAssembly.GetName().Version} @{appStamp}, " +
                    $"lib {libAssembly.GetName().Version} @{libStamp}) ----");
            }
            catch
            {
                TableViewTransitionTrace.Write("---- trace attached ----");
            }
        }

        private void EnsureStableSourceAttached()
        {
            var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (queue != null)
                _groupedSource.SetDispatcher(queue);
            if (!ReferenceEquals(ItemsSource, _groupedSource))
                ItemsSource = _groupedSource;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var mode = App.SharedViewModel?.AppConfigs?.TransitionMode ?? "Default";
            ApplyTransitionMode(mode);
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.PropertyChanged += OnConfigPropertyChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _rowsScrollViewer = null;
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.PropertyChanged -= OnConfigPropertyChanged;
        }

        private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Configs.TransitionMode)) return;
            var mode = (sender as Configs)?.TransitionMode ?? "Default";
            DispatcherQueue.TryEnqueue(() => ApplyTransitionMode(mode));
        }

        /// <summary>
        /// 切换动画方式（严格按设置来）：
        /// Default = 复刻自带容器过渡的观感（每行“从下方 30px 上滑 + 淡入”，不逐行错开）—— 原地换内容时
        ///           WinUI 自带的 ContentThemeTransition/EntranceThemeTransition 不会播（Reset 与插入被并在一次布局里），
        ///           所以由库按同样的参数自己播一遍；
        /// Stream（溪流）= 每行依次淡入（逐行错开 15ms，封顶 150ms）；
        /// Fade（淡入）= 同样的入场动画，但不上滑（纯淡入）、整批同时开始（不错开）；None = 无动画。
        /// 除 None 外都保留单条增删动画：
        /// AddDeleteThemeTransition → 新增项目淡入；
        /// RepositionThemeTransition → 删除项目后，下方项目自动上移补位。
        /// </summary>
        private void ApplyTransitionMode(string mode)
        {
            switch (mode)
            {
                case "Fade":
                    // 纯淡入：只淡、不上滑，整批同时开始（不错开）。
                    //
                    // 这里以前是“把整个 TableView 的 Opacity 归零，再整表淡入”，于是有两个必然的毛病：
                    // ① 归零发生在内容还没挂进可视树的时候 —— 从点开分组文件夹到新行实体化之间，
                    //    整张表（含列头）都是空的，看起来就是“整表消失”；
                    // ② 分组文件夹那次淡入还被推迟到低优先级回调里才 Begin()（当时是为了躲开“容器实体化晚一帧”），
                    //    结果空窗更长、随后才闪一下淡入。
                    // 现在改成和其它两种模式共用库的入场动画：行在容器实体化时（渲染之前）就被置为透明，
                    // 本帧末由库统一开播 —— 不会空窗，列头也不参与淡入（与默认/溪流一致）。
                    ItemContainerTransitions = CreateIncrementalTransitions();
                    AnimateContentReplacement = true;
                    ContentReplacementAnimationDuration = TimeSpan.FromMilliseconds(350);
                    ContentReplacementStagger = TimeSpan.Zero;
                    ContentReplacementMaxStagger = TimeSpan.Zero;
                    ContentReplacementSlideOffsetY = 0;
                    break;
                case "None":
                    ItemContainerTransitions = new TransitionCollection();
                    AnimateContentReplacement = false;
                    break;
                case "Stream":
                    ItemContainerTransitions = CreateIncrementalTransitions();
                    AnimateContentReplacement = true;
                    ContentReplacementAnimationDuration = TimeSpan.FromMilliseconds(200);
                    ContentReplacementStagger = TimeSpan.FromMilliseconds(15);
                    ContentReplacementMaxStagger = TimeSpan.FromMilliseconds(150);
                    ContentReplacementSlideOffsetY = 0;
                    break;
                default: // "Default"
                    ItemContainerTransitions = CreateDefaultTransitions();
                    AnimateContentReplacement = true;
                    // 复刻自带容器过渡的观感，参数按 WinUI.TableView Sample 实测值收敛：
                    // 淡入 + 从下方 12px 上滑、120ms、EaseOut、整批一起（不逐行错开）
                    ContentReplacementAnimationDuration = TimeSpan.FromMilliseconds(130);
                    ContentReplacementStagger = TimeSpan.Zero;
                    ContentReplacementMaxStagger = TimeSpan.Zero;
                    ContentReplacementSlideOffsetY = 22;
                    break;
            }

            TableViewTransitionTrace.Write(
                $"mode={mode} animateReplacement={AnimateContentReplacement} " +
                $"transitions={ItemContainerTransitions?.Count ?? 0} stagger={ContentReplacementStagger.TotalMilliseconds:0}ms " +
                $"slide={ContentReplacementSlideOffsetY:0}px duration={ContentReplacementAnimationDuration.TotalMilliseconds:0}ms");
        }

        /// <summary>仅保留单条增删/重排动画（不包含入场动画，避免切换文件夹时逐行出现）。</summary>
        private static TransitionCollection CreateIncrementalTransitions() => new()
        {
            new AddDeleteThemeTransition(),
            new RepositionThemeTransition { IsStaggeringEnabled = false },
        };

        /// <summary>
        /// 默认模式的容器过渡：只保留单条增删与重排补位动画。
        /// 整批换内容的入场动画由 TableView 的 AnimateContentReplacement 统一负责 ——
        /// 如果把 ContentThemeTransition / EntranceThemeTransition 也留着，两者会同时播、看起来发抖。
        /// </summary>
        private static TransitionCollection CreateDefaultTransitions() => new()
        {
            new AddDeleteThemeTransition(),
            new ReorderThemeTransition(),
            new RepositionThemeTransition { IsStaggeringEnabled = false },
        };

        public void UpdateSource(ObservableCollection<FileSystemNodeViewModel> items, bool grouped,
            string? sortPath = null, bool ascending = true)
        {
            PrepareForContentReplacement();
            EnsureStableSourceAttached();
            // 告诉库“内容要整体替换了”：库会在本轮布局结束时（渲染之前）统一滚顶并按设置播放入场动画
            BeginContentReplacement();
            // 单遍构建：SetItems 内部直接分组 + 排序，避免再调 SortWithinGroups 造成第二次 RebuildFlat
            _groupedSource.SetItems(items, grouped, sortPath, ascending);
            // 换内容之后立刻再复位一次（此时旧内容的滚动范围还在，ChangeView 会同步生效），
            // 让随后那次布局是从“已经在顶部”开始的；库在本帧末还会再兜一次并统一开播动画。
            ResetScrollPosition(force: false);
            TableViewTransitionTrace.Write(
                $"mount source items={items.Count} grouped={grouped} sort={sortPath ?? "-"}/{(ascending ? "asc" : "desc")}");
            SyncSortState(sortPath, ascending, itemsAlreadySorted: true);
            FinishContentReplacement();
            ScheduleScrollToTop();
        }

        /// <summary>
        /// 使用后台已构建好的 GroupedFileList 整体挂载（UI 线程只做一次 Reset 级切换）。
        /// 后台构建时如果已经套用过同一个排序，这里就不再在 UI 线程重排（性能优先）。
        /// </summary>
        public void UpdateSourcePrebuilt(GroupedFileList source, string? sortPath = null, bool ascending = true)
        {
            PrepareForContentReplacement();
            EnsureStableSourceAttached();
            // 告诉库“内容要整体替换了”：库会在本轮布局结束时（渲染之前）统一滚顶并按设置播放入场动画
            BeginContentReplacement();

            // 只取真实条目，过滤掉 source 里已经生成的分组标题行：
            // 把带标题的扁平列表再交给 SetItems 分组，会为“标题行”再生成一层标题 → 分组文件夹出现两个标签。
            var items = new List<FileSystemNodeViewModel>(source.Count);
            foreach (var item in source)
            {
                if (item is not FileGroupHeader)
                    items.Add(item);
            }

            _groupedSource.SetItems(items, source.IsGrouped);
            // 换内容之后立刻再复位一次（同 UpdateSource 的说明）
            ResetScrollPosition(force: false);
            TableViewTransitionTrace.Write(
                $"mount prebuilt items={items.Count} grouped={source.IsGrouped} sort={sortPath ?? "-"}/{(ascending ? "asc" : "desc")}");
            var normalized = string.IsNullOrEmpty(sortPath) ? null : sortPath;
            var alreadySorted =
                string.Equals(source.ActiveSortPath ?? string.Empty, normalized ?? string.Empty, StringComparison.Ordinal) &&
                (normalized is null || source.ActiveSortAscending == ascending);
            SyncSortState(normalized, ascending, alreadySorted);
            FinishContentReplacement();
            ScheduleScrollToTop();
        }

        /// <summary>
        /// 把外部（标签页）的排序状态灌入表格：更新列头箭头、更新数据源的“活动排序”
        /// （决定后续增量插入的位置），并在需要时对当前内容重排。
        /// </summary>
        /// <param name="itemsAlreadySorted">true = 当前内容已经按该排序排好，不再重复排序（省掉 UI 线程一次整表重排）。</param>
        public void SyncSortState(string? sortPath, bool ascending, bool itemsAlreadySorted)
        {
            _sortPath = string.IsNullOrEmpty(sortPath) ? null : sortPath;
            _sortAscending = ascending;
            ApplySortToColumns();

            if (_sortPath is null)
            {
                _groupedSource.ClearActiveSort();
                return;
            }

            if (itemsAlreadySorted || _groupedSource.Count == 0)
                _groupedSource.SetActiveSort(_sortPath, _sortAscending);
            else
                _groupedSource.SortWithinGroups(_sortPath, _sortAscending);
        }

        /// <summary>让列头箭头与当前排序状态保持一致。</summary>
        private void ApplySortToColumns()
        {
            foreach (var col in Columns)
            {
                col.SortDirection = col.SortMemberPath == _sortPath
                    ? (_sortAscending ? SD.Ascending : SD.Descending)
                    : null;
            }
        }

        /// <summary>
        /// 整体替换表格内容（换文件夹/刷新/搜索）前的复位。
        /// 数据源现在是全生命周期唯一的稳定实例，换文件夹时不会再“换 ItemsSource”，
        /// 于是 ListView 不再自动丢弃选中项、拖拽状态，也不再复位滚动位置：
        /// ① 拖拽锚点按行索引残留 → 双击进入文件夹后，晚到的指针事件拿旧锚点和新内容组合，
        ///    把新文件夹里的一片行选中（表现为“有些项莫名其妙被选中”）；
        /// ② 选中项/当前单元格同样按索引残留 → 落到新内容里不对应的行上，
        ///    TableView 又会把刚选中的行滚进视图，旧滚动偏移也可能被保留
        ///    （表现为“所见位置不在 TableView 顶部”）。
        /// 因此替换前显式复位滚动位置、取消拖拽、清空选中与锚点。
        /// </summary>
        private void PrepareForContentReplacement()
        {
            ResetScrollPosition();
            ResetInteractionStateForContentReplacement();
        }

        /// <summary>
        /// 内容替换完成后的收尾：只做“清选中项”这一件廉价的事（ListView 在原地换内容时
        /// 仍可能把选中项按索引保留下来）。
        /// 注意这里**不能**再调 ForceContainerRebuild()：它会断开再接回 base.ItemsSource，让 ListView
        /// 丢掉整套行容器与面板测量缓存后从零重建，而稳定数据源下 SetItems 刚把内容铺过一遍——
        /// 等于每次进文件夹干两遍活，实测就是“进文件夹卡一下 / 工作集上涨”的来源。
        /// 那个方法是给“真的换 ItemsSource”用的，内容原地替换不需要它。
        /// 必须在内容挂载之后同步执行，且不能用排队回调——“打开文件所在位置”等场景会在挂载后
        /// 立刻主动选中目标项，排队回调会把那次正常的选中一起清掉。
        /// </summary>
        private void FinishContentReplacement()
        {
            if (SelectedItems.Count > 0)
                DeselectAll();
        }

        public void SortBy(string sortPath, bool ascending) => ApplyUserSort(sortPath, ascending);

        /// <summary>
        /// 用户主动排序（表头点击 / 工具栏排序菜单）：立即重排当前内容、更新列头箭头，
        /// 并把结果通知外部（标签页据此记住排序、必要时持久化）。
        /// </summary>
        private void ApplyUserSort(string? sortPath, bool ascending)
        {
            // 用户开始排序时，让挂载时排队的“滚回顶部”回调作废，
            // 否则它会在低优先级回调里打断刚刚开始的排序重排动画
            _contentGeneration++;

            var normalized = string.IsNullOrEmpty(sortPath) ? null : sortPath;

            if (normalized is null)
            {
                // 取消排序：回到默认顺序（ResetSort 清掉活动排序并重建平铺表）
                if (_groupedSource.Count > 0)
                    _groupedSource.ResetSort();
                else
                    _groupedSource.ClearActiveSort();
                _sortPath = null;
                _sortAscending = true;
                ApplySortToColumns();
                SortChanged?.Invoke(null, true);
                return;
            }

            _sortPath = normalized;
            _sortAscending = ascending;
            if (_groupedSource.Count > 0)
                _groupedSource.SortWithinGroups(normalized, ascending);
            else
                _groupedSource.SetActiveSort(normalized, ascending);
            ApplySortToColumns();
            SortChanged?.Invoke(normalized, ascending);
        }


        private void OnFlatListChanged()
        {
            // 数据源现在是稳定实例：RebuildFlat/ToggleGroup 产生的 Clear/Add 事件本身
            // 已经会让 ListView 更新，不再需要把 ItemsSource 断开再挂回来。
        }

        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                var isAltDown = ((int)InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & 1) != 0;
                var isCtrlDown = ((int)InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & 1) != 0;
                var isShiftDown = ((int)InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & 1) != 0;

                // Alt+Enter → properties; plain Enter → open item (handled in MiddleFilesView)
                // Only let base handle Enter for cell navigation when Ctrl/Shift is held
                if (!isCtrlDown && !isShiftDown)
                {
                    return;
                }
            }

            base.OnKeyDown(e);
        }

        protected override void OnSorting(TableViewSortingEventArgs args)
        {
            // 空文件夹也允许排序：这里只是记录状态（列头箭头 + 标签页排序），
            // 下一次进入有内容的文件夹时会按它排好；不能再走 base（base 会去动 SortDescriptions）
            var column = args.Column;
            var sortPath = column.SortMemberPath;
            if (string.IsNullOrEmpty(sortPath))
            {
                base.OnSorting(args);
                return;
            }

            SD? direction = column.SortDirection switch
            {
                null => SD.Ascending,
                SD.Ascending => SD.Descending,
                SD.Descending => null,
                _ => null
            };

            ApplyUserSort(direction is null ? null : sortPath,
                direction == SD.Ascending);

            args.Handled = true;
        }

        /// <summary>
        /// 切换文件夹前把滚动位置复位到顶部。
        /// TableView/ListView 内部有多个 ScrollViewer（表头横向滚动、数据行纵向滚动），只有数据行那个
        /// 才需要复位。为了不给每次导航增加开销：先尝试缓存/命中“行区域那个 ScrollViewer”（即内部
        /// ScrollViewer 里可纵向滚动的那个），命中就只对它做一次 ChangeView；偏移本来就是 0 时直接跳过
        /// （ChangeView 会触发一次布局，无意义地调用就是白白多一帧）。缓存失效时才回退到整树遍历。
        /// </summary>
        /// <param name="force">true = 挂载完成后强制归零（即使当前偏移已是 0），用于内容替换后兜底滚顶。</param>
        public void ResetScrollPosition(bool force = false)
        {
            var target = _rowsScrollViewer;
            if (target is null || (!force && target.ScrollableHeight <= 0 && target.VerticalOffset <= 0))
            {
                _rowsScrollViewer = FindDescendants<ScrollViewer>(this)
                    .FirstOrDefault(sv => sv.ScrollableHeight > 0);
                target = _rowsScrollViewer;
            }

            if (target is not null && (force || target.VerticalOffset > 0.5))
            {
                target.ChangeView(null, 0, null, true);
                return;
            }

            // 兜底：行区域那个还没测量出来（例如首次导航）时，仍然把所有 ScrollViewer 复位一遍，
            // 但只在确实存在非零偏移时才动它们（force 时全部归零）。
            foreach (var sv in FindDescendants<ScrollViewer>(this))
            {
                if (force || sv.VerticalOffset > 0.5)
                    sv.ChangeView(null, 0, null, true);
            }
        }

        private int _contentGeneration;

        /// <summary>
        /// 兜底：正常路径下滚顶由库在“本轮布局结束、渲染之前”完成（见 TableView.FinishContentReplacement），
        /// 这里只是一个保险（例如库那条路径没跑到时）。只在偏移确实不为 0 时才动它（force:false），
        /// 因此不会在内容已经正确停在顶部时再制造一次可见的滚动。
        /// 用 _contentGeneration 作版本号：如果回调执行前用户又立刻排序/切换了内容，
        /// 旧回调自动作废，不再去打断刚发生的排序动画。
        /// </summary>
        private void ScheduleScrollToTop()
        {
            var generation = ++_contentGeneration;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (generation != _contentGeneration) return;
                if (SelectedItems.Count > 0) return;
                ResetScrollPosition(force: false);
            });
        }

        // 数据行所在的 ScrollViewer（可纵向滚动的那个）缓存：避免每次导航都遍历整棵可视树。
        private ScrollViewer? _rowsScrollViewer;

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) yield return t;
                foreach (var sub in FindDescendants<T>(child))
                    yield return sub;
            }
        }
    }
}
