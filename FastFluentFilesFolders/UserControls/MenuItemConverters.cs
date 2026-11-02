using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FastFluentFilesFolders.UserControls
{
    /// <summary>
    /// 非空字符串 → Visible（菜单项快捷键提示、备注文本用）。
    /// <para>
    /// 必须 <c>partial</c>：它实现 WinRT 接口（<see cref="IValueConverter"/>）并经
    /// <c>Binding.Converter</c> 跨 ABI 传递，Native AOT / 关闭 <c>BuiltInComInterop</c> 的构建里
    /// 只有 partial 类型才会由 CsWinRT 源生成器生成 AOT 安全的 COM 虚表。漏掉 partial 时转换器
    /// 无法被封送，XAML 会抛
    /// <c>Failed to assign to property 'Microsoft.UI.Xaml.UIElement.Visibility'</c>
    /// （E_XAMLPARSEFAILED 0x802B000A → 进程 0xc000027b 崩溃，右键菜单一弹就死）。
    /// </para>
    /// </summary>
    public sealed partial class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
