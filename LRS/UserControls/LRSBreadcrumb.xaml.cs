using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
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

namespace LRS.UserControls
{
	public sealed partial class LRSBreadcrumb : UserControl, INotifyPropertyChanged
	{
		// ---------- 依赖属性 ----------
		public static readonly DependencyProperty CurrentPathProperty =
			DependencyProperty.Register(nameof(CurrentPath), typeof(string), typeof(LRSBreadcrumb),
				new PropertyMetadata(null, OnCurrentPathChanged));

		public static readonly DependencyProperty NavigateCommandProperty =
			DependencyProperty.Register(nameof(NavigateCommand), typeof(ICommand), typeof(LRSBreadcrumb),
				new PropertyMetadata(null));

		public static readonly DependencyProperty NavigateSubCommandProperty =
			DependencyProperty.Register(nameof(NavigateSubCommand), typeof(ICommand), typeof(LRSBreadcrumb),
				new PropertyMetadata(null));

		// ---------- CLR 属性 ----------
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

		// ---------- 内部数据 ----------
		public ObservableCollection<BreadcrumbSegment> Segments { get; } = new();

		// 复制路径命令
		public ICommand CopyPathCommand { get; }

		// ---------- 构造函数 ----------
		public LRSBreadcrumb()
		{
			this.InitializeComponent();
			CopyPathCommand = new RelayCommand(ExecuteCopyPath);
			DataContext = App.SharedViewModel;

			// 获取 Compositor（用于动画）
			this.Loaded += (s, e) =>
			{
				_compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
			};
		}

		private Compositor _compositor;

		// ---------- 静态回调 ----------
		private static void OnCurrentPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var control = (LRSBreadcrumb)d;
			control.UpdateSegments(e.NewValue as string);
		}

		// ---------- 更新面包屑段 ----------
		private void UpdateSegments(string path)
		{
			Debug.WriteLine($"[UpdateSegments] path = {path}");
			Segments.Clear();
			if (string.IsNullOrEmpty(path))
				return;

			var parts = ParsePath(path);
			for (int i = 0; i < parts.Count; i++)
			{
				var displayName = parts[i];
				string fullPath;
				if (i == 0 && path.StartsWith("\\")) // 网络路径特殊处理
				{
					fullPath = parts[0];
				}
				else
				{
					fullPath = string.Join("\\", parts.Take(i + 1));
					if (i == 0 && fullPath.Length == 2 && fullPath[1] == ':')
						fullPath += "\\";
				}

				var segment = new BreadcrumbSegment
				{
					DisplayName = displayName,
					FullPath = fullPath,
					IsLast = (i == parts.Count - 1),
					NavigateCommand = NavigateCommand,
					NavigateSubCommand = NavigateSubCommand
				};
				Segments.Add(segment);
			}
		}

		private List<string> ParsePath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return new List<string>();

			// 处理驱动器根目录 "C:\"
			if (path.EndsWith(":\\"))
				return new List<string> { path.TrimEnd('\\') };

			// 处理网络路径 "\\Server\Share\Folder"
			if (path.StartsWith("\\\\"))
			{
				var parts = path.Split('\\');
				var result = new List<string>();
				if (parts.Length >= 2)
				{
					result.Add(parts[0] + "\\" + parts[1]); // \\Server
					for (int i = 2; i < parts.Length; i++)
					{
						if (!string.IsNullOrEmpty(parts[i]))
							result.Add(parts[i]);
					}
				}
				return result;
			}

