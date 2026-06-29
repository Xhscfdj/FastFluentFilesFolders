using LRS.Services;
using LRS.UserControls;
using LRS.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using WinRT.Interop;

namespace LRS.Views
{
    public sealed partial class MiddleFilesView : Page
    {
        private readonly CommandBarFlyout _itemContextFlyout;
        private readonly CommandBarFlyout _baseContextFlyout;

        public MiddleFilesView()
        {
            InitializeComponent();
            this.DataContext = App.SharedViewModel;
            _itemContextFlyout = BuildItemContextFlyout();
            _baseContextFlyout = BuildBaseContextFlyout();
            FileGrid.ContextFlyout = _itemContextFlyout;
            FileGrid.BaseContextFlyout = _baseContextFlyout;
        }

        private void OnTreeDataGridItemInvoked(object sender, FileSystemNodeViewModel item)
        {
            (this.DataContext as MainWindowViewModel)?.OpenItem(item);
        }

        // Primary:   剪切, 复制, 粘贴, 重命名, 删除  (two-tone themed)
        // Secondary: 打开, 打开方式, 复制路径

        private CommandBarFlyout BuildItemContextFlyout()
        {
            var flyout = new CommandBarFlyout { AlwaysExpanded = true };

            flyout.PrimaryCommands.Add(ThemedBtn("剪切",   ThemedIconKey("Icon.Cut"),    OnCutClick));
            flyout.PrimaryCommands.Add(ThemedBtn("复制",   ThemedIconKey("Icon.Copy"),    OnCopyClick));
            flyout.PrimaryCommands.Add(ThemedBtn("粘贴",   ThemedIconKey("Icon.Paste"),   OnPasteClick));
            flyout.PrimaryCommands.Add(ThemedBtn("重命名", ThemedIconKey("Icon.Rename"),  OnRenameClick));
            flyout.PrimaryCommands.Add(ThemedBtn("删除",   ThemedIconKey("Icon.Delete"),  OnDeleteClick));
            flyout.PrimaryCommands.Add(RedBtn("彻底删除", "\uECC9", OnPermanentDeleteClick));

            flyout.SecondaryCommands.Add(PlainBtn("打开",     "\uE8E5", OnOpenClick));
            flyout.SecondaryCommands.Add(PlainBtn("打开方式", "\uE8E5", OnOpenWithClick));
            flyout.SecondaryCommands.Add(PlainBtn("复制路径", "\uE8C8", OnCopyPathClick));
            flyout.SecondaryCommands.Add(new AppBarSeparator());
			flyout.SecondaryCommands.Add(PlainBtn("属性", "\uE90F", OnPropertiesClick));
			flyout.SecondaryCommands.Add(new AppBarSeparator());
			flyout.SecondaryCommands.Add(BuildShowMoreOptionsBtn(isItemMenu: true));

			return flyout;
		}

		private CommandBarFlyout BuildBaseContextFlyout()
        {
            var flyout = new CommandBarFlyout { AlwaysExpanded = true };

            var newSubMenu = new MenuFlyout();
            newSubMenu.Items.Add(SubMenuBtn("文本文档", "\uE7C3", OnNewTextDocumentClick));
            newSubMenu.Items.Add(SubMenuBtn("快捷方式", "\uE71B", OnNewShortcutClick));
            newSubMenu.Items.Add(SubMenuBtn("文件",     "\uE7C3", OnNewFileClick));
            newSubMenu.Items.Add(new MenuFlyoutSeparator());
            newSubMenu.Items.Add(SubMenuBtn("Excel 表格", "\uE9F9", OnNewExcelClick));
            newSubMenu.Items.Add(SubMenuBtn("Word 文档",  "\uE89A", OnNewWordClick));
            newSubMenu.Items.Add(SubMenuBtn("PPT 演示",   "\uE8B4", OnNewPowerPointClick));

            var newBtn = new AppBarButton
            {
                Label = "新建",
                Icon = new FontIcon { Glyph = "\uE710", FontSize = 16 },
                Flyout = newSubMenu
            };
            flyout.SecondaryCommands.Add(newBtn);
            flyout.SecondaryCommands.Add(PlainBtn("新建文件夹", "\uE8F4", OnNewFolderClick));
            flyout.SecondaryCommands.Add(new AppBarSeparator());
			flyout.SecondaryCommands.Add(PlainBtn("粘贴", "\uE77F", OnPasteClick));
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
				Label = "显示更多选项",
				Icon = new FontIcon { Glyph = "\uE712", FontSize = 16 },
				Flyout = subMenu
			};
		}

		private void OnItemShowMoreOptionsOpening(object? sender, object e)
		{
			if (sender is not MenuFlyout flyout) return;
			flyout.Items.Clear();
			var item = FileGrid.SelectedItem;
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
					flyout.Items.Add(new MenuFlyoutItem { Text = "(无可用选项)", IsEnabled = false });
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
				flyout.Items.Add(new MenuFlyoutItem { Text = "(无法加载选项)", IsEnabled = false });
			}
		}

		// === Click handlers ===
        private void OnOpenClick(object sender, RoutedEventArgs e)
        {
            var item = FileGrid.SelectedItem;
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
}
