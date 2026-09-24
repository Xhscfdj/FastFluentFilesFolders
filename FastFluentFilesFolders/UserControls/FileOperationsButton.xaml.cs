using FastFluentFilesFolders.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FastFluentFilesFolders.UserControls
{
    public sealed partial class FileOperationsButton : UserControl
    {
        private ObservableCollection<FileOperationItem>? _items;
        private FileOperationItem? _latestItem;
        private const string DefaultIconGlyph = "\uE8B7";

        public FileOperationsButton()
        {
            InitializeComponent();
            App.ML.PropertyChanged += OnMLPropertyChanged;
            ApplyLocalizedStrings();
        }

        public void SetItems(ObservableCollection<FileOperationItem> items)
        {
            // 工具栏重建（回收站切换/语言切换）时会再次调用：同一集合不要重复订阅
            if (ReferenceEquals(_items, items)) return;

            if (_items != null)
                _items.CollectionChanged -= OnItemsChanged;

            _items = items;
            OpsList.ItemsSource = items;
            items.CollectionChanged += OnItemsChanged;
            UpdateCount();
            UpdateLatestItem();
        }

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateCount();
            UpdateLatestItem();
        }

        private void UpdateLatestItem()
        {
            var latest = _items != null && _items.Count > 0 ? _items[0] : null;
            if (ReferenceEquals(latest, _latestItem))
            {
                RefreshIslandIcon();
                return;
            }

            if (_latestItem != null)
                _latestItem.PropertyChanged -= OnItemPropertyChanged;
            _latestItem = latest;
            if (_latestItem != null)
                _latestItem.PropertyChanged += OnItemPropertyChanged;
            RefreshIslandIcon();
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileOperationItem.State) ||
                e.PropertyName == nameof(FileOperationItem.IconGlyph))
                RefreshIslandIcon();
        }

        private void RefreshIslandIcon()
        {
            IslandIcon.Glyph = _latestItem?.DisplayIconGlyph ?? DefaultIconGlyph;
        }

        private void UpdateCount()
        {
            if (_items == null) return;
            RootBtn.Visibility = Visibility.Visible;
            var count = _items.Count;
            CountText.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CountText.Text = count.ToString();
            EmptyText.Visibility = count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        public void RemoveItem(FileOperationItem item)
        {
            _items?.Remove(item);
        }

        public void ClearCompleted()
        {
            if (_items == null) return;
            for (var i = _items.Count - 1; i >= 0; i--)
                if (_items[i].IsCompleted) _items.RemoveAt(i);
        }

        private void CloseItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FileOperationItem item }) RemoveItem(item);
        }

        private void ClearCompleted_Click(object sender, RoutedEventArgs e)
        {
            ClearCompleted();
        }

        private void ApplyLocalizedStrings()
        {
            HeaderTitleText.Text = App.ML.FileOperationsTitle;
            ClearCompletedBtn.Content = App.ML.ClearCompleted;
            EmptyText.Text = App.ML.NoFileOperations;
        }

        private void OnMLPropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyLocalizedStrings();
    }
}