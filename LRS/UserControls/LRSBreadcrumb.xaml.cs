using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using LRS.ViewModels;

namespace LRS.UserControls
{
    public sealed partial class LRSBreadcrumb : UserControl, INotifyPropertyChanged
    {
        public static readonly DependencyProperty CurrentPathProperty =
            DependencyProperty.Register(nameof(CurrentPath), typeof(string), typeof(LRSBreadcrumb),
                new PropertyMetadata(null, OnCurrentPathChanged));

        public static readonly DependencyProperty NavigateCommandProperty =
            DependencyProperty.Register(nameof(NavigateCommand), typeof(ICommand), typeof(LRSBreadcrumb),
                new PropertyMetadata(null));

        public static readonly DependencyProperty NavigateSubCommandProperty =
            DependencyProperty.Register(nameof(NavigateSubCommand), typeof(ICommand), typeof(LRSBreadcrumb),
                new PropertyMetadata(null));

        public static readonly DependencyProperty GoBackCommandProperty =
            DependencyProperty.Register(nameof(GoBackCommand), typeof(ICommand), typeof(LRSBreadcrumb),
                new PropertyMetadata(null));

        public static readonly DependencyProperty GoForwardCommandProperty =
            DependencyProperty.Register(nameof(GoForwardCommand), typeof(ICommand), typeof(LRSBreadcrumb),
                new PropertyMetadata(null));

        public static readonly DependencyProperty GoUpCommandProperty =
            DependencyProperty.Register(nameof(GoUpCommand), typeof(ICommand), typeof(LRSBreadcrumb),
                new PropertyMetadata(null));

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

        public LRSBreadcrumb()
        {
            this.InitializeComponent();
            CopyPathCommand = new RelayCommand(ExecuteCopyPath);

            this.Loaded += (s, e) =>
            {
                _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
            };
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
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
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
