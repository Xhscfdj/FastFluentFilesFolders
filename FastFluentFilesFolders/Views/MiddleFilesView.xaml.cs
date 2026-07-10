using FastFluentFilesFolders.Extensions;
using FastFluentFilesFolders.Extensions.Interfaces;
using FastFluentFilesFolders.Helpers;
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
using Windows.System;
using WinRT.Interop;
using WinUI.TableView;

namespace FastFluentFilesFolders.Views
{
    public sealed partial class MiddleFilesView : Page
    {
        private readonly CommandBarFlyout _itemContextFlyout;
        private readonly CommandBarFlyout _baseContextFlyout;
        private readonly List<ICommandBarElement> _itemPluginItems = new();
        private readonly List<ICommandBarElement> _basePluginItems = new();
        private ObservableCollection<FileSystemNodeViewModel>? _watchedCollection;
        private static MultiLanguageStringsViewModel ML => App.ML;

        public MiddleFilesView()
        {
            InitializeComponent();
            RefreshHeaders();
            App.ML.PropertyChanged += (_, e) => RefreshHeaders();
            this.DataContext = App.SharedViewModel;
            _itemContextFlyout = BuildItemContextFlyout();
            _baseContextFlyout = BuildBaseContextFlyout();
            FileGrid.ContextRequested += OnFileGridContextRequested;
            FileGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnFileGridKeyDown), true);

            App.SharedViewModel.RenameFocusRequested += OnRenameFocusRequested;

            var copyAccel = new KeyboardAccelerator { Key = VirtualKey.C, Modifiers = VirtualKeyModifiers.Control };
            copyAccel.Invoked += (_, args) => { args.Handled = true; OnCopyClick(null, null); };
            FileGrid.KeyboardAccelerators.Add(copyAccel);

            var pasteAccel = new KeyboardAccelerator { Key = VirtualKey.V, Modifiers = VirtualKeyModifiers.Control };
            pasteAccel.Invoked += (_, args) => { args.Handled = true; OnPasteClick(null, null); };
            FileGrid.KeyboardAccelerators.Add(pasteAccel);

            var cutAccel = new KeyboardAccelerator { Key = VirtualKey.X, Modifiers = VirtualKeyModifiers.Control };
            cutAccel.Invoked += (_, args) => { args.Handled = true; OnCutClick(null, null); };
            FileGrid.KeyboardAccelerators.Add(cutAccel);

