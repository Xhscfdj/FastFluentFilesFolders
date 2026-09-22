using FastFluentFilesFolders.Helpers;
using FastFluentFilesFolders.ViewModels;
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
        private GroupedFileList? _groupedSource;
        private Storyboard? _fadeStoryboard;
        private bool _fadeEnabled;

        public LrsTableView()
        {
            AllowLiveShaping = false;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
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
        /// 切换动画方式：
        /// Default = 控件默认容器过渡；Fade = 关闭逐行滚动过渡 + 整表淡入；None = 无动画。
        /// </summary>
        private void ApplyTransitionMode(string mode)
        {
            switch (mode)
            {
                case "Fade":
                    ItemContainerTransitions = new TransitionCollection();
                    _fadeEnabled = true;
                    break;
                case "None":
                    ItemContainerTransitions = new TransitionCollection();
                    _fadeEnabled = false;
                    break;
                default: // "Default"
                    ItemContainerTransitions = null; // 清除本地值，回到控件默认过渡
                    _fadeEnabled = false;
                    break;
            }
        }

        private void FadeInContent()
        {
            _fadeStoryboard?.Stop();
            var sb = new Storyboard();
            var da = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(da, this);
            Storyboard.SetTargetProperty(da, "Opacity");
            sb.Children.Add(da);
            sb.Completed += (_, _) => Opacity = 1;
            _fadeStoryboard = sb;
            Opacity = 0;
            sb.Begin();
        }

        public void UpdateSource(ObservableCollection<FileSystemNodeViewModel> items, bool grouped)
        {
            // 每次切换文件夹都新建一个 GroupedFileList，并先填充完成再整体挂到 ItemsSource。
            // 关键点：绝不在一个仍被 TableView 订阅的旧源上逐条 Clear/Add —— 那样会产生
            // “新项逐个替换旧项（旧项来自上一个文件夹）”的滚动替换感。新建源 + 一次性挂载
            // 让 TableView 只看到一次整体切换。
            if (_groupedSource != null)
                _groupedSource.FlatListChanged -= OnFlatListChanged;

            // 切换文件夹前先复位滚动位置，避免 TableView 保留旧偏移造成“新项滚动覆盖老项”。
            ResetScrollPosition();

            var source = new GroupedFileList();
            source.FlatListChanged += OnFlatListChanged;
            source.SetItems(items, grouped);

            _groupedSource = source;
            ItemsSource = source;
            if (_fadeEnabled && source.Count > 0) FadeInContent();
        }

        /// <summary>
        /// 使用后台已构建好的 GroupedFileList 整体挂载（UI 线程只做一次 Reset 级切换）。
        /// </summary>
        public void UpdateSourcePrebuilt(GroupedFileList source)
        {
            if (_groupedSource != null)
                _groupedSource.FlatListChanged -= OnFlatListChanged;

            ResetScrollPosition();

            source.FlatListChanged += OnFlatListChanged;
            _groupedSource = source;
            ItemsSource = source;
            if (_fadeEnabled) FadeInContent();
        }

        public void SortBy(string sortPath, bool ascending)
        {
            if (_groupedSource != null && _groupedSource.Count > 0)
            {
                _groupedSource.SortWithinGroups(sortPath, ascending);
                var s = ItemsSource;
                ItemsSource = null;
                ItemsSource = s;
            }

            foreach (var col in Columns)
            {
                if (col.SortMemberPath == sortPath)
                {
                    col.SortDirection = ascending ? SD.Ascending : SD.Descending;
                }
                else
                {
                    col.SortDirection = null;
                }
            }
        }

        private void OnFlatListChanged()
        {
            var source = ItemsSource;
            ItemsSource = null;
            ItemsSource = source;
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
            if (_groupedSource == null || _groupedSource.Count == 0)
            {
                base.OnSorting(args);
                return;
            }

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

            if (direction is not null)
            {
                _groupedSource.SortWithinGroups(sortPath, direction == SD.Ascending);
                column.SortDirection = direction;
            }
            else
            {
                _groupedSource.ResetSort();
                column.SortDirection = null;
            }

            var s = ItemsSource;
            ItemsSource = null;
            ItemsSource = s;
            args.Handled = true;
        }

        /// <summary>
        /// 切换文件夹前把滚动位置复位到顶部。TableView/ListView 内部通常有多个 ScrollViewer
        /// （表头横向滚动、数据行纵向滚动），只找到第一个可能命中表头那个，导致旧表纵向偏移被保留、
        /// 新表顺着旧偏移“滚动出现”。这里遍历复位所有 ScrollViewer（含真正滚动数据行的那个）。
        /// </summary>
        public void ResetScrollPosition()
        {
            foreach (var sv in FindDescendants<ScrollViewer>(this))
                sv.ChangeView(null, 0, null, true);
        }

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
