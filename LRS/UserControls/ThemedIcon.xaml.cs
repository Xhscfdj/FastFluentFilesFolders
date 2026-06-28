using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace LRS.UserControls
{
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

        public string BaseData { get => (string)GetValue(BaseDataProperty); set => SetValue(BaseDataProperty, value); }
        public string AltData { get => (string)GetValue(AltDataProperty); set => SetValue(AltDataProperty, value); }
        public string AccentFillData { get => (string)GetValue(AccentFillDataProperty); set => SetValue(AccentFillDataProperty, value); }
        public string AccentOutlineData { get => (string)GetValue(AccentOutlineDataProperty); set => SetValue(AccentOutlineDataProperty, value); }
        public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

        private static readonly SolidColorBrush _accentBrush =
            (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        private static readonly SolidColorBrush _baseBrush =
            (SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"];

        public ThemedIcon()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => BuildLayers();
        private static void OnLayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var icon = (ThemedIcon)d;
            if (icon.IsLoaded) icon.BuildLayers();
        }

        private void BuildLayers()
        {
            LayerRoot.Children.Clear();

            if (StrokeThickness > 0)
            {
                AddStrokeLayer(AltData);
                AddStrokeLayer(BaseData);
                AddLayer(AccentFillData, _accentBrush, 0.4);
                AddLayer(AccentOutlineData, _accentBrush, 1.0);
            }
            else
            {
                AddLayer(AltData, _baseBrush, 1.0);
                AddLayer(AccentFillData, _accentBrush, 0.4);
                AddLayer(BaseData, _baseBrush, 1.0);
                AddLayer(AccentOutlineData, _accentBrush, 1.0);
            }
        }

        private void AddLayer(string? data, Brush fill, double opacity)
        {
            if (string.IsNullOrEmpty(data)) return;
            var geom = ParseGeometry(data);
            if (geom == null) return;
            LayerRoot.Children.Add(new Path { Data = geom, Fill = fill, Opacity = opacity });
        }

        private void AddStrokeLayer(string? data)
        {
            if (string.IsNullOrEmpty(data)) return;
            var geom = ParseGeometry(data);
            if (geom == null) return;
            LayerRoot.Children.Add(new Path
            {
                Data = geom,
                Stroke = _baseBrush,
                StrokeThickness = StrokeThickness,
                Fill = null
            });
        }

        private static Geometry? ParseGeometry(string pathData)
        {
            var xaml = $"<Geometry xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>{pathData}</Geometry>";
            return (Geometry)XamlReader.Load(xaml);
        }
    }
}
