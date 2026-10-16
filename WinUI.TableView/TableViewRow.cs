using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using WinUI.TableView.Extensions;
using WinUI.TableView.Helpers;

namespace WinUI.TableView;

/// <summary>
/// Represents a row in a TableView.
/// </summary>

#if WINDOWS
[WinRT.GeneratedBindableCustomProperty]
#endif
public partial class TableViewRow : ListViewItem
{
    private const string Selection_Background = "SelectionBackground";
    private const double Selection_IndicatorHeight = 16d;
    private const string Check_Mark = "\uE73E";
    private Thickness _focusVisualMargin = new(1);
    private readonly Thickness _selectionBackgroundMargin = new(4, 2, 4, 2);
    private readonly Thickness _selectionIndicatorMargin = new(4, 0, 0, 0);
    private ListViewItemPresenter? _itemPresenter;
    private Border? _selectionBackground;
    private Border? _selectionIndicator;
    private bool _ensureCells = true;
    private Brush? _cellPresenterBackground;
    private Brush? _cellPresenterForeground;
    private Storyboard? _entranceAnimationStoryboard;
    private int _entranceAnimationToken = -1;
    private bool _entranceAnimationStarted;

    /// <summary>
    /// Prepares the entrance animation for this row: pins <see cref="UIElement.Opacity"/> to 0 synchronously and
    /// builds the storyboard (fade + optional slide, with <paramref name="delay"/> as its <c>BeginTime</c>), but
    /// does <b>not</b> start it. <see cref="TableView"/> starts the whole batch at the end of the layout pass that
    /// built it (<see cref="StartPreparedEntranceAnimation"/>).
    /// <para>
    /// Starting per row as each one is realized is what made the animation look like it "stuttered several times":
    /// the panel builds the rows one at a time (~20-30ms each, cell templates dominate), so a per-row start turns
    /// one entry animation into a dozen staggered micro-fades.
    /// </para>
    /// Returns false when this row already belongs to the given batch.
    /// </summary>
    internal bool PrepareEntranceAnimation(TimeSpan duration, int token, TimeSpan delay, double slideOffsetY)
    {
        if (_entranceAnimationToken == token) return false;

        StopEntranceAnimation();
        _entranceAnimationToken = token;
        _entranceAnimationStarted = false;
        Opacity = 0;
        TableViewTransitionTrace.Write(
            $"  row prepare: token={token} loaded={IsLoaded} duration={duration.TotalMilliseconds}ms " +
            $"delay={delay.TotalMilliseconds:0}ms slide={slideOffsetY:0}px");

        var storyboard = new Storyboard();
        // 先快后慢更“利索”：线性曲线在同样的时长下会显得拖
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            // 逐行错开就靠它：延迟计入动画时间线，Completed 也会相应往后推
            BeginTime = delay,
            Duration = new Duration(duration),
            EasingFunction = easing,
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);

