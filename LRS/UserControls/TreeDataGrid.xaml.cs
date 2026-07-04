using LRS.ViewModels;
using LRS;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Windows.System;
using Windows.Foundation;

namespace LRS.UserControls
{
    public sealed partial class TreeDataGrid : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(object), typeof(TreeDataGrid),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public static readonly DependencyProperty NameColumnWidthProperty =
            DependencyProperty.Register(nameof(NameColumnWidth), typeof(GridLength), typeof(TreeDataGrid),
                new PropertyMetadata(new GridLength(4, GridUnitType.Star), OnColumnWidthChanged));

        public static readonly DependencyProperty ModifiedColumnWidthProperty =
            DependencyProperty.Register(nameof(ModifiedColumnWidth), typeof(GridLength), typeof(TreeDataGrid),
                new PropertyMetadata(new GridLength(2, GridUnitType.Star), OnColumnWidthChanged));

        public static readonly DependencyProperty CreatedColumnWidthProperty =
            DependencyProperty.Register(nameof(CreatedColumnWidth), typeof(GridLength), typeof(TreeDataGrid),
                new PropertyMetadata(new GridLength(2, GridUnitType.Star), OnColumnWidthChanged));

        public static readonly DependencyProperty SizeColumnWidthProperty =
            DependencyProperty.Register(nameof(SizeColumnWidth), typeof(GridLength), typeof(TreeDataGrid),
                new PropertyMetadata(new GridLength(1, GridUnitType.Star), OnColumnWidthChanged));

        public static readonly DependencyProperty ParentRowHeightProperty =
            DependencyProperty.Register(nameof(ParentRowHeight), typeof(double), typeof(TreeDataGrid),
                new PropertyMetadata(36.0));

        public static readonly DependencyProperty NavigateCommandProperty =
            DependencyProperty.Register(nameof(NavigateCommand), typeof(ICommand), typeof(TreeDataGrid),
                new PropertyMetadata(null));

        public static readonly DependencyProperty IsSpecialFolderProperty =
            DependencyProperty.Register(nameof(IsSpecialFolder), typeof(bool), typeof(TreeDataGrid),
                new PropertyMetadata(false, OnSpecialFolderChanged));

        public static readonly DependencyProperty ContextFlyoutProperty =
            DependencyProperty.Register(nameof(ContextFlyout), typeof(FlyoutBase), typeof(TreeDataGrid),
                new PropertyMetadata(null));

        public static readonly DependencyProperty BaseContextFlyoutProperty =
            DependencyProperty.Register(nameof(BaseContextFlyout), typeof(FlyoutBase), typeof(TreeDataGrid),
                new PropertyMetadata(null, OnBaseContextFlyoutChanged));

        public object? ItemsSource
        {
            get => GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public GridLength NameColumnWidth
        {
            get => (GridLength)GetValue(NameColumnWidthProperty);
            set => SetValue(NameColumnWidthProperty, value);
        }

        public GridLength ModifiedColumnWidth
        {
            get => (GridLength)GetValue(ModifiedColumnWidthProperty);
            set => SetValue(ModifiedColumnWidthProperty, value);
        }

        public GridLength CreatedColumnWidth
        {
            get => (GridLength)GetValue(CreatedColumnWidthProperty);
            set => SetValue(CreatedColumnWidthProperty, value);
        }

        public GridLength SizeColumnWidth
        {
            get => (GridLength)GetValue(SizeColumnWidthProperty);
            set => SetValue(SizeColumnWidthProperty, value);
        }

        public double ParentRowHeight
        {
            get => (double)GetValue(ParentRowHeightProperty);
            set => SetValue(ParentRowHeightProperty, value);
        }

        public ICommand? NavigateCommand
        {
            get => (ICommand?)GetValue(NavigateCommandProperty);
            set => SetValue(NavigateCommandProperty, value);
        }

        public bool IsSpecialFolder
        {
            get => (bool)GetValue(IsSpecialFolderProperty);
            set => SetValue(IsSpecialFolderProperty, value);
        }

        public FlyoutBase? ContextFlyout
        {
            get => (FlyoutBase?)GetValue(ContextFlyoutProperty);
            set => SetValue(ContextFlyoutProperty, value);
        }

