using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using FastFluentFilesFolders.ViewModels;
using FastFluentFilesFolders.Extensions;
using FastFluentFilesFolders.Extensions.Interfaces;
using FastFluentFilesFolders.Services;

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
        private bool _isSearchMode;

        public LRSBreadcrumb()
        {
            this.InitializeComponent();
            CopyPathCommand = new RelayCommand(ExecuteCopyPath);

            ToolTipService.SetToolTip(BackButton, App.ML.TooltipBack);
            ToolTipService.SetToolTip(ForwardButton, App.ML.TooltipForward);
            ToolTipService.SetToolTip(UpButton, App.ML.TooltipUp);
            ToolTipService.SetToolTip(RefreshButton, App.ML.TooltipRefresh);
            ToolTipService.SetToolTip(HomeButton, App.ML.TooltipHome);
            ToolTipService.SetToolTip(SearchButton, App.ML.TooltipSearch);
            ToolTipService.SetToolTip(CopyPathButton, App.ML.TooltipCopyPath);

            // Backspace 触发回退（地址栏编辑或文本框聚焦时不拦截）
            this.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnBreadcrumbKeyDown), true);

            this.Loaded += (s, e) =>
            {
                _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
                PopulatePluginToolbar();
            };

            App.LocalizationService.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LocalizationService.CurrentLanguage))
                    RefreshTooltips();
            };

            SearchTextBox.PlaceholderText = App.ML.SearchPlaceholder;
            SearchTextBox.SetBinding(TextBox.TextProperty, new Binding
            {
                Source = App.SharedViewModel,
                Path = new PropertyPath(nameof(MainWindowViewModel.SearchText)),
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            App.SharedViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.IsSearchMode))
                    SyncSearchUi();
            };
        }

        private void RefreshTooltips()
        {
            ToolTipService.SetToolTip(BackButton, App.ML.TooltipBack);
            ToolTipService.SetToolTip(ForwardButton, App.ML.TooltipForward);
            ToolTipService.SetToolTip(UpButton, App.ML.TooltipUp);
            ToolTipService.SetToolTip(RefreshButton, App.ML.TooltipRefresh);
            ToolTipService.SetToolTip(HomeButton, App.ML.TooltipHome);
            ToolTipService.SetToolTip(SearchButton, App.ML.TooltipSearch);
            ToolTipService.SetToolTip(CopyPathButton, App.ML.TooltipCopyPath);
            SearchTextBox.PlaceholderText = App.ML.SearchPlaceholder;
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

        private void OnAddressBarAreaPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_isSearchMode) return;
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
            if (_isSearchMode) return;
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
                // 支持相对路径（如 ..新建文件夹）：以当前文件夹为基准解析成绝对路径后再导航
                if (!string.IsNullOrWhiteSpace(path) && !System.IO.Path.IsPathRooted(path) && !string.IsNullOrEmpty(CurrentPath))
                {
                    try
                    {
                        path = System.IO.Path.GetFullPath(System.IO.Path.Combine(CurrentPath, path));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[LRSBreadcrumb] 解析相对路径失败: {ex.Message}");
                    }
                }
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
            if (_isSearchMode) return;
            IsEditing = false;
        }

        private void OnBreadcrumbKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Back)
                return;

            // 地址栏编辑中或焦点在文本框时，让 Backspace 正常删除字符
            if (_isEditing || e.OriginalSource is TextBox)
                return;

            e.Handled = true;
            GoBackCommand?.Execute(null);
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

            // 侧栏虚拟位置（此电脑/网络/回收站/云盘）：单段面包屑并显示友好名称
            var virtualName = App.SharedViewModel?.GetSidebarDisplayName(path);
            if (virtualName != null)
            {
                AddSegment(path, virtualName, isLast: true);
                return;
            }

            var isUnc = path.StartsWith("\\\\", StringComparison.Ordinal);
            var trimmed = path.TrimEnd('\\');
            if (trimmed.Length == 0)
                return;

            string[] parts = isUnc
                ? trimmed.Substring(2).Split('\\')
                : trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return;

            var crumbs = new List<(string Display, string FullPath)>();
            if (isUnc)
            {
                var acc = "\\\\" + parts[0];
                crumbs.Add((parts[0], acc));
                for (int i = 1; i < parts.Length; i++)
                {
                    acc = acc + "\\" + parts[i];
                    crumbs.Add((parts[i], acc));
                }
            }
            else
            {
                // 本地路径：把 “C:” 还原成 “C:\” 作为根段
                var drive = parts[0].Length == 1 && path.Length > 1 && path[1] == ':'
                    ? parts[0] + ":"
                    : parts[0];
                var acc = drive + "\\";
                crumbs.Add((parts[0], acc));
                for (int i = 1; i < parts.Length; i++)
                {
                    acc = acc + parts[i];
                    crumbs.Add((parts[i], acc));
                    acc = acc + "\\";
                }
            }

            for (int i = 0; i < crumbs.Count; i++)
            {
                var fullPath = crumbs[i].FullPath;
                // 驱动器根 “C:\” 保留末尾反斜杠
                if (!(i == 0 && !isUnc && fullPath.EndsWith(":\\")))
                    fullPath = fullPath.TrimEnd('\\');
                AddSegment(fullPath, crumbs[i].Display, i == crumbs.Count - 1);
            }
        }

        private void AddSegment(string fullPath, string displayName, bool isLast)
        {
            Segments.Add(new BreadcrumbSegment
            {
                DisplayName = displayName,
                FullPath = fullPath,
                IsLast = isLast,
                NavigateCommand = NavigateCommand,
                NavigateSubCommand = NavigateSubCommand
            });
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
            flyout.Items.Add(new MenuFlyoutItem { Text = App.ML.SearchLoading, IsEnabled = false });

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
                            Text = App.ML.FlyoutEmptyFolder,
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
                        Text = App.ML.FlyoutInaccessible,
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
            // 刷新按钮走真实磁盘重载；普通导航到当前路径不应触发全量重载
            if (!string.IsNullOrEmpty(CurrentPath))
                _ = App.SharedViewModel.RefreshCurrentFolderAsync();
        }

        private void OnSearchButtonClick(object sender, RoutedEventArgs e)
        {
            if (_isSearchMode) ExitSearch();
            else EnterSearch();
        }

        private void EnterSearch()
        {
            _isSearchMode = true;
            App.SharedViewModel.EnterSearchMode();
            ExitEditMode();
            BreadcrumbScrollViewer.Visibility = Visibility.Collapsed;
            SearchTextBox.Visibility = Visibility.Visible;
            SearchTextBox.Focus(FocusState.Programmatic);
            SearchTextBox.SelectAll();
        }

        private void ExitSearch()
        {
            _isSearchMode = false;
            App.SharedViewModel.ExitSearchMode();
            SearchTextBox.Visibility = Visibility.Collapsed;
            BreadcrumbScrollViewer.Visibility = Visibility.Visible;
        }

        private void SyncSearchUi()
        {
            var vm = App.SharedViewModel;
            if (vm.IsSearchMode == _isSearchMode) return;
            if (vm.IsSearchMode) EnterSearch(); else ExitSearch();
        }

        private void OnSearchTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                ExitSearch();
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
}