        // 上滑：行的 RenderTransform 库内没别处用（只有选中竖条用它自己的变换），所以这里可以安全占用；
        // 它就是自带 ContentThemeTransition / EntranceThemeTransition 的等价效果（从下方偏移滑到位）。
        TranslateTransform? slide = null;
        if (slideOffsetY != 0)
        {
            slide = new TranslateTransform { Y = slideOffsetY };
            RenderTransform = slide;

            var slideAnimation = new DoubleAnimation
            {
                From = slideOffsetY,
                To = 0,
                BeginTime = delay,
                Duration = new Duration(duration),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(slideAnimation, slide);
            Storyboard.SetTargetProperty(slideAnimation, "Y");
            storyboard.Children.Add(slideAnimation);
        }

        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_entranceAnimationStoryboard, storyboard)) return;
            _entranceAnimationStoryboard = null;
            TableViewTransitionTrace.Write($"  row done: token={token} opacityAtEnd={Opacity:0.###}");
            Opacity = 1;
            if (slide is not null && ReferenceEquals(RenderTransform, slide))
            {
                slide.Y = 0;
            }
        };

        _entranceAnimationStoryboard = storyboard;
        return true;
    }

    /// <summary>
    /// Whether this row has an entrance animation prepared for the given batch that hasn't started yet.
    /// </summary>
    internal bool HasPendingEntranceAnimation(int token) =>
        _entranceAnimationToken == token && !_entranceAnimationStarted && _entranceAnimationStoryboard is not null;

    /// <summary>
    /// Starts the entrance animation prepared by <see cref="PrepareEntranceAnimation"/>. Called by
    /// <see cref="TableView"/> for the whole batch at once, so the rows fade in together instead of dribbling in
    /// one by one as they are realized.
    /// </summary>
    internal void StartPreparedEntranceAnimation(int token)
    {
        if (_entranceAnimationToken != token || _entranceAnimationStarted) return;

        var storyboard = _entranceAnimationStoryboard;
        if (storyboard is null) return;

        _entranceAnimationStarted = true;

        if (IsLoaded)
        {
            storyboard.Begin();
            return;
        }

        // 还没进树的行上起动画会被丢掉，等 Loaded 再播
        RoutedEventHandler? handler = null;
        handler = (_, _) =>
        {
            Loaded -= handler;
            if (_entranceAnimationToken == token && ReferenceEquals(_entranceAnimationStoryboard, storyboard))
                storyboard.Begin();
        };
        Loaded += handler;
    }

    /// <summary>
    /// Cancels a pending or running entrance animation and restores full opacity. Called when the container is
    /// recycled, so a half-faded row can never be reused for another item.
    /// </summary>
    internal void CancelEntranceAnimation()
    {
        _entranceAnimationToken = -1;
        StopEntranceAnimation();
    }

    /// <summary>
    /// Stops the entrance animation and restores full opacity.
    /// </summary>
    private void StopEntranceAnimation()
    {
        if (_entranceAnimationStoryboard is null && Opacity >= 1) return;

        _entranceAnimationStoryboard?.Stop();
        _entranceAnimationStoryboard = null;
        _entranceAnimationStarted = false;
        Opacity = 1;

        // 上滑用的变换是我们自己挂的，取消时把它归位，避免复用到别的条目上时带着偏移
        if (RenderTransform is TranslateTransform slide)
        {
            slide.Y = 0;
        }
    }

    /// <summary>
    /// Initializes a new instance of the TableViewRow class.
    /// </summary>
    public TableViewRow()
    {
        DefaultStyleKey = typeof(TableViewRow);

        SizeChanged += OnSizeChanged;
        Loaded += TableViewRow_Loaded;
#if WINDOWS
        ContextRequested += OnContextRequested;
        RegisterPropertyChangedCallback(IsSelectedProperty, delegate { OnIsSelectedChanged(); });
#endif
        RegisterPropertyChangedCallback(ForegroundProperty, delegate { OnForegroundChanged(); });
        RegisterPropertyChangedCallback(BackgroundProperty, delegate { OnBackgroundChanged(); });
    }

#if !WINDOWS
    /// <inheritdoc/>
    protected override void OnRightTapped(RightTappedRoutedEventArgs e)
    {
        base.OnRightTapped(e);

        var position = e.GetPosition(this);
#else
    /// <summary>
    /// Handles the ContextRequested event.
    /// </summary>
    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (!e.TryGetPosition(sender, out var position)) return;
#endif

        // Select the row before showing the Context Menu
        if (TableView is not null && TableView.ForceRowOrCellSelectionOnContextRequested && !IsSelected)
        {
            TableView.MakeSelection(new TableViewCellSlot(Index, -1), false);
        }

        e.Handled = TableView?.ShowRowContext(this, position) is true;
    }

#if WINDOWS
    /// <summary>
    /// Handles the IsSelected property changed.
    /// </summary>
    private void OnIsSelectedChanged()
    {
        EnsureLayout();
        RowPresenter?.SetRowDetailsVisibility();
    }