        public FlyoutBase? BaseContextFlyout
        {
            get => (FlyoutBase?)GetValue(BaseContextFlyoutProperty);
            set => SetValue(BaseContextFlyoutProperty, value);
        }

        private static void OnBaseContextFlyoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (TreeDataGrid)d;
            ctrl.FileListView.ContextFlyout = (FlyoutBase?)e.NewValue;
        }

        public event EventHandler<FileSystemNodeViewModel>? ItemInvoked;
        public event EventHandler<FileSystemNodeViewModel>? ItemRightTapped;

        public FileSystemNodeViewModel? SelectedItem { get; set; }
        public Windows.Foundation.Point LastRightTappedPoint { get; private set; }

        private ObservableCollection<TimeGroup> _groupedItems = new();
        private int _draggingColumn = -1;
        private double _dragStartX;
        private double _dragStartWidth;
        private bool _inDragWidthSync;
        private INotifyCollectionChanged? _observedCollection;
        private bool _rebuildPending;
        private int _rebuildGeneration;
        private readonly Dictionary<string, bool> _collapsedGroups = new();

        public enum SortMode { None, NameAsc, NameDesc, ModifiedAsc, ModifiedDesc, CreatedAsc, CreatedDesc, SizeAsc, SizeDesc }
        private SortMode _currentSort = SortMode.NameAsc;

        private ScrollViewer? _listViewScrollViewer;

        public TreeDataGrid()
        {
            InitializeComponent();
            FileListView.ContainerContentChanging += OnContainerContentChanging;
            this.Loaded += (_, _) =>
            {
                if (App.SharedViewModel != null)
                    App.SharedViewModel.RenameFocusRequested += OnRenameFocusRequested;
                _listViewScrollViewer = FindListViewScrollViewer();
                AttachHeaderClip();
                CalculateAndApplyColumnWidths();
            };
            this.SizeChanged += (_, _) =>
            {
                if (_draggingColumn < 0)
                {
                    CalculateAndApplyColumnWidths();
                    RefreshHeaderClip();
                }
            };
            this.Unloaded += (_, _) =>
            {
                if (App.SharedViewModel != null)
                    App.SharedViewModel.RenameFocusRequested -= OnRenameFocusRequested;
            };
        }

        private double GetContentAreaWidth()
        {
            if (_listViewScrollViewer != null &&
                _listViewScrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            {
                return _listViewScrollViewer.ViewportWidth;
            }
            return HeaderGrid.ActualWidth;
        }

        private void AttachHeaderClip()
        {
            var clip = new RectangleGeometry();
            HeaderGrid.Clip = clip;
            SizeChanged += (s, e) =>
            {
                clip.Rect = new Rect(0, 0, GetContentAreaWidth(), HeaderGrid.ActualHeight);
            };
            clip.Rect = new Rect(0, 0, GetContentAreaWidth(), HeaderGrid.ActualHeight);
        }

        private ScrollViewer? FindListViewScrollViewer()
        {
            return FindVisualChild<ScrollViewer>(FileListView);
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }
            return null;
        }

