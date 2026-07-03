using LRS.Helpers;
using LRS.Services;
using LRS.UserControls;
using LRS.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using WinRT.Interop;
using WinUI.TableView;

namespace LRS.Views
{
    public sealed partial class MiddleFilesView : Page
    {
        private readonly CommandBarFlyout _itemContextFlyout;
        private readonly CommandBarFlyout _baseContextFlyout;
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
            var items = vm.CurrentFolderContent ?? new();
            FileGrid.UpdateSource(items, vm.IsCurrentFolderSpecial);
            if (FileGrid.ItemsSource is GroupedFileList list)
            {
                list.SetDispatcher(DispatcherQueue.GetForCurrentThread());
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
                    FileGrid.SelectedItem = item;
                    _itemContextFlyout.ShowAt(row, new FlyoutShowOptions { Position = position });
                }
                else
                {
                    _baseContextFlyout.ShowAt(sender, new FlyoutShowOptions { Position = position });
                }
                args.Handled = true;
            }
        }

        // Primary:   剪切, 复制, 粘贴, 重命名, 删除  (two-tone themed)
        // Secondary: 打开, 打开方式, 复制路径

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
			PopulateNativeContextMenu(flyout, item.FullPath, hwnd);
		}

		private void OnBaseShowMoreOptionsOpening(object? sender, object e)
		{
			if (sender is not MenuFlyout flyout) return;
			flyout.Items.Clear();
			var vm = this.DataContext as MainWindowViewModel;
			var path = vm?.SelectedFolder?.FullPath ?? vm?.CurrentBreadcrumbPath;
			if (string.IsNullOrEmpty(path)) return;
			var hwnd = WindowNative.GetWindowHandle(App.MainWindow!);
			PopulateNativeContextMenu(flyout, path, hwnd);
		}

		private static void PopulateNativeContextMenu(MenuFlyout flyout, string path, IntPtr hwnd)
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

		// === Click handlers ===
        private void OnOpenClick(object sender, RoutedEventArgs e)
        {
            var item = FileGrid.SelectedItem as FileSystemNodeViewModel;
            if (item == null) return;
            (this.DataContext as MainWindowViewModel)?.OpenItem(item);
        }
        private void OnOpenWithClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.OpenWithCommand.Execute(FileGrid.SelectedItem);
        private void OnCutClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.CutCommand.Execute(FileGrid.SelectedItem);
        private void OnCopyClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.CopyCommand.Execute(FileGrid.SelectedItem);
        private void OnPasteClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.PasteCommand.Execute(null);
        private void OnRenameClick(object sender, RoutedEventArgs e)
        {
            _itemContextFlyout.Hide();
            (this.DataContext as MainWindowViewModel)?.RenameCommand.Execute(FileGrid.SelectedItem);
        }
        private void OnDeleteClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.DeleteCommand.Execute(FileGrid.SelectedItem);
        private void OnPermanentDeleteClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.PermanentDeleteCommand.Execute(FileGrid.SelectedItem);
        private void OnCopyPathClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.CopyPathCommand.Execute(FileGrid.SelectedItem);
        private void OnPropertiesClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.PropertiesCommand.Execute(FileGrid.SelectedItem);
        private void OnNewFolderClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.NewFolderCommand.Execute(null);
        private void OnNewTextDocumentClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.NewTextDocumentCommand.Execute(null);
        private void OnNewShortcutClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.NewShortcutCommand.Execute(null);
        private void OnNewFileClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.NewFileCommand.Execute(null);
        private void OnNewExcelClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.NewExcelSpreadsheetCommand.Execute(null);
        private void OnNewWordClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.NewWordDocumentCommand.Execute(null);
        private void OnNewPowerPointClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.NewPowerPointPresentationCommand.Execute(null);
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