			// 普通路径
			return new List<string>(path.Split('\\', StringSplitOptions.RemoveEmptyEntries));
		}

		// ---------- 箭头点击事件 ----------
		private void OnArrowClick(object sender, RoutedEventArgs e)
		{
			var button = sender as Button;
			if (sender is not Button btn) return;

			if (btn.Tag is not LRSBreadcrumb currentItem) return;
			var segment = button?.Tag as BreadcrumbSegment;
			if (segment == null) return;

			// ---- 旋转动画 ----
			var visual = ElementCompositionPreview.GetElementVisual(button);
			if (_compositor != null)
			{
				// 判断当前旋转角度：若接近0则旋转到180，否则回0
				float currentAngle = (float)visual.RotationAngleInDegrees;
				float targetAngle = (Math.Abs(currentAngle) < 1) ? 180f : 0f;

				// 1. 创建动画
				var animation = _compositor.CreateScalarKeyFrameAnimation();
				animation.Duration = TimeSpan.FromMilliseconds(200);

				// 2. 创建缓动函数 (Cubic Bezier)
				var easingFunction = _compositor.CreateCubicBezierEasingFunction(
					new System.Numerics.Vector2(0.25f, 0.1f),
					new System.Numerics.Vector2(0.25f, 1.0f));

				// 3. 在插入关键帧时指定缓动函数
				animation.InsertKeyFrame(1.0f, targetAngle, easingFunction);

				// 4. 运行动画
				visual.StartAnimation("RotationAngleInDegrees", animation);
			}

			// ---- 弹出子文件夹菜单 ----
			var flyout = button.Flyout as MenuFlyout;
			if (flyout != null)
			{
				flyout.Items.Clear();

				// 异步获取子文件夹（为避免阻塞UI，使用Task.Run，但需在UI线程构建菜单项）
				var subDirs = Task.Run(() => SafeGetDirs(segment.FullPath)).Result; // 同步等待，但数量少可接受
				foreach (var dir in subDirs)
				{
					var item = new MenuFlyoutItem
					{
						Text = Path.GetFileName(dir),
						Tag = dir
					};
					item.Click += (s, args) =>
					{
						var path = (s as MenuFlyoutItem)?.Tag as string;
						if (!string.IsNullOrEmpty(path))
							NavigateSubCommand?.Execute(path);
					};
					flyout.Items.Add(item);
				}

				if (flyout.Items.Count == 0)
				{
					flyout.Items.Add(new MenuFlyoutItem { Text = "（空文件夹）", IsEnabled = false });
				}

				// Flyout 会自动打开（Button 自带 Flyout）
			}
		}

		// ---------- 辅助方法 ----------
		private static List<string> SafeGetDirs(string path)
		{
			// 复用你已有的 SafeGetDirs 方法
			return LRS.ViewModels.FileSystemNodeViewModel.SafeGetDirs(path);
		}

		// ---------- 复制路径 ----------
		private void ExecuteCopyPath()
		{
			if (!string.IsNullOrEmpty(CurrentPath))
			{
				var dataPackage = new DataPackage();
				dataPackage.SetText(CurrentPath);
				Clipboard.SetContent(dataPackage);
			}
		}

		// ---------- 转换器 ----------
		public class BoolToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
		{
			public object Convert(object value, Type targetType, object parameter, string language)
			{
				bool val = value is bool && (bool)value;
				// 若参数为 "Collapse"，则 Collapse 隐藏，否则用 Visibility.Collapsed
				string param = parameter as string;
				return val ? Visibility.Collapsed : Visibility.Visible;
			}

			public object ConvertBack(object value, Type targetType, object parameter, string language)
			{
				throw new NotImplementedException();
			}
		}

		// ---------- RelayCommand 简单实现 ----------
		private class RelayCommand : ICommand
		{
			private readonly Action _execute;
			public RelayCommand(Action execute) => _execute = execute;
			public event EventHandler CanExecuteChanged;
			public bool CanExecute(object parameter) => true;
			public void Execute(object parameter) => _execute();
		}

		// ---------- INotifyPropertyChanged ----------
		public event PropertyChangedEventHandler PropertyChanged;
		private void OnPropertyChanged(string name) =>
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}

	// ---------- 面包屑段数据模型 ----------
	public class BreadcrumbSegment
	{
		public string DisplayName { get; set; }
		public string FullPath { get; set; }
		public bool IsLast { get; set; }
		public ICommand NavigateCommand { get; set; }
		public ICommand NavigateSubCommand { get; set; }
		public ObservableCollection<string> SubFolders { get; set; } = new();
	}
}