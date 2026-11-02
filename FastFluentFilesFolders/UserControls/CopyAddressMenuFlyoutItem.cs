using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastFluentFilesFolders.UserControls
{
    /// <summary>
    /// 支持自定义内容的菜单项：Content 可承载任意 UIElement（如 ThemedIcon + 文本），
    /// 模板在右侧以灰色小字显示 KeyboardAcceleratorTextOverride 快捷键提示。
    /// <para>
    /// 必须 <c>partial</c>：它是 WinRT 控件派生类、会跨 WinRT ABI 传递，Native AOT / 关闭
    /// <c>BuiltInComInterop</c> 的构建要靠 CsWinRT 源生成器为 partial 类型生成 AOT 安全的 COM 虚表
    /// （CsWinRT1028）；漏掉时菜单项在 AOT 版里会封送失败。
    /// </para>
    /// </summary>
    public sealed partial class CopyAddressMenuFlyoutItem : MenuFlyoutItem
    {
        public CopyAddressMenuFlyoutItem()
        {
            Style = (Style)Application.Current.Resources["CopyAddressMenuFlyoutItemStyle"];
        }

        public static readonly DependencyProperty ContentProperty =
            DependencyProperty.Register(nameof(Content), typeof(object), typeof(CopyAddressMenuFlyoutItem),
                new PropertyMetadata(null));

        public object Content
        {
            get => (object)GetValue(ContentProperty);
            set => SetValue(ContentProperty, value);
        }
    }
}