#endif

    /// <summary>
    /// Handles the Foreground property changed.
    /// </summary>
    private void OnForegroundChanged()
    {
        _cellPresenterForeground = Foreground;
        EnsureAlternateColors();
    }

    /// <summary>
    /// Handles the Background property changed.
    /// </summary>
    private void OnBackgroundChanged()
    {
        _cellPresenterBackground = Background;
        EnsureAlternateColors();
    }

    /// <summary>
    /// Handles the Loaded event.
    /// </summary>
    private void TableViewRow_Loaded(object sender, RoutedEventArgs e)
    {
        _focusVisualMargin = FocusVisualMargin;

        RowPresenter?.EnsureGridLines();
        EnsureLayout();
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _cellPresenterBackground = Background;
        _cellPresenterForeground = Foreground;
        _itemPresenter = GetTemplateChild("Root") as ListViewItemPresenter;
#if !WINDOWS
        RowPresenter = GetTemplateChild("RowPresenter") as TableViewRowPresenter;
        _selectionBackground = GetTemplateChild("SelectionBackground") as Border;
#endif
    }

    /// <inheritdoc/>
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        if (_ensureCells)
        {
            EnsureCells();
        }
        else
        {
            // A recycled container may have been built for a different set of visible columns
            // (e.g. a column toggled visible while this row was off-screen). Rebuild the cells so
            // the newly shown column appears on every row, instead of only the first realized one.
            if (TableView?.Columns.VisibleColumns is { } visibleColumns && !HasCellsFor(visibleColumns))
            {
                RowPresenter?.ClearCells();
                AddCells(visibleColumns);
            }

            foreach (var cell in Cells)
            {
                // Defensively resync width on reuse — a recycled container can otherwise keep a
                // stale Width if it missed a Column.ActualWidth change while off-screen (e.g. an
                // auto-width recalculation triggered by a sort), leaving cells misaligned with headers.
                if (cell.Column is not null)
                {
                    cell.Width = cell.Column.ActualWidth;
                }

                cell.RefreshElement();
            }
        }

        RowPresenter?.InvalidateMeasure(); // The cells presenter does not measure every time.
        TableView?.EnsureAlternateRowColors();
    }

    /// <inheritdoc/>
    protected override void OnDoubleTapped(DoubleTappedRoutedEventArgs e)
    {
        var eventArgs = new TableViewRowDoubleTappedEventArgs(Index, this, Content);
        TableView?.OnRowDoubleTapped(eventArgs);
        e.Handled = eventArgs.Handled;

        base.OnDoubleTapped(e);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        finalSize = base.ArrangeOverride(finalSize);

        var cornerRadius = _itemPresenter?.CornerRadius ?? new();
        var left = Math.Max(cornerRadius.TopLeft, cornerRadius.BottomLeft);

        _itemPresenter?.Arrange(new Rect(-left, 0, _itemPresenter.ActualWidth + left, _itemPresenter.ActualHeight));

        // Position feeds drag-selection hit testing only; a column-width change never moves a row
        // relative to the drag canvas, so recomputing it (a visual-tree transform walk) on every
        // row on every frame of a resize drag is pure waste.
        if (TableView?.IsColumnResizing != true)
        {
            UpdatePosition();
        }

        return finalSize;
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);

        e.Handled = TableView?.OnAnyPointerPressed(this, e) ?? false;
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);

        TableView?.EndDragSelection();
    }

    /// <summary>
    /// Updates the position of the row relative to the TableView.
    /// </summary>
    internal void UpdatePosition()
    {
        if (TableView is null) return;

        try
        {
            Position = TransformToVisual(TableView.DragRectangleCanvas).TransformPoint(default);
        }
        catch (Exception ex)
        {
            TableViewTrace.Write($"UpdatePosition failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets or sets the position of the row relative to the TableView.
    /// </summary>
    internal Point Position { get; set; }

    /// <summary>
    /// Ensures cells are created for the row.
    /// </summary>
    internal void EnsureCells()
    {
        if (TableView is null)
        {
            return;
        }

        if (RowPresenter is not null && _ensureCells)
        {
            RowPresenter.ClearCells();

            AddCells(TableView.Columns.VisibleColumns);
            _ensureCells = false;
        }
    }

    /// <summary>
    /// Returns whether the row already has exactly one cell per visible column, in the same order.
    /// </summary>
    private bool HasCellsFor(IList<TableViewColumn> visibleColumns)
    {
        var cells = Cells;
        if (cells.Count != visibleColumns.Count)
        {
            return false;
        }

        for (var i = 0; i < visibleColumns.Count; i++)
        {
            if (!ReferenceEquals(cells[i].Column, visibleColumns[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Handles the SizeChanged event.
    /// </summary>
    private async void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (TableView?.CurrentCellSlot?.Row == Index)
        {
            _ = await TableView.ScrollCellIntoView(TableView.CurrentCellSlot.Value);
        }

        // Update the row positions in the TableView after the size change.
        TableView?.UpdateRowPositions();
    }

    /// <summary>
    /// Handles the collection changed event for the columns.
    /// </summary>
    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.OfType<TableViewColumn>() is IEnumerable<TableViewColumn> newItems)
        {
            AddCells(newItems.Where(x => x.Visibility == Visibility.Visible));
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems?.OfType<TableViewColumn>() is IEnumerable<TableViewColumn> oldItems)
        {
            RemoveCells(oldItems);
        }
        else if (e.Action == NotifyCollectionChangedAction.Move && e.NewItems?.Count > 0)
        {
            RowPresenter?.MoveCells(e.NewItems.OfType<TableViewColumn>().First(), e.NewStartingIndex);
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset && RowPresenter is not null)
        {
            RowPresenter.ClearCells();
        }
    }

    /// <summary>
    /// Handles the property changed event for a column.
    /// </summary>
    private void OnColumnPropertyChanged(object? sender, TableViewColumnPropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TableViewColumn.Visibility))
        {
            if (e.Column.Visibility == Visibility.Visible)
            {
                AddCells([e.Column]);
            }
            else
            {
                RemoveCells([e.Column]);
            }
        }
        else if ((e.PropertyName is nameof(TableViewColumn.Order) ||
            e.PropertyName is nameof(TableViewColumn.IsFrozen)) &&
            e.Column.Visibility is Visibility.Visible)
        {
            RemoveCells([e.Column]);
            AddCells([e.Column]);
        }
        else if (e.PropertyName is nameof(TableViewColumn.ActualWidth))
        {
            if (Cells.FirstOrDefault(x => x.Column == e.Column) is { } cell)
            {
                cell.Width = e.Column.ActualWidth;
            }
        }
        else if (e.PropertyName is nameof(TableViewColumn.IsReadOnly))
        {
            UpdateCellsState();
        }
        else if (e.PropertyName is nameof(TableViewColumn.CellStyle))
        {
            EnsureCellsStyle(e.Column);
        }
        else if (e.PropertyName is nameof(TableViewBoundColumn.ElementStyle))
        {
            EnsureElementStyle(e.Column);
        }
        else if (e.PropertyName is nameof(TableViewBoundColumn.EditingElementStyle))
        {
            EnsureEditingElementStyle(e.Column);
        }
    }

    /// <summary>
    /// Removes cells for the specified columns.
    /// </summary>
    private void RemoveCells(IEnumerable<TableViewColumn> columns)
    {
        if (RowPresenter is not null)
        {
            foreach (var column in columns)
            {
                var cell = RowPresenter.Cells.FirstOrDefault(x => x.Column == column);
                if (cell is not null)
                {
                    RowPresenter.RemoveCell(cell);
                }
            }
        }
    }

    /// <summary>
    /// Adds cells for the specified columns.
    /// </summary>
    private void AddCells(IEnumerable<TableViewColumn> columns)
    {
        if (RowPresenter is not null && TableView is not null)
        {
            foreach (var column in columns)
            {
                var cell = new TableViewCell
                {
                    Row = this,
                    Column = column,
                    TableView = TableView,
                    Index = TableView.Columns.VisibleColumns.IndexOf(column),
                    Width = column.ActualWidth
                };

                cell.SetBinding(HeightProperty, new Binding
                {
                    Path = new PropertyPath($"{nameof(TableViewCell.TableView)}.{nameof(TableView.RowHeight)}"),
                    RelativeSource = new RelativeSource { Mode = RelativeSourceMode.Self }
                });

                cell.SetBinding(MaxHeightProperty, new Binding
                {
                    Path = new PropertyPath($"{nameof(TableViewCell.TableView)}.{nameof(TableView.RowMaxHeight)}"),
                    RelativeSource = new RelativeSource { Mode = RelativeSourceMode.Self }
                });

                cell.SetBinding(MinHeightProperty, new Binding
                {
                    Path = new PropertyPath($"{nameof(TableViewCell.TableView)}.{nameof(TableView.RowMinHeight)}"),
                    RelativeSource = new RelativeSource { Mode = RelativeSourceMode.Self }
                });

                RowPresenter.InsertCell(cell);
            }
        }
    }

    /// <summary>
    /// Handles the TableView changing event.
    /// </summary>
    private void OnTableViewChanging()
    {
        if (TableView is not null)
        {
            TableView.IsReadOnlyChanged -= OnTableViewIsReadOnlyChanged;

            if (TableView.Columns is not null)
            {
                TableView.Columns.CollectionChanged -= OnColumnsCollectionChanged;
                TableView.Columns.ColumnPropertyChanged -= OnColumnPropertyChanged;
            }
        }
    }

    /// <summary>
    /// Handles the TableView changed event.
    /// </summary>
    private void OnTableViewChanged()
    {
        if (TableView is not null)
        {
            TableView.IsReadOnlyChanged += OnTableViewIsReadOnlyChanged;

            if (TableView.Columns is not null)
            {
                TableView.Columns.CollectionChanged += OnColumnsCollectionChanged;
                TableView.Columns.ColumnPropertyChanged += OnColumnPropertyChanged;
            }
        }
    }

    /// <summary>
    /// Handles the IsReadOnly property changed event for the TableView.
    /// </summary>
    private void OnTableViewIsReadOnlyChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UpdateCellsState();
    }

    /// <summary>
    /// Updates the state of the cells.
    /// </summary>
    private void UpdateCellsState()
    {
        foreach (var cell in Cells)
        {
            cell.UpdateElementState();
        }
    }

    private void EnsureElementStyle(TableViewColumn column)
    {
        foreach (var cell in Cells)
        {
            if (cell.Column == column
                && cell.Content is FrameworkElement element
                && cell.Column is TableViewBoundColumn boundColumn
                && (TableView?.IsEditing is false || TableView?.CurrentCellSlot != cell.Slot))
            {
                element.Style = boundColumn.ElementStyle;
            }
        }
    }

    private void EnsureEditingElementStyle(TableViewColumn column)
    {
        if (TableView?.IsEditing is true
            && TableView.CurrentCellSlot is not null
            && column is TableViewBoundColumn boundColumn
            && TableView.GetCellFromSlot(TableView.CurrentCellSlot.Value) is { } cell
            && cell.Column == column
            && cell.Content is FrameworkElement element)
        {
            element.Style = boundColumn.EditingElementStyle;
        }
    }

    /// <summary>
    /// Ensures the cells style is applied.
    /// </summary>
    internal void EnsureCellsStyle(TableViewColumn? column = null, object? dataItem = null)
    {
        var cells = Cells.Where(x => column is null || x.Column == column);

        foreach (var cell in cells)
        {
            cell.EnsureStyle(dataItem ?? Content);
        }
    }

    /// <summary>
    /// Applies the current cell state to the specified slot.
    /// </summary>
    internal void ApplyCurrentCellState(TableViewCellSlot slot)
    {
        if (slot.Column >= 0 && slot.Column < Cells.Count)
        {
            var cell = Cells[slot.Column];
            cell.ApplyCurrentCellState();
        }
    }

    /// <summary>
    /// Applies the selection state to the cells.
    /// </summary>
    internal void ApplyCellsSelectionState()
    {
        foreach (var cell in Cells)
        {
            cell.ApplySelectionState();
        }
    }

    /// <summary>
    /// Ensures the layout of the row.
    /// </summary>
    internal void EnsureLayout()
    {
        var cornerRadius = _itemPresenter?.CornerRadius ?? new();
        var left = Math.Max(cornerRadius.TopLeft, cornerRadius.BottomLeft) / 2;
        var detailsHeight = RowPresenter?.GetDetailsContentHeight() ?? 0d;
#if WINDOWS
        _selectionIndicator ??= _itemPresenter?.FindDescendants()
                                                .OfType<Border>()
                                                .FirstOrDefault(x => x is { Width: 3 });
        var selectionIndicator = _selectionIndicator;

        var cellsHeight = ActualHeight - detailsHeight;
        var selectionIndicatorHeight = Math.Max(Selection_IndicatorHeight, cellsHeight - 40);

        if (selectionIndicator is not null)
        {
            selectionIndicator.MaxHeight = selectionIndicatorHeight;
            selectionIndicator.Margin = new Thickness(
                _selectionIndicatorMargin.Left + left,
                _selectionIndicatorMargin.Top,
                _selectionIndicatorMargin.Right,
                _selectionIndicatorMargin.Bottom);
        }

        if (TableView is ListView { SelectionMode: ListViewSelectionMode.Multiple })
        {
            var fontIcon = this.FindDescendant<FontIcon>(x => x.Glyph == Check_Mark);
            selectionIndicator = fontIcon?.Parent as Border;
        }

        if (TableView is ListView { SelectionMode: ListViewSelectionMode.Multiple })
        {
            var fontIcon = this.FindDescendant<FontIcon>(x => x.Glyph == Check_Mark);
            selectionIndicator = fontIcon?.Parent as Border;
        }


        _selectionBackground ??= _itemPresenter?.FindDescendants()
                                                .OfType<Border>()
                                                .FirstOrDefault(x => x.Name is not Selection_Background && x.Margin == _selectionBackgroundMargin);

        FocusVisualMargin = new Thickness(
            _focusVisualMargin.Left + left,
            _focusVisualMargin.Top,
            _focusVisualMargin.Right,
            _focusVisualMargin.Bottom + GetHorizontalGridlineHeight());

        EnsureSelectionIndicatorPosition(detailsHeight, selectionIndicator);
#endif
        if (_selectionBackground is not null)
        {
            _selectionBackground.Name = Selection_Background;
            _selectionBackground.Margin = new Thickness(
                _selectionBackgroundMargin.Left + left,
                _selectionBackgroundMargin.Top,
                _selectionBackgroundMargin.Right,
                _selectionBackgroundMargin.Bottom + GetHorizontalGridlineHeight() + detailsHeight);
        }
    }

    /// <summary>
    /// Ensures the position of the selection indicator.
    /// </summary>
    private async void EnsureSelectionIndicatorPosition(double detailsHeight, Border? selectionIndicator)
    {
        await Task.Yield(); // let the animations and visual state changes complete

        if (selectionIndicator is null)
        {
            return;
        }

        // 这里每次都换上一个全新的 CompositeTransform（而不是复用模板里的那个）：
        // 模板的 Selected 视觉状态只把 ScaleY 动画到 1，Normal 状态并不把它还原，
        // 残留的动画值会盖住本地值，让已经取消选中、尤其是行容器被回收给别的条目的行继续显示竖条
        // —— 表现为“进入文件夹后有些项莫名其妙被选中”。
        // 换成没有挂任何动画的新变换后，ScaleY 完全由本行的 IsSelected 决定，
        // 模板里那些指向旧变换的动画也不再影响渲染（它们只会作用到已被替换掉的对象上）。
        selectionIndicator.RenderTransform = new CompositeTransform
        {
            ScaleX = 1,
            ScaleY = IsSelected ? 1 : 0,
            // 行详情面板可见时把竖条上移半个面板高度，否则回到原位
            TranslateY = RowPresenter?.IsDetailsPanelVisible ?? false ? Math.Round(-detailsHeight / 2) : 0
        };
    }

    /// <summary>
    /// Ensures alternate colors are applied to the row.
    /// </summary>
    internal void EnsureAlternateColors()
    {
        if (TableView is null || RowPresenter is null) return;

        RowPresenter.Background =
            Index % 2 == 1 && TableView.AlternateRowBackground is not null ? TableView.AlternateRowBackground : _cellPresenterBackground;

        RowPresenter.Foreground =
            Index % 2 == 1 && TableView.AlternateRowForeground is not null ? TableView.AlternateRowForeground : _cellPresenterForeground;
    }

    internal void UpdateSelectCheckMarkOpacity()
    {
        var fontIcon = this.FindDescendant<FontIcon>(x => x.Glyph == Check_Mark);

        if (fontIcon?.Parent is Border border)
        {
            border.Opacity = TableView?.IsEditing is true ? 0.3 : 1;
        }
    }

    /// <summary>
    /// Gets the height of the horizontal gridlines.
    /// </summary>
    private double GetHorizontalGridlineHeight()
    {
        return TableView?.GridLinesVisibility is TableViewGridLinesVisibility.All or TableViewGridLinesVisibility.Horizontal
            ? TableView.HorizontalGridLinesStrokeThickness : 0d;
    }

    /// <summary>
    /// Gets the list of cells in the row.
    /// </summary>
    public IReadOnlyList<TableViewCell> Cells => RowPresenter?.Cells ?? [];

    /// <summary>
    /// Gets the index of the row.
    /// </summary>
    public int Index => TableView?.IndexFromContainer(this) ?? -1;

    /// <summary>
    /// Gets or sets the TableView associated with the row.
    /// </summary>
    public TableView? TableView
    {
        get;
        internal set
        {
            if (field != value)
            {
                OnTableViewChanging();
                field = value;
                OnTableViewChanged();
            }
        }
    }

    /// <inheritdoc/>
    public TableViewRowPresenter? RowPresenter
#if WINDOWS
       => ContentTemplateRoot as TableViewRowPresenter;
#else
    { get; private set; }
#endif

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new AutomationPeers.TableViewRowAutomationPeer(this);
    }
}
