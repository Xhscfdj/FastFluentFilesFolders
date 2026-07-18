using FastFluentFilesFolders.Helpers;
using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
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

        public void UpdateSource(ObservableCollection<FileSystemNodeViewModel> items, bool grouped)
        {
            if (_groupedSource != null)
            {
                _groupedSource.FlatListChanged -= OnFlatListChanged;
                _groupedSource = null;
            }

            if (grouped)
            {
                var source = new GroupedFileList();
                _groupedSource = source;
                source.FlatListChanged += OnFlatListChanged;
                source.SetItems(items, grouped);
                ItemsSource = source;
            }
            else
            {
                ItemsSource = items;
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
                if (isAltDown)
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
    }
}