            this.DataContextChanged += (s, e) =>
            {
                if (this.DataContext is MainWindowViewModel vm)
                {
                    vm.PropertyChanged += OnViewModelPropertyChanged;
                    UpdateGroupedSource(vm);
                }
            };
            if (this.DataContext is MainWindowViewModel currentVm)
            {
                currentVm.PropertyChanged += OnViewModelPropertyChanged;
                UpdateGroupedSource(currentVm);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentFolderContent) ||
                e.PropertyName == nameof(MainWindowViewModel.IsCurrentFolderSpecial))
            {
                if (sender is MainWindowViewModel vm)
                    UpdateGroupedSource(vm);
            }
        }

        private void UpdateGroupedSource(MainWindowViewModel vm)
        {
            if (_watchedCollection != null)
                _watchedCollection.CollectionChanged -= OnCurrentFolderCollectionChanged;

            var items = vm.CurrentFolderContent ?? new();
            _watchedCollection = items;
            _watchedCollection.CollectionChanged += OnCurrentFolderCollectionChanged;

            FileGrid.UpdateSource(items, vm.IsCurrentFolderSpecial);
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
                (this.DataContext as MainWindowViewModel)?.OpenItem(item);
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
            if (args.TryGetPosition(sender, out var position))
            {
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
                    RebuildPluginItems(_itemContextFlyout, _itemPluginItems, item);
                    _itemContextFlyout.ShowAt(row, new FlyoutShowOptions { Position = position });
                }
                else
                {
                    RebuildPluginItems(_baseContextFlyout, _basePluginItems, null);
                    _baseContextFlyout.ShowAt(sender, new FlyoutShowOptions { Position = position });
                }
                args.Handled = true;
            }
        }

        public void RefreshHeaders()
        {
            ColName.Header = ML.ColumnName;
            ColModifiedDate.Header = ML.ColumnModifiedDate;
            ColCreatedDate.Header = ML.ColumnCreatedDate;
            ColSize.Header = ML.ColumnSize;
        }

        private CommandBarFlyout BuildItemContextFlyout()
        {
            var flyout = new CommandBarFlyout { AlwaysExpanded = true };

            flyout.PrimaryCommands.Add(ThemedBtn(ML.CmdCut,   ThemedIconKey("Icon.Cut"),    OnCutClick));
            flyout.PrimaryCommands.Add(ThemedBtn(ML.CmdCopy,   ThemedIconKey("Icon.Copy"),    OnCopyClick));
            flyout.PrimaryCommands.Add(ThemedBtn(ML.CmdPaste,   ThemedIconKey("Icon.Paste"),   OnPasteClick));
            flyout.PrimaryCommands.Add(ThemedBtn(ML.CmdRename, ThemedIconKey("Icon.Rename"),  OnRenameClick));
            flyout.PrimaryCommands.Add(ThemedBtn(ML.CmdDelete,   ThemedIconKey("Icon.Delete"),  OnDeleteClick));
            flyout.PrimaryCommands.Add(RedBtn(ML.CmdPermanentDelete, "\uECC9", OnPermanentDeleteClick));

            flyout.SecondaryCommands.Add(PlainBtn(ML.CmdOpen,     "\uE8E5", OnOpenClick));
            flyout.SecondaryCommands.Add(PlainBtn(ML.CmdOpenWith, "\uE8E5", OnOpenWithClick));
            flyout.SecondaryCommands.Add(PlainBtn(ML.CmdCopyPath, "\uE8C8", OnCopyPathClick));
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
                Icon = new FontIcon { Glyph = "\uE710", FontSize = 16 },
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
            PopulateNativeContextMenu(flyout, item.FullPath, hwnd, _itemContextFlyout, item);
        }

        private void OnBaseShowMoreOptionsOpening(object? sender, object e)
        {
            if (sender is not MenuFlyout flyout) return;
            flyout.Items.Clear();
            var vm = this.DataContext as MainWindowViewModel;
            var path = vm?.SelectedFolder?.FullPath ?? vm?.CurrentBreadcrumbPath;
            if (string.IsNullOrEmpty(path)) return;
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow!);
            PopulateNativeContextMenu(flyout, path, hwnd, _baseContextFlyout, null);
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
            _itemContextFlyout.Hide();
            foreach (var item in GetSelectedItems())
                _ = item.RefreshAsync();
        }

        private void FinishBaseOp()
        {
            _baseContextFlyout.Hide();
        }

        // === Click handlers ===
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
            if (items.Count > 0)
                (this.DataContext as MainWindowViewModel)?.OpenWithCommand.Execute(items[0]);
        }
        private void OnCutClick(object sender, RoutedEventArgs e)
        {
            FinishItemOp();
            var items = GetSelectedItems();
            if (items.Count > 0)
                (this.DataContext as MainWindowViewModel)?.CutCommand.Execute(items);
        }
        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            FinishItemOp();
            var items = GetSelectedItems();
            if (items.Count > 0)
                (this.DataContext as MainWindowViewModel)?.CopyCommand.Execute(items);
        }
        private void OnPasteClick(object sender, RoutedEventArgs e)
        {
            _itemContextFlyout.Hide();
            _baseContextFlyout.Hide();
            (this.DataContext as MainWindowViewModel)?.PasteCommand.Execute(null);
        }
        private void OnRenameClick(object sender, RoutedEventArgs e)
        {
            _itemContextFlyout.Hide();
            var items = GetSelectedItems();
            if (items.Count == 0) return;
            var item = items[0];
            (this.DataContext as MainWindowViewModel)?.RenameCommand.Execute(item);
        }

        private void OnRenameFocusRequested(FileSystemNodeViewModel item)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                var container = FileGrid.ContainerFromItem(item);
                if (container is TableViewRow row && row.ContentTemplateRoot is UIElement root)
                {
                    var textBox = FindVisualChild<TextBox>(root);
                    if (textBox != null)
                    {
                        textBox.Focus(FocusState.Programmatic);
                        textBox.SelectAll();
                    }
                }
            });
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
                }
            }
        }

        private void CommitInlineRename(TextBox tb, FileSystemNodeViewModel item)
        {
            var newName = tb.Text.Trim();
            item.IsRenaming = false;
            if (!string.IsNullOrEmpty(newName) && newName != item.Name)
            {
                item.Name = newName;
                _ = (this.DataContext as MainWindowViewModel)!.CommitRenameAsync(item, newName);
                _ = item.RefreshAsync();
                if (this.DataContext is MainWindowViewModel vm)
                    UpdateGroupedSource(vm);
            }
        }
        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            _itemContextFlyout.Hide();
            var items = GetSelectedItems();
            if (items.Count > 0)
                (this.DataContext as MainWindowViewModel)?.DeleteCommand.Execute(items);
        }
        private void OnPermanentDeleteClick(object sender, RoutedEventArgs e)
        {
            _itemContextFlyout.Hide();
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
        private void OnPropertiesClick(object sender, RoutedEventArgs e)
        {
            FinishItemOp();
            var items = GetSelectedItems();
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
            if (e.Handled) return;

            var isCtrlDown = ((int)Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & 1) != 0;
            var isAltDown = ((int)Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & 1) != 0;

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
            else if (isAltDown && !isCtrlDown && e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                OnPropertiesClick(sender, e);
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
        }

        private async Task PermanentDeleteWithConfirmAsync()
        {
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
