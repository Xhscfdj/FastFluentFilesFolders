using FastFluentFilesFolders.Extensions;
using FastFluentFilesFolders.Extensions.Interfaces;
using FastFluentFilesFolders.Helpers;
using FastFluentFilesFolders.Models;
using FastFluentFilesFolders.Services;
using FastFluentFilesFolders.UserControls;
using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;
using WinRT.Interop;
using WinUI.TableView;

namespace FastFluentFilesFolders.Views
{
    public sealed partial class MiddleFilesView : Page
    {
        private CommandBarFlyout? _itemContextFlyout;
        private CommandBarFlyout? _baseContextFlyout;
        private CommandBarFlyout? _recycleItemFlyout;
        private CommandBarFlyout? _recycleBaseFlyout;
        private AppBarButton? _pinToggleButton;
        private bool IsInRecycleView => (this.DataContext as MainWindowViewModel)?.IsRecycleBinFolder == true;
        private bool IsSearchView => (this.DataContext as MainWindowViewModel)?.IsSearchMode == true;
        private bool _toolbarBuilt;
        private (ObservableCollection<FileSystemNodeViewModel>? Items, bool Special)? _lastAppliedGroupedSource;
        private readonly List<ICommandBarElement> _itemPluginItems = new();
        private readonly List<ICommandBarElement> _basePluginItems = new();
        private ObservableCollection<FileSystemNodeViewModel>? _watchedCollection;
        private MainWindowViewModel? _dataContextVm;
        // 双击进入文件夹后，待新内容挂载完成时把键盘焦点移回文件表格（令左侧 TreeView 失焦）
        private bool _focusTableAfterNavigation;
        private readonly ObservableCollection<FileOperationItem> _fileOperationItems = new();
        private static MultiLanguageStringsViewModel ML => App.ML;

        public MiddleFilesView()
        {
            InitializeComponent();
            RefreshHeaders();
            App.ML.PropertyChanged += OnMLPropertyChanged;
            this.DataContext = App.SharedViewModel;

            FileGrid.ContextRequested += OnFileGridContextRequested;
            FileGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnFileGridKeyDown), true);

            App.SharedViewModel.RenameFocusRequested += OnRenameFocusRequested;
            App.SharedViewModel.SelectItemRequested += OnSelectItemRequested;
            App.SharedViewModel.ConflictResolutionRequested += OnConflictResolutionRequested;

            FileOperationReporter.OperationAdded += OnFileOperationReported;

            this.Loaded += (_, _) =>
            {
                if (_toolbarBuilt) return;
                _toolbarBuilt = true;
                BuildToolbar();
            };
            this.Unloaded += OnUnloaded;

            var copyAccel = new KeyboardAccelerator { Key = VirtualKey.C, Modifiers = VirtualKeyModifiers.Control };
            copyAccel.Invoked += (_, args) => { args.Handled = true; OnCopyClick(null, null); };
            FileGrid.KeyboardAccelerators.Add(copyAccel);

            var pasteAccel = new KeyboardAccelerator { Key = VirtualKey.V, Modifiers = VirtualKeyModifiers.Control };
            pasteAccel.Invoked += (_, args) => { args.Handled = true; OnPasteClick(null, null); };
            FileGrid.KeyboardAccelerators.Add(pasteAccel);

            var cutAccel = new KeyboardAccelerator { Key = VirtualKey.X, Modifiers = VirtualKeyModifiers.Control };
            cutAccel.Invoked += (_, args) => { args.Handled = true; OnCutClick(null, null); };
            FileGrid.KeyboardAccelerators.Add(cutAccel);

            var copyPathAccel = new KeyboardAccelerator { Key = VirtualKey.C, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
            copyPathAccel.Invoked += (_, args) => { args.Handled = true; OnCopyPathClick(null, null); };
            FileGrid.KeyboardAccelerators.Add(copyPathAccel);

            // Ctrl+P：切换当前选中文件夹的固定状态（固定/取消固定到快速访问）
            var pinAccel = new KeyboardAccelerator { Key = VirtualKey.P, Modifiers = VirtualKeyModifiers.Control };
            pinAccel.Invoked += async (_, args) =>
            {
                args.Handled = true;
                if (FileGrid.SelectedItem is FileSystemNodeViewModel pinnedItem && pinnedItem.IsDirectory && !pinnedItem.IsPlaceholder)
                    await TogglePinFolderAsync(pinnedItem);
            };
            FileGrid.KeyboardAccelerators.Add(pinAccel);

            this.DataContextChanged += (s, e) =>
            {
                if (this.DataContext is MainWindowViewModel vm && vm != _dataContextVm)
                {
                    if (_dataContextVm != null)
                        _dataContextVm.PropertyChanged -= OnViewModelPropertyChanged;
                    _dataContextVm = vm;
                    vm.PropertyChanged += OnViewModelPropertyChanged;
                    UpdateGroupedSource(vm);
                }
            };
            if (this.DataContext is MainWindowViewModel currentVm)
            {
                _dataContextVm = currentVm;
                currentVm.PropertyChanged += OnViewModelPropertyChanged;
                UpdateGroupedSource(currentVm);
            }
        }

