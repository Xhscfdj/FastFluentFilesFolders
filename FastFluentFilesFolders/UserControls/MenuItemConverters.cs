using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FastFluentFilesFolders.UserControls
{
    /// <summary>非空字符串 → Visible（菜单项快捷键提示、备注文本用）。</summary>
    public sealed class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
