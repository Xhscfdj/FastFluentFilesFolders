using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace FastFluentFilesFolders.UserControls
{
    /// <summary>
    /// 分层矢量图标控件。
    /// 几何数据存放在 <c>IconData.*</c> 资源里（16×16 设计栅格，SVG 路径微语言），分为线稿层
    /// （用 <see cref="Control.Foreground"/> 绘制）、内部色域层（用 <see cref="AccentBrush"/> 以 40%
    /// 透明度填充）与强调色描边层。两种用法：
    /// <list type="bullet">
    /// <item><c>Style="{StaticResource Icon.Cut}"</c> —— 固定图标；</item>
    /// <item><c>Glyph="&#xE8C8;"</c> —— 运行时字形（插件、文件操作岛），按字形码位查表。</item>
    /// </list>
    /// </summary>
    public sealed partial class ThemedIcon : UserControl
    {
        public static readonly DependencyProperty BaseDataProperty =
            DependencyProperty.Register(nameof(BaseData), typeof(string), typeof(ThemedIcon),
                new PropertyMetadata("", OnLayerChanged));
        public static readonly DependencyProperty AltDataProperty =
            DependencyProperty.Register(nameof(AltData), typeof(string), typeof(ThemedIcon),
                new PropertyMetadata("", OnLayerChanged));
        public static readonly DependencyProperty AccentFillDataProperty =
            DependencyProperty.Register(nameof(AccentFillData), typeof(string), typeof(ThemedIcon),
                new PropertyMetadata("", OnLayerChanged));
        public static readonly DependencyProperty AccentOutlineDataProperty =
            DependencyProperty.Register(nameof(AccentOutlineData), typeof(string), typeof(ThemedIcon),
                new PropertyMetadata("", OnLayerChanged));
        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(ThemedIcon),
                new PropertyMetadata(0.0, OnLayerChanged));
        public static readonly DependencyProperty AccentBrushProperty =
            DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(ThemedIcon),
                new PropertyMetadata(null, OnLayerChanged));
        public static readonly DependencyProperty MonoProperty =
            DependencyProperty.Register(nameof(Mono), typeof(bool), typeof(ThemedIcon),
                new PropertyMetadata(false, OnLayerChanged));
        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(ThemedIcon),
                new PropertyMetadata("", OnGlyphChanged));

        public string BaseData { get => (string)GetValue(BaseDataProperty); set => SetValue(BaseDataProperty, value); }
        public string AltData { get => (string)GetValue(AltDataProperty); set => SetValue(AltDataProperty, value); }
        public string AccentFillData { get => (string)GetValue(AccentFillDataProperty); set => SetValue(AccentFillDataProperty, value); }
        public string AccentOutlineData { get => (string)GetValue(AccentOutlineDataProperty); set => SetValue(AccentOutlineDataProperty, value); }
        public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
        /// <summary>强调色图层画刷（默认取系统强调色）。</summary>
        public Brush? AccentBrush { get => (Brush?)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
        /// <summary>true 时只画线稿层，整体跟随 <see cref="Control.Foreground"/>（红色"彻底删除"、文件操作岛状态色等）。</summary>
        public bool Mono { get => (bool)GetValue(MonoProperty); set => SetValue(MonoProperty, value); }
        /// <summary>Segoe Fluent Icons 字形码位，按码位自动套用对应的 IconData 图层。</summary>
        public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

        private static readonly SolidColorBrush _fallbackBaseBrush =
            (SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        private static readonly SolidColorBrush _fallbackAccentBrush =
            (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"];

        private Brush BaseBrush => Foreground ?? _fallbackBaseBrush;
        private Brush AccentBrushOrDefault => AccentBrush ?? _fallbackAccentBrush;

        public ThemedIcon()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            // Foreground / 主题变化时重画线稿层
            RegisterPropertyChangedCallback(ForegroundProperty, OnForegroundChanged);
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => BuildLayers();

        /// <summary>不依赖 Loaded 立即构建图层（用于还没进可视树就要出图的场景）。</summary>
        public void EnsureBuilt() => BuildLayers();

        private void OnForegroundChanged(DependencyObject sender, DependencyProperty dp) => BuildLayers();

        private static void OnLayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 立即建层：CommandBarFlyout 的二级命令（Popup）里的控件不会触发 Loaded，
            // 若等 Loaded 再画就会是空白图标（标签也会左移），所以这里不等 Loaded。
            ((ThemedIcon)d).BuildLayers();
        }

        private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var icon = (ThemedIcon)d;
            icon.ApplyGlyph(e.NewValue as string);
        }

        /// <summary>按字形码位套用图标图层数据；未收录的字形回退成字体图标。</summary>
        private void ApplyGlyph(string? glyph)
        {
            if (string.IsNullOrEmpty(glyph) || !IconLibrary.TryGetName(glyph, out var name))
            {
                ClearValue(BaseDataProperty);
                ClearValue(AccentFillDataProperty);
                ClearValue(AccentOutlineDataProperty);
                // 图层可能没有变化（连续两个未收录字形），这里强制重画以便字体图标跟着换
                BuildLayers();
                return;
            }

            ApplyIconData(BaseDataProperty, name);
            ApplyIconData(AccentFillDataProperty, name + ".Fill");
            ApplyIconData(AccentOutlineDataProperty, name + ".Outline");
        }

        private void ApplyIconData(DependencyProperty property, string name)
        {
            if (TryGetIconData(name, out var data))
                SetValue(property, data);
            else
                ClearValue(property);
        }

        private static bool TryGetIconData(string name, out string data)
            => IconLibrary.TryGetData("IconData." + name, out data);

        private void BuildLayers()
        {
            LayerRoot.Children.Clear();

            var baseBrush = BaseBrush;
            var accentBrush = AccentBrushOrDefault;

            if (Mono)
            {
                // 单色模式：行为与 FontIcon 一致，整体跟随 Foreground（含状态色）
                AddLayer(AltData, baseBrush, 1.0);
                AddLayer(BaseData, baseBrush, 1.0);
            }
            else if (StrokeThickness > 0)
            {
                AddStrokeLayer(AltData, baseBrush);
                AddStrokeLayer(BaseData, baseBrush);
                AddLayer(AccentFillData, accentBrush, 0.4);
                AddLayer(AccentOutlineData, accentBrush, 1.0);
            }
            else
            {
                AddLayer(AltData, baseBrush, 1.0);
                AddLayer(AccentFillData, accentBrush, 0.4);
                AddLayer(BaseData, baseBrush, 1.0);
                AddLayer(AccentOutlineData, accentBrush, 1.0);
            }

            // 只有字形、没有矢量数据时（插件自定义字形等）回退成字体图标，避免出现空白图标
            if (LayerRoot.Children.Count == 0 && !string.IsNullOrEmpty(Glyph))
            {
                IconLibrary.LogFallback(Glyph, "ThemedIcon");
                LayerRoot.Children.Add(new FontIcon
                {
                    Glyph = Glyph,
                    FontSize = FontSize,
                    Foreground = baseBrush
                });
            }
        }

        private void AddLayer(string? data, Brush fill, double opacity)
        {
            if (string.IsNullOrEmpty(data)) return;
            var geom = ParseGeometry(data);
            if (geom == null) return;
            try
            {
                LayerRoot.Children.Add(new Path { Data = geom, Fill = fill, Opacity = opacity });
            }
            catch (System.Exception ex)
            {
                // 几何数据异常时只丢掉这一层，不能让整个界面挂掉
                System.Diagnostics.Debug.WriteLine($"[ThemedIcon] 图层应用失败: {ex.Message} / {data[..System.Math.Min(60, data.Length)]}");
            }
        }

        private void AddStrokeLayer(string? data, Brush strokeBrush)
        {
            if (string.IsNullOrEmpty(data)) return;
            var geom = ParseGeometry(data);
            if (geom == null) return;
            try
            {
                LayerRoot.Children.Add(new Path
                {
                    Data = geom,
                    Stroke = strokeBrush,
                    StrokeThickness = StrokeThickness,
                    Fill = null
                });
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ThemedIcon] 描边图层应用失败: {ex.Message}");
            }
        }

        private static Geometry? ParseGeometry(string pathData) => IconLibrary.ParseGeometry(pathData);
    }
}
