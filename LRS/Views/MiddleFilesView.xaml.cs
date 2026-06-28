using LRS.UserControls;
using LRS.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

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

            flyout.SecondaryCommands.Add(PlainBtn("打开",     "\uE8E5", OnOpenClick));
            flyout.SecondaryCommands.Add(PlainBtn("打开方式", "\uE8E5", OnOpenClick));
            flyout.SecondaryCommands.Add(PlainBtn("复制路径", "\uE8C8", OnCopyPathClick));

            return flyout;
        }

        private CommandBarFlyout BuildBaseContextFlyout()
        {
            var flyout = new CommandBarFlyout { AlwaysExpanded = true };

            flyout.PrimaryCommands.Add(ThemedBtn("新建文件夹",   ThemedIconKey("Icon.Folder"),   OnNewFolderClick));
            flyout.PrimaryCommands.Add(ThemedBtn("新建文本文档", ThemedIconKey("Icon.Document"), OnNewTextDocumentClick));

            flyout.SecondaryCommands.Add(new AppBarSeparator());
            flyout.SecondaryCommands.Add(PlainBtn("粘贴", "\uE77F", OnPasteClick));

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

        // === Click handlers ===
        private void OnOpenClick(object sender, RoutedEventArgs e)
        {
            var item = FileGrid.SelectedItem;
            if (item == null) return;
            (this.DataContext as MainWindowViewModel)?.OpenItem(item);
        }
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
        private void OnCopyPathClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.CopyPathCommand.Execute(FileGrid.SelectedItem);
        private void OnNewFolderClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.NewFolderCommand.Execute(null);
        private void OnNewTextDocumentClick(object sender, RoutedEventArgs e)
            => (this.DataContext as MainWindowViewModel)?.NewTextDocumentCommand.Execute(null);
    }
}
