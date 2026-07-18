using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using FastFluentFilesFolders.ViewModels;
using FastFluentFilesFolders.Extensions;
using FastFluentFilesFolders.Extensions.Interfaces;

namespace FastFluentFilesFolders.UserControls
{
    public sealed partial class LRSBreadcrumb : UserControl, INotifyPropertyChanged
    {
        public static readonly DependencyProperty CurrentPathProperty =
            DependencyProperty.Register(nameof(CurrentPath), typeof(string), typeof(LRSBreadcrumb),
                new PropertyMetadata(null, OnCurrentPathChanged));

        public static readonly DependencyProperty NavigateCommandProperty =
            DependencyProperty.Register(nameof(NavigateCommand), typeof(ICommand), typeof(LRSBreadcrumb),
                new PropertyMetadata(null, OnNavigateCommandChanged));

        public static readonly DependencyProperty NavigateSubCommandProperty =
            DependencyProperty.Register(nameof(NavigateSubCommand), typeof(ICommand), typeof(LRSBreadcrumb),
                new PropertyMetadata(null, OnNavigateCommandChanged));

        public static readonly DependencyProperty GoBackCommandProperty =
            DependencyProperty.Register(nameof(GoBackCommand), typeof(ICommand), typeof(LRSBreadcrumb),
                new PropertyMetadata(null));

        public static readonly DependencyProperty GoForwardCommandProperty =
            DependencyProperty.Register(nameof(GoForwardCommand), typeof(ICommand), typeof(LRSBreadcrumb),
                new PropertyMetadata(null));

        public static readonly DependencyProperty GoUpCommandProperty =
            DependencyProperty.Register(nameof(GoUpCommand), typeof(ICommand), typeof(LRSBreadcrumb),
                new PropertyMetadata(null));