        private void OnMLPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshAllStrings();

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            App.ML.PropertyChanged -= OnMLPropertyChanged;
            App.SharedViewModel.RenameFocusRequested -= OnRenameFocusRequested;
            App.SharedViewModel.SelectItemRequested -= OnSelectItemRequested;
            App.SharedViewModel.ConflictResolutionRequested -= OnConflictResolutionRequested;
            FileOperationReporter.OperationAdded -= OnFileOperationReported;
            if (_dataContextVm != null)
                _dataContextVm.PropertyChanged -= OnViewModelPropertyChanged;
            if (_watchedCollection != null)
                _watchedCollection.CollectionChanged -= OnCurrentFolderCollectionChanged;
            _lastAppliedGroupedSource = null;
            this.Unloaded -= OnUnloaded;
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentFolderContent) ||
                e.PropertyName == nameof(MainWindowViewModel.IsCurrentFolderSpecial) ||
                e.PropertyName == nameof(MainWindowViewModel.IsSearchMode) ||
                e.PropertyName == nameof(MainWindowViewModel.SearchResults) ||
                e.PropertyName == nameof(MainWindowViewModel.SelectedFolder))
            {
                if (sender is MainWindowViewModel vm)
                {
                    UpdateGroupedSource(vm);
                    UpdateRecycleColumnVisibility();

                    if (_focusTableAfterNavigation && e.PropertyName == nameof(MainWindowViewModel.CurrentFolderContent))
                    {
                        _focusTableAfterNavigation = false;
                        FileGrid.Focus(FocusState.Programmatic);
                    }
                }
            }
        }

        private void UpdateGroupedSource(MainWindowViewModel vm)
        {
            if (vm.IsSearchMode)
            {
                if (_watchedCollection != null) { _watchedCollection.CollectionChanged -= OnCurrentFolderCollectionChanged; _watchedCollection = null; }
                _lastAppliedGroupedSource = null;
                FileGrid.UpdateSource(vm.SearchResults, grouped: false);
                return;
            }

            var items = vm.CurrentFolderContent ?? new();
            var special = vm.IsCurrentFolderSpecial;

            // 后台预构建好的数据源（避免 UI 线程做分组构建 SetItems/RebuildFlat）
            if (vm.PendingPrebuiltSource != null)
            {
                var prebuilt = vm.PendingPrebuiltSource;
                var prebuiltVersion = vm.PendingPrebuiltVersion;
                vm.PendingPrebuiltSource = null;

                if (_watchedCollection != null)
                    _watchedCollection.CollectionChanged -= OnCurrentFolderCollectionChanged;
                _watchedCollection = items;
                _watchedCollection.CollectionChanged += OnCurrentFolderCollectionChanged;
                _lastAppliedGroupedSource = (items, special);

                if (vm.CurrentTab == null || prebuiltVersion == vm.CurrentTab.NavigationVersion)
                {
                    FileGrid.UpdateSourcePrebuilt(prebuilt);
                    if (FileGrid.ItemsSource is GroupedFileList gl)
                        gl.SetDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                }
                return;
            }

            // Deduplicate: skip if same (items reference, special flag) was already applied
            if (_lastAppliedGroupedSource is ({ } lastItems, var lastSpecial) &&
                ReferenceEquals(lastItems, items) && lastSpecial == special)
                return;

            if (_watchedCollection != null)
                _watchedCollection.CollectionChanged -= OnCurrentFolderCollectionChanged;

            _watchedCollection = items;
            _watchedCollection.CollectionChanged += OnCurrentFolderCollectionChanged;

            _lastAppliedGroupedSource = (items, special);
            FileGrid.UpdateSource(items, special);
            if (FileGrid.ItemsSource is GroupedFileList list)
                list.SetDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        }

        private void OnCurrentFolderCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (FileGrid.ItemsSource is GroupedFileList list)
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove && e.OldItems != null)
                {
                    foreach (FileSystemNodeViewModel item in e.OldItems)
                        list.RemoveItem(item);
                }
                else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
                {
                    foreach (FileSystemNodeViewModel item in e.NewItems)
                        list.AddItem(item);
                }
                else
                {
                    if (this.DataContext is MainWindowViewModel vm)
                        FileGrid.UpdateSource(vm.CurrentFolderContent ?? new(), vm.IsCurrentFolderSpecial);
                }
            }
        }

        private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FileSystemNodeViewModel item && !item.IsPlaceholder)
            {
                var vm = this.DataContext as MainWindowViewModel;
                vm?.OpenItem(item);

                // 双击进入文件夹后让左侧 TreeView 自动失焦：等新内容挂载完成后把键盘焦点移回文件表格。
                // 否则在“此电脑/回收站/网络/WSL”等虚拟位置下，方向键与快捷键仍会作用到目录树。
                if (item.IsDirectory && !item.IsRecycleEntry)
                    _focusTableAfterNavigation = true;
            }
        }

        private void OnCalculateSizeTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FileSystemNodeViewModel item } && !item.IsPlaceholder)
                item.CalculateSizeCommand.Execute(null);
        }

        private void OnGroupToggleClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is FileSystemNodeViewModel header && header.IsPlaceholder)
            {
                if (FileGrid.ItemsSource is GroupedFileList list)
                    list.ToggleGroup(header);
            }
        }

        private void OnFileGridContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            args.TryGetPosition(sender, out var position);

            var element = args.OriginalSource as DependencyObject;
            TableViewRow? row = null;
            while (element != null)
            {
                if (element is TableViewRow r)
                {
                    row = r;
                    break;
                }
                element = VisualTreeHelper.GetParent(element);
            }

            if (row?.Content is FileSystemNodeViewModel item && !item.IsPlaceholder)
            {
                if (!FileGrid.SelectedItems.Contains(item))
                    FileGrid.SelectedItem = item;
                UpdatePinToggleButton(item);
                var t = sender.TransformToVisual(row);
                var showOptions = new FlyoutShowOptions { Position = t.TransformPoint(position) };

                if (item.IsRecycleEntry)
                {
                    // 回收站条目：还原 / 彻底删除
                    _recycleItemFlyout ??= BuildRecycleItemFlyout();
                    _recycleItemFlyout.ShowAt(row, showOptions);
                }
                else
                {
                    _itemContextFlyout ??= BuildItemContextFlyout();
                    RebuildPluginItems(_itemContextFlyout, _itemPluginItems, item);
                    _itemContextFlyout.ShowAt(row, showOptions);
                }
            }
            else
            {
                if (IsInRecycleView)
                {
                    // 回收站空白处：清空回收站
                    _recycleBaseFlyout ??= BuildRecycleBaseFlyout();
                    _recycleBaseFlyout.ShowAt(sender, new FlyoutShowOptions { Position = position });
                }
                else
                {
                    _baseContextFlyout ??= BuildBaseContextFlyout();
                    RebuildPluginItems(_baseContextFlyout, _basePluginItems, null);
                    _baseContextFlyout.ShowAt(sender, new FlyoutShowOptions { Position = position });
                }
            }
            args.Handled = true;
        }

        public void RefreshHeaders()
        {
            ColName.Header = ML.ColumnName;
            ColModifiedDate.Header = ML.ColumnModifiedDate;
            ColCreatedDate.Header = ML.ColumnCreatedDate;
            ColSize.Header = ML.ColumnSize;
            ColOriginalLocation.Header = ML.ColumnOriginalLocation;
            UpdateRecycleColumnVisibility();
        }

        private void UpdateRecycleColumnVisibility()
        {
            if (ColOriginalLocation == null) return;
            var recycle = (this.DataContext as MainWindowViewModel)?.IsRecycleBinFolder == true;
            ColOriginalLocation.Visibility = recycle ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshAllStrings()
        {
            RefreshHeaders();
            RefreshToolbar();
            RefreshGroupHeaderNames();
            _itemContextFlyout = null;
            _pinToggleButton = null;
            _baseContextFlyout = null;
            _recycleItemFlyout = null;
            _recycleBaseFlyout = null;
        }

        private void RefreshToolbar()
        {
            if (!_toolbarBuilt) return;
            ToolbarCmd.PrimaryCommands.Clear();
            BuildToolbar();
        }

        private void RefreshGroupHeaderNames()
        {
            if (FileGrid.ItemsSource is GroupedFileList list)
                list.RefreshHeaderNames();
        }

        private CommandBarFlyout BuildItemContextFlyout()
        {
            var flyout = new CommandBarFlyout { AlwaysExpanded = true };

            flyout.PrimaryCommands.Add(ThemedBtn(ML.CmdCut,   ThemedIconKey("Icon.Cut"),    OnCutClick));
            flyout.PrimaryCommands.Add(ThemedBtn(ML.CmdCopy,   ThemedIconKey("Icon.Copy"),    OnCopyClick));
            flyout.PrimaryCommands.Add(ThemedBtn(ML.CmdPaste,   ThemedIconKey("Icon.Paste"),   OnPasteIntoFolderClick));
            flyout.PrimaryCommands.Add(ThemedBtn(ML.CmdRename, ThemedIconKey("Icon.Rename"),  OnRenameClick));
            flyout.PrimaryCommands.Add(ThemedBtn(ML.CmdDelete,   ThemedIconKey("Icon.Delete"),  OnDeleteClick));
            flyout.PrimaryCommands.Add(RedBtn(ML.CmdPermanentDelete, "\uECC9", OnPermanentDeleteClick));

            flyout.SecondaryCommands.Add(PlainBtn(ML.CmdOpen,     "\uE8E5", OnOpenClick));
            flyout.SecondaryCommands.Add(PlainBtn(ML.CmdOpenWith, "\uE8E5", OnOpenWithClick));
            flyout.SecondaryCommands.Add(CopyPathThemedBtn(ML.CmdCopyPath, OnCopyPathClick));

            // 固定/取消固定（仅文件夹显示；打开菜单时按选中项刷新）
            _pinToggleButton = new AppBarButton
            {
                Label = ML.CmdPinToQuickAccess,
                Icon = new FontIcon { Glyph = "\uE718", FontSize = 16 },
                KeyboardAcceleratorTextOverride = "Ctrl+P",
                Visibility = Visibility.Collapsed
            };
            _pinToggleButton.Click += OnPinToggleClick;
            flyout.SecondaryCommands.Add(_pinToggleButton);
            flyout.SecondaryCommands.Add(PlainBtn(ML.OpenFileLocation, "\uE8B7", OnOpenFileLocationClick));
            flyout.SecondaryCommands.Add(new AppBarSeparator());
            flyout.SecondaryCommands.Add(PlainBtn(ML.CmdProperties, "\uE90F", OnPropertiesClick));
            flyout.SecondaryCommands.Add(new AppBarSeparator());
            flyout.SecondaryCommands.Add(BuildShowMoreOptionsBtn(isItemMenu: true));

            return flyout;
        }

        private CommandBarFlyout BuildBaseContextFlyout()
        {
            var flyout = new CommandBarFlyout { AlwaysExpanded = true };

            var newSubMenu = new MenuFlyout();
            newSubMenu.Items.Add(SubMenuBtn(ML.NewTextDocument, "\uE7C3", OnNewTextDocumentClick));
            newSubMenu.Items.Add(SubMenuBtn(ML.NewShortcut, "\uE71B", OnNewShortcutClick));
            newSubMenu.Items.Add(SubMenuBtn(ML.NewFile,     "\uE7C3", OnNewFileClick));
            newSubMenu.Items.Add(new MenuFlyoutSeparator());
            newSubMenu.Items.Add(SubMenuBtn(ML.NewExcelSpreadsheet, "\uE9F9", OnNewExcelClick));
            newSubMenu.Items.Add(SubMenuBtn(ML.NewWordDocument,  "\uE89A", OnNewWordClick));
            newSubMenu.Items.Add(SubMenuBtn(ML.NewPowerPointPresentation,   "\uE8B4", OnNewPowerPointClick));

            var newBtn = new AppBarButton
            {
                Label = ML.CmdNew,
                Content = new ThemedIcon { Style = (Style)Application.Current.Resources["Icon.New"] },
                Flyout = newSubMenu
            };
            flyout.SecondaryCommands.Add(newBtn);
            flyout.SecondaryCommands.Add(PlainBtn(ML.CmdNewFolder, "\uE8F4", OnNewFolderClick));
            flyout.SecondaryCommands.Add(new AppBarSeparator());
            flyout.SecondaryCommands.Add(PlainBtn(ML.CmdPaste, "\uE77F", OnPasteClick));
            flyout.SecondaryCommands.Add(new AppBarSeparator());
            flyout.SecondaryCommands.Add(BuildShowMoreOptionsBtn(isItemMenu: false));

            return flyout;
        }

        private CommandBarFlyout BuildRecycleItemFlyout()
        {
            var flyout = new CommandBarFlyout { AlwaysExpanded = true };

            flyout.PrimaryCommands.Add(PlainBtn(ML.RecycleRestore, "\uE8E5", OnRecycleRestoreClick));
            flyout.PrimaryCommands.Add(RedBtn(ML.CmdPermanentDelete, "\uECC9", OnRecycleDeleteClick));
            flyout.SecondaryCommands.Add(PlainBtn(ML.CmdCopyPath, "\uE8C8", OnRecycleCopyPathClick));

            return flyout;
        }

        private CommandBarFlyout BuildRecycleBaseFlyout()
        {
            var flyout = new CommandBarFlyout { AlwaysExpanded = true };
            flyout.SecondaryCommands.Add(PlainBtn(ML.RecycleEmpty, "\uE74D", OnRecycleEmptyClick));
            return flyout;
        }

        private async void OnRecycleRestoreClick(object sender, RoutedEventArgs e)
        {
            _recycleItemFlyout?.Hide();
            var vm = this.DataContext as MainWindowViewModel;
            var items = GetSelectedItems().Where(i => i.IsRecycleEntry).ToList();
            foreach (var item in items)
                await vm!.RestoreRecycleItemAsync(item);
        }

        private void OnRecycleCopyPathClick(object sender, RoutedEventArgs e)
        {
            _recycleItemFlyout?.Hide();
            OnCopyPathClick(sender, e);
        }

        private async void OnRecycleDeleteClick(object? sender, RoutedEventArgs? e)
        {
            _recycleItemFlyout?.Hide();
            var vm = this.DataContext as MainWindowViewModel;
            var items = GetSelectedItems().Where(i => i.IsRecycleEntry).ToList();
            if (items.Count == 0) return;

            var dialog = new ContentDialog
            {
                Title = ML.RecycleDeleteConfirmTitle,
                Content = ML.RecycleDeleteConfirmMessage,
                CloseButtonText = ML.CmdCancel,
                PrimaryButtonText = ML.CmdPermanentDelete,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = App.MainWindow!.Content.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                await vm!.PermanentDeleteRecycleItemsAsync(items);
        }

        private async void OnRecycleEmptyClick(object sender, RoutedEventArgs e)
        {
            _recycleBaseFlyout?.Hide();
            var vm = this.DataContext as MainWindowViewModel;
            if (vm == null) return;

            var dialog = new ContentDialog
            {
                Title = ML.RecycleEmptyConfirmTitle,
                Content = ML.RecycleEmptyConfirmMessage,
                CloseButtonText = ML.CmdCancel,
                PrimaryButtonText = ML.RecycleEmpty,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = App.MainWindow!.Content.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                await vm.EmptyRecycleBinAsync();
        }

        private void OnSortNameAscClick(object s, RoutedEventArgs e) => FileGrid.SortBy("Name", true);
        private void OnSortNameDescClick(object s, RoutedEventArgs e) => FileGrid.SortBy("Name", false);
        private void OnSortModifiedDescClick(object s, RoutedEventArgs e) => FileGrid.SortBy("LastModifiedTime", false);
        private void OnSortModifiedAscClick(object s, RoutedEventArgs e) => FileGrid.SortBy("LastModifiedTime", true);
        private void OnSortCreatedDescClick(object s, RoutedEventArgs e) => FileGrid.SortBy("FirstCreatedTime", false);
        private void OnSortCreatedAscClick(object s, RoutedEventArgs e) => FileGrid.SortBy("FirstCreatedTime", true);
        private void OnSortSizeDescClick(object s, RoutedEventArgs e) => FileGrid.SortBy("ExactSize", false);
        private void OnSortSizeAscClick(object s, RoutedEventArgs e) => FileGrid.SortBy("ExactSize", true);

        private void BuildToolbar()
        {
            if (ToolbarCmd == null) return;

            ToolbarCmd.PrimaryCommands.Add(TbIconBtn("Icon.Cut", ML.CmdCut, OnCutClick));
            ToolbarCmd.PrimaryCommands.Add(TbIconBtn("Icon.Copy", ML.CmdCopy, OnCopyClick));
            ToolbarCmd.PrimaryCommands.Add(TbIconBtn("Icon.Paste", ML.CmdPaste, OnPasteClick));
            ToolbarCmd.PrimaryCommands.Add(TbIconBtn("Icon.Rename", ML.CmdRename, OnRenameClick));
            ToolbarCmd.PrimaryCommands.Add(TbIconBtn("Icon.Delete", ML.CmdDelete, OnDeleteClick));
            ToolbarCmd.PrimaryCommands.Add(TbRedBtn(ML.CmdPermanentDelete, OnPermanentDeleteClick));
            ToolbarCmd.PrimaryCommands.Add(new AppBarSeparator());

            var newBtn = TbLabelBtn("Icon.New", ML.CmdNew, null);
            newBtn.Flyout = BuildNewToolbarFlyout();
            ToolbarCmd.PrimaryCommands.Add(newBtn);

            var sortBtn = TbLabelBtn("Icon.Sort", ML.CmdSort, null);
            sortBtn.Flyout = BuildSortToolbarFlyout();
            ToolbarCmd.PrimaryCommands.Add(sortBtn);

            FileOpsBtn.SetItems(_fileOperationItems);
        }

        private static AppBarButton TbIconBtn(string styleKey, string tooltip, RoutedEventHandler? click)
        {
            var icon = new ThemedIcon();
            icon.Style = (Style)Application.Current.Resources[styleKey];
            var btn = new AppBarButton
            {
                Width = 40, Height = 40,
                LabelPosition = CommandBarLabelPosition.Collapsed,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = new Viewbox { Child = icon, Width = 20, Height = 20 }
            };
            ToolTipService.SetToolTip(btn, tooltip);
            if (click != null) btn.Click += click;
            return btn;
        }

        private static AppBarButton TbLabelBtn(string styleKey, string label, RoutedEventHandler? click)
        {
            var icon = new ThemedIcon();
            icon.Style = (Style)Application.Current.Resources[styleKey];
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new Viewbox { Child = icon, Width = 18, Height = 18 });
            stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(new FontIcon { Glyph = "\uE70D", FontSize = 10, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 });
            var btn = new AppBarButton
            {
                LabelPosition = CommandBarLabelPosition.Collapsed,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = stack
            };
            if (click != null) btn.Click += click;
            return btn;
        }

        private static AppBarButton TbRedBtn(string tooltip, RoutedEventHandler? click)
        {
            var fontIcon = new FontIcon { Glyph = "\uECC9", FontSize = 18, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 59, 48)) };
            var btn = new AppBarButton
            {
                Width = 40, Height = 40,
                LabelPosition = CommandBarLabelPosition.Collapsed,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = new Viewbox { Child = fontIcon, Width = 20, Height = 20 }
            };
            ToolTipService.SetToolTip(btn, tooltip);
            if (click != null) btn.Click += click;
            return btn;
        }

        private MenuFlyout BuildNewToolbarFlyout()
        {
            var flyout = new MenuFlyout();
            flyout.Items.Add(SubMenuBtn(ML.NewTextDocument, "\uE7C3", OnNewTextDocumentClick));
            flyout.Items.Add(SubMenuBtn(ML.NewShortcut, "\uE71B", OnNewShortcutClick));
            flyout.Items.Add(SubMenuBtn(ML.NewFile, "\uE7C3", OnNewFileClick));
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(SubMenuBtn(ML.NewExcelSpreadsheet, "\uE9F9", OnNewExcelClick));
            flyout.Items.Add(SubMenuBtn(ML.NewWordDocument, "\uE89A", OnNewWordClick));
            flyout.Items.Add(SubMenuBtn(ML.NewPowerPointPresentation, "\uE8B4", OnNewPowerPointClick));
            return flyout;
        }

        private MenuFlyout BuildSortToolbarFlyout()
        {
            var flyout = new MenuFlyout();
            flyout.Items.Add(SortMenuItem(ML.SortNameAsc, OnSortNameAscClick));
            flyout.Items.Add(SortMenuItem(ML.SortNameDesc, OnSortNameDescClick));
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(SortMenuItem(ML.SortModifiedDesc, OnSortModifiedDescClick));
            flyout.Items.Add(SortMenuItem(ML.SortModifiedAsc, OnSortModifiedAscClick));
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(SortMenuItem(ML.SortCreatedDesc, OnSortCreatedDescClick));
            flyout.Items.Add(SortMenuItem(ML.SortCreatedAsc, OnSortCreatedAscClick));
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(SortMenuItem(ML.SortSizeDesc, OnSortSizeDescClick));
            flyout.Items.Add(SortMenuItem(ML.SortSizeAsc, OnSortSizeAscClick));
            return flyout;
        }

        private static MenuFlyoutItem SortMenuItem(string text, RoutedEventHandler handler)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += handler;
            return item;
        }

        public void AddFileOperation(FileOperationItem item)
        {
            _fileOperationItems.Insert(0, item);
        }

        private void OnFileOperationReported(FileOperationItem item)
        {
            AddFileOperation(item);
        }

        private void RebuildPluginItems(CommandBarFlyout flyout, List<ICommandBarElement> tracker, FileSystemNodeViewModel? targetNode)
        {
            foreach (var old in tracker)
                flyout.SecondaryCommands.Remove(old);
            tracker.Clear();

            AddPluginContextMenuItems(flyout, targetNode, tracker);
        }

        private void AddPluginContextMenuItems(CommandBarFlyout flyout, FileSystemNodeViewModel? targetNode, List<ICommandBarElement> tracker)
        {
            var plugins = App.PluginManager?.GetContextMenuPlugins();
            if (plugins == null || !plugins.Any()) return;

            var location = targetNode != null
                ? (targetNode.IsDirectory ? ContextMenuLocation.FolderItem : ContextMenuLocation.FileItem)
                : ContextMenuLocation.Background;

            bool first = true;
            foreach (var plugin in plugins)
            {
                var items = plugin.GetMenuItems(targetNode, location);
                foreach (var item in items)
                {
                    if (first) { var sep = new AppBarSeparator(); flyout.SecondaryCommands.Add(sep); tracker.Add(sep); first = false; }

                    if (item.IsSeparator)
                    {
                        var sep = new AppBarSeparator();
                        flyout.SecondaryCommands.Add(sep);
                        tracker.Add(sep);
                        continue;
                    }

                    if (item.SubItems != null && item.SubItems.Count > 0)
                    {
                        var subMenu = new MenuFlyout();
                        foreach (var sub in item.SubItems)
                        {
                            if (sub.IsSeparator)
                            {
                                subMenu.Items.Add(new MenuFlyoutSeparator());
                                continue;
                            }
                            var subMenuItem = new MenuFlyoutItem { Text = sub.Header };
                            if (sub.IconGlyph != null)
                                subMenuItem.Icon = new FontIcon { Glyph = sub.IconGlyph, FontSize = 14 };
                            if (sub.Command != null)
                                subMenuItem.Click += (_, _) => { flyout.Hide(); sub.Command.Execute(sub.CommandParameter ?? targetNode); if (targetNode != null && !targetNode.IsPlaceholder) _ = targetNode.RefreshAsync(); };
                            subMenu.Items.Add(subMenuItem);
                        }

                        var appBarBtn = new AppBarButton { Label = item.Header };
                        if (item.ThemedIconKey != null)
                        {
                            var themed = new ThemedIcon();
                            themed.Style = (Style)Application.Current.Resources[item.ThemedIconKey];
                            appBarBtn.Content = themed;
                        }
                        else if (item.IconGlyph != null)
                            appBarBtn.Icon = new FontIcon { Glyph = item.IconGlyph, FontSize = 16 };
                        appBarBtn.Flyout = subMenu;
                        flyout.SecondaryCommands.Add(appBarBtn);
                        tracker.Add(appBarBtn);
                    }
                    else
                    {
                        var btn = new AppBarButton { Label = item.Header };
                        if (item.ThemedIconKey != null)
                        {
                            var themed = new ThemedIcon();
                            themed.Style = (Style)Application.Current.Resources[item.ThemedIconKey];
                            btn.Content = themed;
                        }
                        else if (item.IconGlyph != null)
                            btn.Icon = new FontIcon { Glyph = item.IconGlyph, FontSize = 16 };
                        if (item.Command != null)
                            btn.Click += (_, _) => { flyout.Hide(); item.Command.Execute(item.CommandParameter ?? targetNode); if (targetNode != null && !targetNode.IsPlaceholder) _ = targetNode.RefreshAsync(); };
                        flyout.SecondaryCommands.Add(btn);
                        tracker.Add(btn);
                    }
                }
            }
        }

        private AppBarButton ThemedBtn(string label, string styleKey, RoutedEventHandler? click)
        {
            var icon = new ThemedIcon();
            icon.Style = (Style)Application.Current.Resources[styleKey];
            var btn = new AppBarButton { Label = label, Content = icon };
            if (click != null) btn.Click += click;
            return btn;
        }

        private static string ThemedIconKey(string name) => name;

        private static AppBarButton PlainBtn(string label, string glyph, RoutedEventHandler? click)
        {
            var btn = new AppBarButton
            {
                Label = label,
                Icon = new FontIcon { Glyph = glyph, FontSize = 16 }
            };
            if (click != null) btn.Click += click;
            return btn;
        }

        private static AppBarButton CopyPathThemedBtn(string label, RoutedEventHandler? click)
        {
            var icon = new ThemedIcon();
            icon.Style = (Style)Application.Current.Resources["Icon.Copy"];
            var btn = new AppBarButton
            {
                Label = label,
                Content = new Viewbox { Child = icon, Width = 16, Height = 16 },
                KeyboardAcceleratorTextOverride = "Ctrl+Shift+C"
            };
            if (click != null) btn.Click += click;
            return btn;
        }

        private static AppBarButton RedBtn(string label, string glyph, RoutedEventHandler? click)
        {
            var btn = new AppBarButton
            {
                Label = label,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
                Icon = new FontIcon { Glyph = glyph, FontSize = 16, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red) }
            };
            if (click != null) btn.Click += click;
            return btn;
        }

        private static MenuFlyoutItem SubMenuBtn(string label, string glyph, RoutedEventHandler? click)
        {
            var item = new MenuFlyoutItem
            {
                Text = label,
                Icon = new FontIcon { Glyph = glyph, FontSize = 14 }
            };
            if (click != null) item.Click += click;
            return item;
        }

        private AppBarButton BuildShowMoreOptionsBtn(bool isItemMenu)
        {
            var subMenu = new MenuFlyout();
            if (isItemMenu)
                subMenu.Opening += OnItemShowMoreOptionsOpening;
            else
                subMenu.Opening += OnBaseShowMoreOptionsOpening;

            return new AppBarButton
            {
                Label = ML.CmdShowMoreOptions,
                Icon = new FontIcon { Glyph = "\uE712", FontSize = 16 },
                Flyout = subMenu
            };
        }

        private void OnItemShowMoreOptionsOpening(object? sender, object e)
        {
            if (sender is not MenuFlyout flyout) return;
            flyout.Items.Clear();
            var item = FileGrid.SelectedItem as FileSystemNodeViewModel;
            if (item == null) return;
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow!);
            PopulateNativeContextMenu(flyout, item.FullPath, hwnd, _itemContextFlyout!, item);
        }

        private void OnBaseShowMoreOptionsOpening(object? sender, object e)
        {
            if (sender is not MenuFlyout flyout) return;
            flyout.Items.Clear();
            var vm = this.DataContext as MainWindowViewModel;
            var path = vm?.SelectedFolder?.FullPath ?? vm?.CurrentBreadcrumbPath;
            if (string.IsNullOrEmpty(path)) return;
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow!);
            PopulateNativeContextMenu(flyout, path, hwnd, _baseContextFlyout!, null);
        }

        private static void PopulateNativeContextMenu(MenuFlyout flyout, string path, IntPtr hwnd, CommandBarFlyout parentFlyout, FileSystemNodeViewModel? targetItem)
        {
            try
            {
                var items = NativeContextMenuHelper.BuildMenuItems(path, hwnd);
                if (items.Count == 0)
                {
                    flyout.Items.Add(new MenuFlyoutItem { Text = ML.MsgNoOptionsAvailable, IsEnabled = false });
                    return;
                }
                foreach (var item in items)
                {
                    if (item.IsSeparator)
                    {
                        flyout.Items.Add(new MenuFlyoutSeparator());
                    }
                    else
                    {
                        int cmdId = item.CommandId;
                        string capturedPath = path;
                        var menuItem = new MenuFlyoutItem
                        {
                            Text = item.Label,
                            IsEnabled = item.IsEnabled
                        };
                        menuItem.Click += (s, ev) =>
                        {
                            try
                            {
                                var h = WindowNative.GetWindowHandle(App.MainWindow!);
                                NativeContextMenuHelper.InvokeItem(capturedPath, cmdId, h);
                                parentFlyout.Hide();
                                if (targetItem != null && !targetItem.IsPlaceholder)
                                    _ = targetItem.RefreshAsync();
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ShowMoreOptions] Invoke error: {ex.Message}");
                            }
                        };
                        flyout.Items.Add(menuItem);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShowMoreOptions] Build error: {ex.Message}");
                flyout.Items.Add(new MenuFlyoutItem { Text = ML.MsgCannotLoadOptions, IsEnabled = false });
            }
        }

        private List<FileSystemNodeViewModel> GetSelectedItems()
        {
            var items = new List<FileSystemNodeViewModel>();
            foreach (var obj in FileGrid.SelectedItems)
            {
                if (obj is FileSystemNodeViewModel vm && !vm.IsPlaceholder)
                    items.Add(vm);
            }
            if (items.Count == 0 && FileGrid.SelectedItem is FileSystemNodeViewModel si && !si.IsPlaceholder)
                items.Add(si);
            return items;
        }

        private void FinishItemOp()
        {
            _itemContextFlyout?.Hide();
            foreach (var item in GetSelectedItems())
                _ = item.RefreshAsync();
        }

        private void FinishBaseOp()
        {
            _baseContextFlyout?.Hide();
        }

        // === Click handlers ===
        private void UpdatePinToggleButton(FileSystemNodeViewModel? item)
        {
            if (_pinToggleButton == null) return;
            if (item == null || !item.IsDirectory || item.IsPlaceholder || !System.IO.Directory.Exists(item.FullPath))
            {
                _pinToggleButton.Visibility = Visibility.Collapsed;
                return;
            }
            var vm = this.DataContext as MainWindowViewModel;
            var pinned = vm?.IsFolderPinned(item) ?? QuickAccessHelper.IsPinned(item.FullPath);
            _pinToggleButton.Visibility = Visibility.Visible;
            _pinToggleButton.Label = pinned ? ML.CmdUnpinFromQuickAccess : ML.CmdPinToQuickAccess;
            if (_pinToggleButton.Icon is FontIcon icon)
                icon.Glyph = pinned ? "\uE77A" : "\uE718";
        }

        private async void OnPinToggleClick(object sender, RoutedEventArgs e)
        {
            _itemContextFlyout?.Hide();
            var item = FileGrid.SelectedItem as FileSystemNodeViewModel;
            if (item == null || item.IsPlaceholder || !item.IsDirectory || !System.IO.Directory.Exists(item.FullPath)) return;
            if (this.DataContext is MainWindowViewModel vm)
                await vm.TogglePinnedFolderAsync(item);
        }

        private async Task TogglePinFolderAsync(FileSystemNodeViewModel item)
        {
            if (item == null || item.IsPlaceholder || !item.IsDirectory) return;
            if (this.DataContext is MainWindowViewModel vm)
                await vm.TogglePinnedFolderAsync(item);
            else
                await Task.Run(() => QuickAccessHelper.TogglePin(item.FullPath));
        }

        private void OnOpenClick(object sender, RoutedEventArgs e)
        {
            FinishItemOp();
            var items = GetSelectedItems();
            foreach (var item in items)
                (this.DataContext as MainWindowViewModel)?.OpenItem(item);
        }
        private void OnOpenWithClick(object sender, RoutedEventArgs e)
        {
            FinishItemOp();
            var items = GetSelectedItems();
            if (items.Any(i => i.IsRecycleEntry)) return;
            if (items.Count > 0)
                (this.DataContext as MainWindowViewModel)?.OpenWithCommand.Execute(items[0]);
        }
        private void OnCutClick(object sender, RoutedEventArgs e)
        {
            // M4：回收站、搜索视图不允许剪切（来源目录不明确/无真实路径）
            if (IsInRecycleView || IsSearchView) return;
            FinishItemOp();
            var items = GetSelectedItems();
            if (items.Count > 0)
                (this.DataContext as MainWindowViewModel)?.CutCommand.Execute(items);
        }
        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            if (IsInRecycleView) return;
            FinishItemOp();
            var items = GetSelectedItems();
            if (items.Count > 0)
                (this.DataContext as MainWindowViewModel)?.CopyCommand.Execute(items);
        }
        private void OnPasteClick(object sender, RoutedEventArgs e)
        {
            _itemContextFlyout?.Hide();
            _baseContextFlyout?.Hide();
            ExecutePaste();
        }
        // 右键文件夹 → 粘贴 时，目标为被右键的那个文件夹（与资源管理器一致）
        private void OnPasteIntoFolderClick(object sender, RoutedEventArgs e)
        {
            _itemContextFlyout?.Hide();
            _baseContextFlyout?.Hide();
            if (FileGrid.SelectedItem is FileSystemNodeViewModel item &&
                item.IsDirectory && !item.IsPlaceholder && !item.IsArchiveEntry)
            {
                (this.DataContext as MainWindowViewModel)?.SetPasteTarget(item.FullPath);
            }
            ExecutePaste();
        }

        private void ExecutePaste()
        {
            // M4：回收站/搜索视图/虚拟位置（此电脑、网络、压缩包内部等）不允许粘贴
            if (IsInRecycleView || IsSearchView) return;
            if ((this.DataContext as MainWindowViewModel)?.IsPasteAllowed != true) return;
            // 操作岛卡片由 ViewModel.Paste 统一创建/推进（M1.4：空剪贴板不再产生假成功卡）。
            (this.DataContext as MainWindowViewModel)?.PasteCommand.Execute(null);
        }

        private void OnRenameClick(object sender, RoutedEventArgs e)
        {
            _itemContextFlyout?.Hide();
            var items = GetSelectedItems();
            if (items.Count == 0) return;
            if (items.Any(i => i.IsRecycleEntry || i.IsArchiveEntry)) return;
            var item = items[0];
            (this.DataContext as MainWindowViewModel)?.RenameCommand.Execute(item);
        }

        private void OnSelectItemRequested(FileSystemNodeViewModel item)
        {
            FileGrid.SelectedItem = item;
            FileGrid.ScrollIntoView(item);
        }

        /// <summary>
        /// 剪切粘贴遇到同名项时由 ViewModel 回调到这里弹一次确认框，
        /// 选择结果应用于本次粘贴的全部冲突项。
        /// </summary>
        private async Task<FileConflictResolution> OnConflictResolutionRequested(IReadOnlyList<string> conflictNames)
        {
            var tcs = new TaskCompletionSource<FileConflictResolution>();

            void ShowDialog()
            {
                _ = ShowConflictDialogAsync(conflictNames, tcs);
            }

            if (DispatcherQueue.HasThreadAccess)
                ShowDialog();
            else
                DispatcherQueue.TryEnqueue(ShowDialog);

            return await tcs.Task;
        }

        private async Task ShowConflictDialogAsync(
            IReadOnlyList<string> conflictNames,
            TaskCompletionSource<FileConflictResolution> tcs)
        {
            try
            {
                // 注意：项目自带的 LocalizationService 是简易 JSON 解析器，不会还原 "\n" 转义，
                // 因此换行必须在这里用 Environment.NewLine 拼，不能在语言包里写 \n。
                var preview = string.Join(Environment.NewLine, conflictNames.Take(5));
                var content = string.Format(ML.ConflictMessageFmt, conflictNames.Count)
                    + Environment.NewLine + Environment.NewLine
                    + preview
                    + Environment.NewLine + Environment.NewLine
                    + ML.ConflictHint;

                var dialog = new ContentDialog
                {
                    Title = ML.ConflictTitle,
                    Content = content,
                    PrimaryButtonText = ML.ConflictReplace,
                    SecondaryButtonText = ML.ConflictKeepBoth,
                    CloseButtonText = ML.ConflictSkip,
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = App.MainWindow!.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                tcs.TrySetResult(result switch
                {
                    ContentDialogResult.Primary => FileConflictResolution.Replace,
                    ContentDialogResult.Secondary => FileConflictResolution.KeepBoth,
                    _ => FileConflictResolution.Skip
                });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        private void OnRenameFocusRequested(FileSystemNodeViewModel item)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                var container = FileGrid.ContainerFromItem(item);
                if (container is not TableViewRow row || row.ContentTemplateRoot is not UIElement root) return;

                // 找到名字 TextBlock（NameText），取其父 Grid 作为名字单元格
                var nameText = FindVisualChild<TextBlock>(root);
                if (nameText == null) return;
                var cellGrid = nameText.Parent as Grid;
                if (cellGrid == null) return;
                if (cellGrid.Children.OfType<TextBox>().Any()) return; // 已在重命名中

                // 动态创建重命名输入框：不在模板里每行实例化，降低首帧实体化成本
                var textBox = new TextBox
                {
                    Text = item.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0, 6, 0, 6),
                    Foreground = Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush
                        ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                };
                textBox.DataContext = item;
                textBox.GotFocus += OnRenameTextBoxGotFocus;
                textBox.LostFocus += OnRenameTextBoxLostFocus;
                textBox.KeyDown += OnRenameTextBoxKeyDown;

                Grid.SetColumn(textBox, 2);
                cellGrid.Children.Add(textBox);
                nameText.Visibility = Visibility.Collapsed;

                // 再跳一次调度（Normal），等布局稳定后再聚焦，避免刚创建就被夺走焦点
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    if (textBox.Parent != cellGrid) return;
                    textBox.Focus(FocusState.Programmatic);
                    textBox.SelectAll();
                });
            });
        }

        private void RemoveRenameTextBox(TextBox tb)
        {
            if (tb.Parent is Grid cellGrid)
            {
                var nameText = cellGrid.Children.OfType<TextBlock>().FirstOrDefault();
                if (nameText != null) nameText.Visibility = Visibility.Visible;
                cellGrid.Children.Remove(tb);
            }
            tb.GotFocus -= OnRenameTextBoxGotFocus;
            tb.LostFocus -= OnRenameTextBoxLostFocus;
            tb.KeyDown -= OnRenameTextBoxKeyDown;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void OnRenameTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.Text.Length > 0)
                tb.SelectAll();
        }

        private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is FileSystemNodeViewModel item && item.IsRenaming)
            {
                CommitInlineRename(tb, item);
            }
        }

        private void OnRenameTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is FileSystemNodeViewModel item && item.IsRenaming)
            {
                if (e.Key == VirtualKey.Enter)
                {
                    e.Handled = true;
                    CommitInlineRename(tb, item);
                }
                else if (e.Key == VirtualKey.Escape)
                {
                    e.Handled = true;
                    item.IsRenaming = false;
                    (this.DataContext as MainWindowViewModel)?.CancelRename();
                    RemoveRenameTextBox(tb);
                }
            }
        }

        private async void CommitInlineRename(TextBox tb, FileSystemNodeViewModel item)
        {
            var newName = tb.Text.Trim();
            item.IsRenaming = false;
            RemoveRenameTextBox(tb);
            if (!string.IsNullOrEmpty(newName) && newName != item.Name)
            {
                item.Name = newName;
                await (this.DataContext as MainWindowViewModel)!.CommitRenameAsync(item, newName);
                await item.RefreshAsync();
                if (this.DataContext is MainWindowViewModel vm)
                    UpdateGroupedSource(vm);
            }
        }
        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            _itemContextFlyout?.Hide();
            if (IsInRecycleView) { OnRecycleDeleteClick(sender, e); return; }
            var items = GetSelectedItems();
            if (items.Count > 0)
                (this.DataContext as MainWindowViewModel)?.DeleteCommand.Execute(items);
        }
        private void OnPermanentDeleteClick(object sender, RoutedEventArgs e)
        {
            _itemContextFlyout?.Hide();
            if (IsInRecycleView) { OnRecycleDeleteClick(sender, e); return; }
            var items = GetSelectedItems();
            if (items.Count > 0)
                (this.DataContext as MainWindowViewModel)?.PermanentDeleteCommand.Execute(items);
        }
        private void OnCopyPathClick(object sender, RoutedEventArgs e)
        {
            FinishItemOp();
            var items = GetSelectedItems();
            if (items.Count > 0)
                (this.DataContext as MainWindowViewModel)?.CopyPathCommand.Execute(items);
        }
        private void OnOpenFileLocationClick(object sender, RoutedEventArgs e)
        {
            _itemContextFlyout?.Hide();
            var items = GetSelectedItems();
            if (items.Any(i => i.IsRecycleEntry)) return;
            if (items.Count > 0)
                (this.DataContext as MainWindowViewModel)?.OpenFileLocation(items[0]);
        }
        private void OnPropertiesClick(object sender, RoutedEventArgs e)
        {
            FinishItemOp();
            var items = GetSelectedItems();
            if (items.Any(i => i.IsRecycleEntry)) return;
            if (items.Count > 0)
                (this.DataContext as MainWindowViewModel)?.PropertiesCommand.Execute(items[0]);
        }
        private void OnNewFolderClick(object sender, RoutedEventArgs e)
        {
            FinishBaseOp();
            (this.DataContext as MainWindowViewModel)?.NewFolderCommand.Execute(null);
        }
        private void OnNewTextDocumentClick(object sender, RoutedEventArgs e)
        {
            FinishBaseOp();
            (this.DataContext as MainWindowViewModel)?.NewTextDocumentCommand.Execute(null);
        }
        private void OnNewShortcutClick(object sender, RoutedEventArgs e)
        {
            FinishBaseOp();
            (this.DataContext as MainWindowViewModel)?.NewShortcutCommand.Execute(null);
        }
        private void OnNewFileClick(object sender, RoutedEventArgs e)
        {
            FinishBaseOp();
            (this.DataContext as MainWindowViewModel)?.NewFileCommand.Execute(null);
        }
        private void OnNewExcelClick(object sender, RoutedEventArgs e)
        {
            FinishBaseOp();
            (this.DataContext as MainWindowViewModel)?.NewExcelSpreadsheetCommand.Execute(null);
        }
        private void OnNewWordClick(object sender, RoutedEventArgs e)
        {
            FinishBaseOp();
            (this.DataContext as MainWindowViewModel)?.NewWordDocumentCommand.Execute(null);
        }
        private void OnNewPowerPointClick(object sender, RoutedEventArgs e)
        {
            FinishBaseOp();
            (this.DataContext as MainWindowViewModel)?.NewPowerPointPresentationCommand.Execute(null);
        }

        private void OnFileGridKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var isAltDown = ((int)Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & 1) != 0;

            if (isAltDown && e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                OnPropertiesClick(sender, e);
                return;
            }

            if (e.Handled) return;

            var isCtrlDown = ((int)Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & 1) != 0;
            var isShiftDown = ((int)Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & 1) != 0;

            if (isCtrlDown && isShiftDown && e.Key == VirtualKey.C)
            {
                e.Handled = true;
                OnCopyPathClick(sender, e);
                return;
            }

            if (isCtrlDown && !isAltDown)
            {
                switch (e.Key)
                {
                    case VirtualKey.C:
                        e.Handled = true;
                        OnCopyClick(sender, e);
                        break;
                    case VirtualKey.V:
                        e.Handled = true;
                        OnPasteClick(sender, e);
                        break;
                    case VirtualKey.X:
                        e.Handled = true;
                        OnCutClick(sender, e);
                        break;
                    case VirtualKey.Delete:
                        e.Handled = true;
                        _ = PermanentDeleteWithConfirmAsync();
                        break;
                }
            }
            else if (!isCtrlDown && !isAltDown && e.Key == VirtualKey.Delete)
            {
                e.Handled = true;
                OnDeleteClick(sender, e);
            }
            else if (!isCtrlDown && !isAltDown && e.Key == VirtualKey.F2)
            {
                e.Handled = true;
                var item = FileGrid.SelectedItem as FileSystemNodeViewModel;
                if (item != null && !item.IsPlaceholder)
                    (this.DataContext as MainWindowViewModel)?.RenameCommand.Execute(item);
            }
            else if (!isCtrlDown && !isAltDown && e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                var item = FileGrid.SelectedItem as FileSystemNodeViewModel;
                if (item != null && !item.IsPlaceholder)
                    (this.DataContext as MainWindowViewModel)?.OpenItem(item);
            }
        }

        private async Task PermanentDeleteWithConfirmAsync()
        {
            if (IsInRecycleView)
            {
                OnRecycleDeleteClick(null, null);
                return;
            }
            var items = GetSelectedItems();
            if (items.Count == 0) return;

            var dialog = new ContentDialog
            {
                Title = ML.PermanentDeleteConfirmTitle,
                Content = ML.PermanentDeleteConfirmMessage,
                CloseButtonText = ML.CmdCancel,
                PrimaryButtonText = ML.CmdPermanentDelete,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = App.MainWindow!.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                (this.DataContext as MainWindowViewModel)?.PermanentDeleteCommand.Execute(items);
            }
        }
    }

    public class BoolToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is true ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class InvertBoolConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is bool b ? !b : value;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class InvertVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class ExpandGlyphConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is true ? "\uE96E" : "\uE970";
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