        private void OnRenameFocusRequested(FileSystemNodeViewModel item)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                var container = FileListView.ContainerFromItem(item);
                if (container is ListViewItem lvi && lvi.ContentTemplateRoot is Grid grid)
                {
                    var textBox = grid.Children.OfType<TextBox>()
                        .FirstOrDefault(tb => Grid.GetColumn(tb) == 1);
                    if (textBox != null)
                    {
                        textBox.Focus(FocusState.Programmatic);
                        textBox.SelectAll();
                    }
                }
            });
        }

        private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer.ContentTemplateRoot is Grid grid && grid.ColumnDefinitions.Count >= 8)
            {
                grid.ColumnDefinitions[1].Width = NameColDef.Width;
                grid.ColumnDefinitions[3].Width = ModifiedColDef.Width;
                grid.ColumnDefinitions[5].Width = CreatedColDef.Width;
                grid.ColumnDefinitions[7].Width = SizeColDef.Width;
            }
            if (ContextFlyout != null && args.ItemContainer is ListViewItem container)
            {
                container.ContextFlyout = ContextFlyout;
            }
        }

        private static void OnColumnWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (TreeDataGrid)d;
            if (ctrl._inDragWidthSync) return;
            ctrl.CalculateAndApplyColumnWidths();
            ctrl.FileListView.InvalidateMeasure();
        }

        private void CalculateAndApplyColumnWidths()
        {
            var contentWidth = GetContentAreaWidth();
            if (contentWidth <= 0) return;
            var widths = CalculateColumnWidths(contentWidth);
            ApplyColumnWidths(widths.Name, widths.Modified, widths.Created, widths.Size);
        }

        private (double Name, double Modified, double Created, double Size) CalculateColumnWidths(double availableWidth)
        {
            const double iconCol = 32;
            const double spacerCol = 6;
            const double fixedNonData = iconCol + 3 * spacerCol;

            var specs = new[]
            {
                (NameColumnWidth,  4.0),  // (GridLength, star value for fallback)
                (ModifiedColumnWidth, 2.0),
                (CreatedColumnWidth, 2.0),
                (SizeColumnWidth, 1.0)
            };

            double fixedPixelSum = fixedNonData;
            double totalStarWeight = 0;

            foreach (var (dp, starVal) in specs)
            {
                if (dp.IsAbsolute)
                    fixedPixelSum += dp.Value;
                else if (dp.IsStar)
                    totalStarWeight += dp.Value;
            }

            var starBudget = Math.Max(0, availableWidth - fixedPixelSum);

            double ToPixel(GridLength dp, double starFallback)
            {
                if (dp.IsAbsolute) return dp.Value;
                if (dp.IsStar && totalStarWeight > 0)
                    return Math.Max(0, (dp.Value / totalStarWeight) * starBudget);
                return 0;
            }

            return (
                ToPixel(specs[0].Item1, specs[0].Item2),
                ToPixel(specs[1].Item1, specs[1].Item2),
                ToPixel(specs[2].Item1, specs[2].Item2),
                ToPixel(specs[3].Item1, specs[3].Item2)
            );
        }

        private void ApplyColumnWidths(double namePx, double modifiedPx, double createdPx, double sizePx)
        {
            NameColDef.Width = new GridLength(namePx);
            ModifiedColDef.Width = new GridLength(modifiedPx);
            CreatedColDef.Width = new GridLength(createdPx);
            SizeColDef.Width = new GridLength(sizePx);

            SyncItemsFromHeader();
        }

        private void RefreshHeaderClip()
        {
            if (HeaderGrid.Clip is RectangleGeometry clip)
            {
                clip.Rect = new Rect(0, 0, GetContentAreaWidth(), HeaderGrid.ActualHeight);
            }
        }

        private void SyncItemsFromHeader()
        {
            var nw = NameColDef.Width;
            var mw = ModifiedColDef.Width;
            var cw = CreatedColDef.Width;
            var sw = SizeColDef.Width;

            var itemsCount = FileListView.Items.Count;
            for (int i = 0; i < itemsCount; i++)
            {
                var container = FileListView.ContainerFromIndex(i);
                if (container is ListViewItem item && item.ContentTemplateRoot is Grid grid && grid.ColumnDefinitions.Count >= 8)
                {
                    grid.ColumnDefinitions[1].Width = nw;
                    grid.ColumnDefinitions[3].Width = mw;
                    grid.ColumnDefinitions[5].Width = cw;
                    grid.ColumnDefinitions[7].Width = sw;
                }
            }
        }

        private void SyncHeaderColumnWidths()
        {
            NameColDef.Width = NameColumnWidth;
            ModifiedColDef.Width = ModifiedColumnWidth;
            CreatedColDef.Width = CreatedColumnWidth;
            SizeColDef.Width = SizeColumnWidth;
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (TreeDataGrid)d;
            ctrl.ObserveSourceCollection();
            ctrl.RebuildGroups();
        }

        private static void OnSpecialFolderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (TreeDataGrid)d;
            ctrl._currentSort = (bool)e.NewValue
                ? SortMode.NameAsc
                : ParseSortMode(App.SharedViewModel?.AppConfigs?.DefaultOrderMode);
            ctrl.UpdateSortIndicators();
            ctrl.RebuildGroups();
        }

        public static SortMode ParseSortMode(string? mode)
        {
            if (Enum.TryParse<SortMode>(mode, out var result))
                return result;
            return SortMode.ModifiedDesc;
        }

        private void ObserveSourceCollection()
        {
            if (_observedCollection != null)
                _observedCollection.CollectionChanged -= OnSourceCollectionChanged;

            _observedCollection = ItemsSource as INotifyCollectionChanged;

            if (_observedCollection != null)
                _observedCollection.CollectionChanged += OnSourceCollectionChanged;
        }

        private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_rebuildPending) return;
            _rebuildPending = true;
            var gen = _rebuildGeneration;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _rebuildPending = false;
                if (gen != _rebuildGeneration) return;
                RebuildGroups();
            });
        }

        private void RebuildGroups()
        {
            if (ItemsSource is not IEnumerable<FileSystemNodeViewModel> source)
            {
                GroupedSource.Source = null;
                return;
            }

            var items = source.ToList();
            var ordered = ApplySort(items).ToList();

            if (IsSpecialFolder)
            {
                var grouped = ordered
                    .GroupBy(f => TimeGroupConverter.GetTimeGroup(f.LastModifiedTime))
                    .OrderBy(g => TimeGroupSortConverter.GetSortOrder(g.Key))
                    .ThenBy(g => g.Key)
                    .Select(g =>
                    {
                        bool isCollapsed = _collapsedGroups.TryGetValue(g.Key, out var c) && c;
                        var groupItems = isCollapsed
                            ? new ObservableCollection<FileSystemNodeViewModel>()
                            : new ObservableCollection<FileSystemNodeViewModel>(g);
                        return new TimeGroup(g.Key, groupItems, () =>
                        {
                            _collapsedGroups[g.Key] = !isCollapsed;
                            RebuildGroups();
                        })
                        { IsExpanded = !isCollapsed };
                    })
                    .ToList();

                _groupedItems = new ObservableCollection<TimeGroup>(grouped);
                GroupedSource.IsSourceGrouped = true;
                GroupedSource.Source = _groupedItems;
            }
            else
            {
                GroupedSource.IsSourceGrouped = false;
                GroupedSource.Source = new ObservableCollection<FileSystemNodeViewModel>(ordered);
            }

            _rebuildGeneration++;
        }

        private IEnumerable<FileSystemNodeViewModel> ApplySort(List<FileSystemNodeViewModel> items)
        {
            return _currentSort switch
            {
                SortMode.None => items,
                SortMode.NameAsc => items.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
                SortMode.NameDesc => items.OrderByDescending(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
                SortMode.ModifiedAsc => items.OrderBy(f => f.LastModifiedTime),
                SortMode.ModifiedDesc => items.OrderByDescending(f => f.LastModifiedTime),
                SortMode.CreatedAsc => items.OrderBy(f => f.FirstCreatedTime),
                SortMode.CreatedDesc => items.OrderByDescending(f => f.FirstCreatedTime),
                SortMode.SizeAsc => items.OrderBy(f => f.ExactSize),
                SortMode.SizeDesc => items.OrderByDescending(f => f.ExactSize),
                _ => items
            };
        }

        private void OnSortByName(object sender, RoutedEventArgs e)
        {
            _currentSort = _currentSort == SortMode.NameAsc ? SortMode.NameDesc : SortMode.NameAsc;
            RebuildGroups();
            UpdateSortIndicators();
        }

        private void OnSortByModified(object sender, RoutedEventArgs e)
        {
            _currentSort = _currentSort == SortMode.ModifiedDesc ? SortMode.ModifiedAsc : SortMode.ModifiedDesc;
            RebuildGroups();
            UpdateSortIndicators();
        }

        private void OnSortByCreated(object sender, RoutedEventArgs e)
        {
            _currentSort = _currentSort == SortMode.CreatedDesc ? SortMode.CreatedAsc : SortMode.CreatedDesc;
            RebuildGroups();
            UpdateSortIndicators();
        }

        private void OnSortBySize(object sender, RoutedEventArgs e)
        {
            _currentSort = _currentSort == SortMode.SizeAsc ? SortMode.SizeDesc : SortMode.SizeAsc;
            RebuildGroups();
            UpdateSortIndicators();
        }

        private void UpdateSortIndicators()
        {
            NameSortIndicator.Text = _currentSort switch
            {
                SortMode.NameAsc => "\u25B2",
                SortMode.NameDesc => "\u25BC",
                _ => ""
            };
            ModifiedSortIndicator.Text = _currentSort switch
            {
                SortMode.ModifiedAsc => "\u25B2",
                SortMode.ModifiedDesc => "\u25BC",
                _ => ""
            };
            CreatedSortIndicator.Text = _currentSort switch
            {
                SortMode.CreatedAsc => "\u25B2",
                SortMode.CreatedDesc => "\u25BC",
                _ => ""
            };
            SizeSortIndicator.Text = _currentSort switch
            {
                SortMode.SizeAsc => "\u25B2",
                SortMode.SizeDesc => "\u25BC",
                _ => ""
            };
        }

        private void OnColumnDragStarted(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement handle) return;
            var tag = handle.Name;

            int colIdx = tag switch
            {
                "NameDragHandle" => 1,
                "ModifiedDragHandle" => 3,
                "CreatedDragHandle" => 5,
                _ => -1
            };

            if (colIdx < 0) return;

            _draggingColumn = colIdx;
            _dragStartX = e.GetCurrentPoint(this).Position.X;
            _dragStartWidth = GetColumnWidth(colIdx);
            handle.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnColumnDragDelta(object sender, PointerRoutedEventArgs e)
        {
            if (_draggingColumn < 0) return;

            var delta = e.GetCurrentPoint(this).Position.X - _dragStartX;
            var newWidth = Math.Max(40, _dragStartWidth + delta);

            var contentWidth = GetContentAreaWidth();
            if (contentWidth > 0)
            {
                const double fixedSpacers = 32 + 3 * 6;
                double otherPixelColumns = 0;

                if (_draggingColumn != 1 && NameColumnWidth.IsAbsolute)
                    otherPixelColumns += NameColumnWidth.Value;
                if (_draggingColumn != 3 && ModifiedColumnWidth.IsAbsolute)
                    otherPixelColumns += ModifiedColumnWidth.Value;
                if (_draggingColumn != 5 && CreatedColumnWidth.IsAbsolute)
                    otherPixelColumns += CreatedColumnWidth.Value;
                if (_draggingColumn != 7 && SizeColumnWidth.IsAbsolute)
                    otherPixelColumns += SizeColumnWidth.Value;

                var maxForDragged = contentWidth - fixedSpacers - otherPixelColumns;
                newWidth = Math.Min(newWidth, Math.Max(40, maxForDragged));
            }

            _inDragWidthSync = true;
            SetColumnGridLength(_draggingColumn, new GridLength(newWidth));
            _inDragWidthSync = false;

            CalculateAndApplyColumnWidths();
            RefreshHeaderClip();
            e.Handled = true;
        }

        private void OnColumnDragCompleted(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement handle)
                handle.ReleasePointerCapture(e.Pointer);
            _draggingColumn = -1;

            RefreshHeaderClip();
            FileListView.InvalidateMeasure();
        }

        private double GetColumnWidth(int idx)
        {
            var colDef = GetHeaderColumnDefinition(idx);
            if (colDef != null && colDef.ActualWidth > 0)
                return colDef.ActualWidth;

            return idx switch
            {
                1 => NameColumnWidth.IsStar ? 200 : NameColumnWidth.Value,
                3 => ModifiedColumnWidth.IsStar ? 160 : ModifiedColumnWidth.Value,
                5 => CreatedColumnWidth.IsStar ? 160 : CreatedColumnWidth.Value,
                _ => 100
            };
        }

        private ColumnDefinition? GetHeaderColumnDefinition(int idx)
        {
            return idx switch
            {
                1 => NameColDef,
                3 => ModifiedColDef,
                5 => CreatedColDef,
                7 => SizeColDef,
                _ => null
            };
        }

        private void SetColumnGridLength(int idx, GridLength width)
        {
            switch (idx)
            {
                case 1: NameColumnWidth = width; break;
                case 3: ModifiedColumnWidth = width; break;
                case 5: CreatedColumnWidth = width; break;
            }
        }

        private void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement element && element.DataContext is FileSystemNodeViewModel item)
            {
                ItemInvoked?.Invoke(this, item);
                NavigateCommand?.Execute(item);
            }
        }

        private void OnItemRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement element && element.DataContext is FileSystemNodeViewModel item)
            {
                SelectedItem = item;
                FileListView.SelectedItem = item;
                LastRightTappedPoint = e.GetPosition(this);
                ItemRightTapped?.Invoke(this, item);
            }
        }

        private void OnRenameTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.Text.Length > 0)
                textBox.SelectAll();
        }

        private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is FileSystemNodeViewModel item && item.IsRenaming)
            {
                var newName = textBox.Text.Trim();
                item.IsRenaming = false;
                if (!string.IsNullOrEmpty(newName) && newName != item.Name)
                {
                    item.Name = newName;
                    _ = App.SharedViewModel.CommitRenameAsync(item, newName);
                    _ = item.RefreshAsync();
                }
            }
        }

        private void OnRenameTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is FileSystemNodeViewModel item && item.IsRenaming)
            {
                if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    e.Handled = true;
                    var newName = textBox.Text.Trim();
                    item.IsRenaming = false;
                    if (!string.IsNullOrEmpty(newName) && newName != item.Name)
                    {
                        item.Name = newName;
                        _ = App.SharedViewModel.CommitRenameAsync(item, newName);
                        _ = item.RefreshAsync();
                    }
                }
                else if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    e.Handled = true;
                    item.IsRenaming = false;
                }
            }
        }
    }

    public sealed class ResizeHandleGrid : Grid
    {
        public ResizeHandleGrid()
        {
            PointerEntered += OnPointerEntered;
            PointerExited += OnPointerExited;
        }

        private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        }

        private void OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = null;
        }
    }

    public class TimeGroup : INotifyPropertyChanged
    {
        public string Key { get; }
        public ObservableCollection<FileSystemNodeViewModel> Items { get; }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandGlyph)));
            }
        }

        public string ExpandGlyph => IsExpanded ? "\uE96E" : "\uE970";

        private readonly Action? _onToggled;

        public TimeGroup(string key, ObservableCollection<FileSystemNodeViewModel> items, Action? onToggled = null)
        {
            Key = key;
            Items = items;
            _onToggled = onToggled;
        }

        public void ToggleIsExpanded()
        {
            IsExpanded = !IsExpanded;
            _onToggled?.Invoke();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => $"{Key} ({Items.Count})";
    }

    public class TimeGroupConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTime dt)
                return GetTimeGroup(dt);
            return "未知";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }

        public static string GetTimeGroup(DateTime dateTime)
        {
            var now = DateTime.Now;
            var local = dateTime.Kind == DateTimeKind.Utc ? dateTime.ToLocalTime() : dateTime;
            var today = now.Date;

            if (local.Date == today)
                return "今天";

            if (local.Date == today.AddDays(-1))
                return "昨天";

            var diffDays = (today - local.Date).Days;
            if (diffDays < 7 && local.DayOfWeek < today.DayOfWeek)
                return "本周早些时候";

            if (diffDays < 14)
                return "上周";

            if (local.Year == now.Year && local.Month == now.Month)
                return "本月早些时候";

            if (new DateTime(now.Year, now.Month, 1).AddMonths(-1) == new DateTime(local.Year, local.Month, 1))
                return "上个月";

            if (local.Year == now.Year)
                return "今年早些时候";

            if (local.Year == now.Year - 1)
                return "去年";

            return "很久以前";
        }
    }

    public class TimeGroupSortConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string s)
                return GetSortOrder(s);
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }

        public static int GetSortOrder(string groupName)
        {
            return groupName switch
            {
                "今天" => 0,
                "昨天" => 1,
                "本周早些时候" => 2,
                "上周" => 3,
                "本月早些时候" => 4,
                "上个月" => 5,
                "今年早些时候" => 6,
                "去年" => 7,
                "很久以前" => 8,
                _ => 9
            };
        }
    }
}