        public static readonly DependencyProperty HomePathProperty =
            DependencyProperty.Register(nameof(HomePath), typeof(string), typeof(LRSBreadcrumb),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty CanGoBackProperty =
            DependencyProperty.Register(nameof(CanGoBack), typeof(bool), typeof(LRSBreadcrumb),
                new PropertyMetadata(false));

        public static readonly DependencyProperty CanGoForwardProperty =
            DependencyProperty.Register(nameof(CanGoForward), typeof(bool), typeof(LRSBreadcrumb),
                new PropertyMetadata(false));

        public double controlHeight = 30.0;
        public string CurrentPath
        {
            get => (string)GetValue(CurrentPathProperty);
            set => SetValue(CurrentPathProperty, value);
        }

        public ICommand NavigateCommand
        {
            get => (ICommand)GetValue(NavigateCommandProperty);
            set => SetValue(NavigateCommandProperty, value);
        }

        public ICommand NavigateSubCommand
        {
            get => (ICommand)GetValue(NavigateSubCommandProperty);
            set => SetValue(NavigateSubCommandProperty, value);
        }

        public ICommand GoBackCommand
        {
            get => (ICommand)GetValue(GoBackCommandProperty);
            set => SetValue(GoBackCommandProperty, value);
        }

        public ICommand GoForwardCommand
        {
            get => (ICommand)GetValue(GoForwardCommandProperty);
            set => SetValue(GoForwardCommandProperty, value);
        }

        public ICommand GoUpCommand
        {
            get => (ICommand)GetValue(GoUpCommandProperty);
            set => SetValue(GoUpCommandProperty, value);
        }

        public string HomePath
        {
            get => (string)GetValue(HomePathProperty);
            set => SetValue(HomePathProperty, value);
        }

        public bool CanGoBack
        {
            get => (bool)GetValue(CanGoBackProperty);
            set => SetValue(CanGoBackProperty, value);
        }

        public bool CanGoForward
        {
            get => (bool)GetValue(CanGoForwardProperty);
            set => SetValue(CanGoForwardProperty, value);
        }

        public ObservableCollection<BreadcrumbSegment> Segments { get; } = new();
        public ICommand CopyPathCommand { get; }

        private Compositor _compositor;
        private CancellationTokenSource? _searchCts;
        private readonly ObservableCollection<SearchResultItem> _searchResults = new();
        private Flyout _searchFlyout;
        private TextBox _searchTextBox;
        private ListView _searchResultsList;
        private TextBlock _searchStatusText;
        private Grid _searchContentGrid;

        public LRSBreadcrumb()
        {
            this.InitializeComponent();
            CopyPathCommand = new RelayCommand(ExecuteCopyPath);
            BuildSearchFlyout();

            this.Loaded += (s, e) =>
            {
                _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
                PopulatePluginToolbar();
            };
        }

        private void PopulatePluginToolbar()
        {
            var plugins = App.PluginManager?.GetToolbarPlugins();
            PluginToolbarItems.Items.Clear();
            if (plugins == null) return;

            var toolbarItems = new List<object>();
            bool first = true;
            foreach (var plugin in plugins)
            {
                foreach (var item in plugin.GetToolbarItems())
                {
                    if (first && toolbarItems.Count > 0)
                    {
                        var sep = new AppBarSeparator { Margin = new Thickness(4, 0, 4, 0) };
                        toolbarItems.Add(sep);
                        first = false;
                    }

                    var btn = new Button
                    {
                        Width = 32, Height = 32,
                        Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                        Padding = new Thickness(4),
                        Tag = item
                    };
                    Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(btn, item.ToolTip ?? item.Header);
                    if (item.Command != null)
                        btn.Command = item.Command;
                    if (item.CommandParameter != null)
                        btn.CommandParameter = item.CommandParameter;
                    if (item.IconGlyph != null)
                        btn.Content = new FontIcon { Glyph = item.IconGlyph, FontSize = 14 };

                    toolbarItems.Add(btn);
                }
            }

            foreach (var tb in toolbarItems)
                PluginToolbarItems.Items.Add(tb);
        }

        private void BuildSearchFlyout()
        {
            _searchTextBox = new TextBox { PlaceholderText = "搜索当前目录...", Margin = new Thickness(8) };
            _searchTextBox.TextChanged += OnSearchTextChanged;
            _searchTextBox.KeyDown += OnSearchTextBoxKeyDown;

            _searchResultsList = new ListView
            {
                BorderThickness = new Thickness(0),
                Margin = new Thickness(4, 0, 4, 4),
                SelectionMode = ListViewSelectionMode.Single,
                IsItemClickEnabled = true
            };
            _searchResultsList.ItemClick += OnSearchResultItemClick;

            var xaml = """
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <Grid Height="32" Background="Transparent">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="24"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <FontIcon Grid.Column="0" Glyph="{Binding IconGlyph}" FontSize="14"
                                  VerticalAlignment="Center" Margin="4,0,0,0"
                                  Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
                        <TextBlock Grid.Column="1" Text="{Binding Name}"
                                   VerticalAlignment="Center" Margin="6,0,0,0"
                                   TextTrimming="CharacterEllipsis" FontSize="13"/>
                        <TextBlock Grid.Column="2" Text="{Binding PathPreview}"
                                   VerticalAlignment="Center" Margin="6,0,4,0" FontSize="11"
                                   Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                                   TextTrimming="CharacterEllipsis"/>
                    </Grid>
                </DataTemplate>
                """;
            _searchResultsList.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);

            _searchStatusText = new TextBlock
            {
                Text = "输入关键词开始搜索", Margin = new Thickness(12), FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            _searchStatusText.Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];

            _searchContentGrid = new Grid { MaxHeight = 420 };
            _searchContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            _searchContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(_searchTextBox, 0);
            Grid.SetRow(_searchResultsList, 1);
            Grid.SetRow(_searchStatusText, 1);
            _searchContentGrid.Children.Add(_searchTextBox);
            _searchContentGrid.Children.Add(_searchResultsList);
            _searchContentGrid.Children.Add(_searchStatusText);

            var searchFlyoutStyle = new Style(typeof(FlyoutPresenter));
            searchFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.MaxWidthProperty, 9999.0));
            _searchFlyout = new Flyout
            {
                Content = _searchContentGrid,
                FlyoutPresenterStyle = searchFlyoutStyle
            };
            _searchFlyout.Closing += OnSearchFlyoutClosing;
            FlyoutBase.SetAttachedFlyout(AddressBarArea, _searchFlyout);
        }

        private void OnAddressBarAreaPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_isEditing) return;
            if (!IsButtonOrDescendant(e.OriginalSource as DependencyObject))
            {
                EnterEditMode();
                e.Handled = true;
            }
        }

        private static bool IsButtonOrDescendant(DependencyObject element)
        {
            while (element != null)
            {
                if (element is Button) return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        private void OnPathTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (!_isEditing)
                EnterEditMode();
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing == value) return;
                _isEditing = value;
                OnPropertyChanged(nameof(IsEditing));
                if (value)
                    EnterEditMode();
                else
                    ExitEditMode();
            }
        }

        private void EnterEditMode()
        {
            if (_isEditing) return;
            _isEditing = true;
            PathTextBox.Visibility = Visibility.Visible;
            PathTextBox.IsReadOnly = false;
            PathTextBox.BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
            PathTextBox.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextBoxBackgroundThemeBrush"];
            BreadcrumbScrollViewer.Visibility = Visibility.Collapsed;
            PathTextBox.SetBinding(TextBox.TextProperty, new Microsoft.UI.Xaml.Data.Binding
            {
                Source = this,
                Path = new PropertyPath(nameof(CurrentPath)),
                Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay
            });
            PathTextBox.Focus(FocusState.Programmatic);
            PathTextBox.SelectAll();
        }

        private void ExitEditMode()
        {
            PathTextBox.Visibility = Visibility.Collapsed;
            PathTextBox.IsReadOnly = true;
            PathTextBox.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            PathTextBox.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            BreadcrumbScrollViewer.Visibility = Visibility.Visible;
        }

        private void OnPathTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                var path = PathTextBox.Text;
                if (!string.IsNullOrWhiteSpace(path) &&
                    (Directory.Exists(path) || ViewModels.ArchiveHelper.IsArchiveVirtualPath(path)))
                {
                    NavigateCommand?.Execute(path);
                }
                IsEditing = false;
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                IsEditing = false;
                e.Handled = true;
            }
        }

        private void OnPathTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            IsEditing = false;
        }

        private static void OnCurrentPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LRSBreadcrumb)d;
            control.UpdateSegments(e.NewValue as string);
        }

        private static void OnNavigateCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LRSBreadcrumb)d;
            control.UpdateSegments(control.CurrentPath);
        }

        public void RefreshSegments()
        {
            UpdateSegments(CurrentPath);
        }

        private void UpdateSegments(string path)
        {
            Segments.Clear();
            if (string.IsNullOrEmpty(path))
                return;

            var parts = ParsePath(path);
            for (int i = 0; i < parts.Length; i++)
            {
                var displayName = parts[i];
                string fullPath;
                if (i == 0 && path.StartsWith("\\\\"))
                {
                    fullPath = string.Join("\\", parts.Take(2));
                    if (displayName == fullPath)
                        continue;
                }
                else if (i == 0)
                {
                    fullPath = parts[0] + "\\";
                }
                else
                {
                    var rootPrefix = path.StartsWith("\\\\")
                        ? string.Join("\\", parts.Take(2))
                        : parts[0] + "\\";
                    var remaining = parts.Skip(path.StartsWith("\\\\") ? 2 : 1).Take(i - (path.StartsWith("\\\\") ? 1 : 0));
                    fullPath = path.StartsWith("\\\\")
                        ? rootPrefix + "\\" + string.Join("\\", remaining) + "\\" + displayName
                        : parts[0] + "\\" + string.Join("\\", parts.Skip(1).Take(i));
                }

                fullPath = fullPath.TrimEnd('\\');
                if (fullPath.Length == 2 && fullPath[1] == ':')
                    fullPath += "\\";

                var segment = new BreadcrumbSegment
                {
                    DisplayName = displayName,
                    FullPath = fullPath,
                    IsLast = (i == parts.Length - 1),
                    NavigateCommand = NavigateCommand,
                    NavigateSubCommand = NavigateSubCommand
                };
                Segments.Add(segment);
            }
        }

        private string[] ParsePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Array.Empty<string>();

            if (path.Length == 3 && path.EndsWith(":\\"))
                return new[] { path.TrimEnd('\\') };

            if (path.StartsWith("\\\\"))
                return path.TrimEnd('\\').Split('\\');

            return path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        }

        private void OnChevronClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not BreadcrumbSegment segment)
                return;

            var visual = ElementCompositionPreview.GetElementVisual(btn);
            if (_compositor != null)
            {
                float currentAngle = (float)visual.RotationAngleInDegrees;
                float targetAngle = (Math.Abs(currentAngle) < 1) ? 180f : 0f;

                var animation = _compositor.CreateScalarKeyFrameAnimation();
                animation.Duration = TimeSpan.FromMilliseconds(200);
                var easingFunction = _compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.25f, 0.1f),
                    new System.Numerics.Vector2(0.25f, 1.0f));
                animation.InsertKeyFrame(1.0f, targetAngle, easingFunction);
                visual.StartAnimation("RotationAngleInDegrees", animation);
            }
        }

        private async void OnFlyoutOpening(object sender, object e)
        {
            if (sender is not MenuFlyout flyout) return;
            var btn = flyout.Target as Button;
            if (btn?.Tag is not BreadcrumbSegment segment) return;

            flyout.Items.Clear();
            flyout.Items.Add(new MenuFlyoutItem { Text = "加载中...", IsEnabled = false });

            await PopulateSubFolderFlyout(flyout, segment.FullPath);
        }

        private async Task PopulateSubFolderFlyout(MenuFlyout flyout, string path)
        {
            try
            {
                var subDirs = await Task.Run(() => FileSystemNodeViewModel.SafeGetDirs(path));
                DispatcherQueue.TryEnqueue(() =>
                {
                    flyout.Items.Clear();
                    foreach (var dir in subDirs)
                    {
                        var dirName = Path.GetFileName(dir);
                        var item = new MenuFlyoutItem
                        {
                            Text = dirName,
                            Tag = dir
                        };
                        item.Click += OnSubFolderItemClick;
                        flyout.Items.Add(item);
                    }

                    if (flyout.Items.Count == 0)
                    {
                        flyout.Items.Add(new MenuFlyoutItem
                        {
                            Text = "（空文件夹）",
                            IsEnabled = false
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LRSBreadcrumb] PopulateSubFolderFlyout error: {ex.Message}");
                DispatcherQueue.TryEnqueue(() =>
                {
                    flyout.Items.Clear();
                    flyout.Items.Add(new MenuFlyoutItem
                    {
                        Text = "（无法访问）",
                        IsEnabled = false
                    });
                });
            }
        }

        private void OnSubFolderItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string path)
            {
                NavigateSubCommand?.Execute(path);
            }
        }

        private void OnFlyoutClosing(object sender, object e)
        {
            if (sender is not MenuFlyout flyout) return;

            if (flyout.Target is Button btn && _compositor != null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(btn);
                var animation = _compositor.CreateScalarKeyFrameAnimation();
                animation.Duration = TimeSpan.FromMilliseconds(200);
                var easingFunction = _compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.25f, 0.1f),
                    new System.Numerics.Vector2(0.25f, 1.0f));
                animation.InsertKeyFrame(1.0f, 0f, easingFunction);
                visual.StartAnimation("RotationAngleInDegrees", animation);
            }

            flyout.Items.Clear();
        }

        private void ExecuteCopyPath()
        {
            if (!string.IsNullOrEmpty(CurrentPath))
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(CurrentPath);
                Clipboard.SetContent(dataPackage);
            }
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(CurrentPath))
                NavigateCommand?.Execute(CurrentPath);
        }

        private void OnSearchButtonClick(object sender, RoutedEventArgs e)
        {
            _searchContentGrid.Width = AddressBarArea.ActualWidth;
            _searchResultsList.ItemsSource = _searchResults;
            _searchTextBox.Text = string.Empty;
            _searchResults.Clear();
            _searchStatusText.Visibility = Visibility.Visible;
            _searchResultsList.Visibility = Visibility.Collapsed;

            FlyoutBase.ShowAttachedFlyout(AddressBarArea);
        }

        private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;
            var query = _searchTextBox.Text.Trim();

            if (query.Length == 0)
            {
                _searchResults.Clear();
                _searchStatusText.Text = "输入关键词开始搜索";
                _searchStatusText.Visibility = Visibility.Visible;
                _searchResultsList.Visibility = Visibility.Collapsed;
                return;
            }

            _searchStatusText.Text = "搜索中...";
            _searchStatusText.Visibility = Visibility.Visible;
            _searchResultsList.Visibility = Visibility.Collapsed;

            var currentPath = CurrentPath;
            if (string.IsNullOrEmpty(currentPath) || !Directory.Exists(currentPath))
                return;

            try
            {
                var results = await Task.Run(() =>
                {
                    var list = new List<SearchResultItem>();
                    try
                    {
                        foreach (var dir in Directory.EnumerateDirectories(currentPath, "*", SearchOption.AllDirectories))
                        {
                            if (token.IsCancellationRequested) break;
                            var name = Path.GetFileName(dir);
                            if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var relative = dir.Substring(currentPath.Length).TrimStart('\\');
                                list.Add(new SearchResultItem { Name = name, FullPath = dir, IsDirectory = true, PathPreview = relative });
                            }
                        }
                        foreach (var file in Directory.EnumerateFiles(currentPath, "*", SearchOption.AllDirectories))
                        {
                            if (token.IsCancellationRequested) break;
                            var name = Path.GetFileName(file);
                            if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var relative = file.Substring(currentPath.Length).TrimStart('\\');
                                list.Add(new SearchResultItem { Name = name, FullPath = file, IsDirectory = false, PathPreview = relative });
                            }
                            if (list.Count >= 200) break;
                        }
                    }
                    catch { }
                    return list;
                }, token);

                if (token.IsCancellationRequested) return;

                DispatcherQueue.TryEnqueue(() =>
                {
                    _searchResults.Clear();
                    foreach (var r in results)
                        _searchResults.Add(r);

                    if (_searchResults.Count == 0)
                    {
                        _searchStatusText.Text = "未找到结果";
                        _searchStatusText.Visibility = Visibility.Visible;
                        _searchResultsList.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        _searchStatusText.Visibility = Visibility.Collapsed;
                        _searchResultsList.Visibility = Visibility.Visible;
                    }
                });
            }
            catch (OperationCanceledException) { }
        }

        private void OnSearchTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                _searchFlyout.Hide();
            }
        }

        private void OnSearchFlyoutClosing(object sender, object e)
        {
            _searchCts?.Cancel();
        }

        private void OnSearchResultItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SearchResultItem result)
            {
                _searchFlyout.Hide();
                if (result.IsDirectory)
                    NavigateCommand?.Execute(result.FullPath);
                else
                    FastFluentFilesFolders.ViewModels.MainWindowViewModel.OpenWithDefaultProgram(result.FullPath);
            }
        }

        private class RelayCommand : ICommand
        {
            private readonly Action _execute;
            private readonly Func<bool> _canExecute;
            public RelayCommand(Action execute, Func<bool> canExecute = null)
            {
                _execute = execute;
                _canExecute = canExecute;
            }
            public event EventHandler CanExecuteChanged;
            public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
            public void Execute(object parameter) => _execute();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class LastToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool isLast && isLast) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class BreadcrumbSegment
    {
        public string DisplayName { get; set; }
        public string FullPath { get; set; }
        public bool IsLast { get; set; }
        public ICommand NavigateCommand { get; set; }
        public ICommand NavigateSubCommand { get; set; }
    }

    public class SearchResultItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public string PathPreview { get; set; } = string.Empty;
        public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE8A5";
    }
}
