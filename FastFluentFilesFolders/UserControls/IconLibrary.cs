using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace FastFluentFilesFolders.UserControls
{
    /// <summary>
    /// ThemedIcon 图标库的运行时入口：
    /// <list type="bullet">
    /// <item>Segoe Fluent Icons 字形码位 → <c>IconData.*</c> 资源名的映射（插件、文件操作岛等动态图标用）；</item>
    /// <item>几何数据的解析（WinUI 限制：每层都要新建 Geometry，不能共用实例）；</item>
    /// <item>为 <see cref="MenuFlyoutItem"/> 这类只接受 <see cref="IconElement"/> 的位置生成
    /// <see cref="PathIcon"/>（有矢量图标时）或回退到 <see cref="FontIcon"/>。</item>
    /// </list>
    /// </summary>
    public static class IconLibrary
    {
        /// <summary>字形码位 → IconData 名称（名称同时对应 Icon.&lt;名称&gt; 样式）。</summary>
        private static readonly Dictionary<string, string> GlyphToName = new(StringComparer.Ordinal)
        {
            // 右键菜单
            ["\uE8E5"] = "Open",             // 打开 / 打开方式 / 回收站还原
            ["\uE718"] = "Pin",              // 固定到「快速访问」
            ["\uE77A"] = "Unpin",            // 取消固定
            ["\uE8B7"] = "FolderLine",       // 打开文件所在位置 / 文件操作岛
            ["\uE90F"] = "Properties",       // 属性
            ["\uE712"] = "More",             // 显示更多选项
            ["\uECC9"] = "DeletePermanent",  // 彻底删除
            ["\uE7C3"] = "NewDocument",      // 新建文本文档 / 新建文件
            ["\uE8A5"] = "NewDocument",      // 文档（同一字形）
            ["\uE71B"] = "NewLink",          // 新建快捷方式
            ["\uE9F9"] = "Excel",            // 新建 Excel 工作簿
            ["\uE89A"] = "Word",             // 新建 Word 文档
            ["\uE8B4"] = "PowerPoint",       // 新建 PowerPoint 演示文稿
            ["\uE8F4"] = "NewFolder",        // 新建文件夹
            ["\uE77F"] = "Paste",            // 粘贴
            ["\uE8C8"] = "Copy",             // 复制 / 复制路径
            ["\uE8AB"] = "Cut",              // 剪切
            ["\uE74D"] = "EmptyRecycleBin",  // 清空回收站 / 删除操作
            ["\uE7B8"] = "ArchiveBox",       // 压缩 / 解压插件
            ["\uE946"] = "Help",             // 帮助 / 插件
            ["\uE8BD"] = "Chat",             // 示例插件

            // 工具栏 / 地址栏
            ["\uE72B"] = "Back",             // 后退
            ["\uE72A"] = "Forward",          // 前进
            ["\uE74A"] = "Up",               // 向上一级
            ["\uE72C"] = "Refresh",          // 刷新
            ["\uE80F"] = "Home",             // 主页
            ["\uE721"] = "Search",           // 搜索
            ["\uE70D"] = "ChevronDown",      // 下拉指示
            ["\uE76C"] = "ChevronRight",     // 展开 / 面包屑分隔
            ["\uE711"] = "Close",            // 关闭 / 清除

            // 文件操作岛状态
            ["\uE73E"] = "Success",          // 操作成功
            ["\uE783"] = "Error",            // 操作失败
        };

        /// <summary>字形是否有对应的 ThemedIcon 矢量图标。</summary>
        public static bool HasIcon(string? glyph) => TryGetName(glyph, out _);

        /// <summary>字形码位 → IconData 名称。</summary>
        public static bool TryGetName(string? glyph, out string name)
        {
            if (!string.IsNullOrEmpty(glyph) && GlyphToName.TryGetValue(glyph, out var found))
            {
                name = found;
                return true;
            }
            name = string.Empty;
            return false;
        }

        /// <summary>取字形的线稿层几何数据（给 <see cref="PathIcon"/> 用）。</summary>
        public static string? GetBaseData(string? glyph)
            => TryGetName(glyph, out var name) && TryGetData("IconData." + name, out var data) ? data : null;

        /// <summary>
        /// 取 <c>IconData.*</c> 资源。
        /// 注意：<see cref="ResourceDictionary.TryGetValue(object, out object)"/> 只查本字典、不下钻
        /// <see cref="ResourceDictionary.MergedDictionaries"/>（图标数据在合并进来的
        /// ThemedIconResources.xaml 里），必须自己递归合并字典，否则会退化成字体图标。
        /// </summary>
        public static bool TryGetData(string key, out string data)
        {
            if (TryLookupResource(key, out var value) && value is string text)
            {
                data = text;
                return true;
            }
            data = string.Empty;
            return false;
        }

        /// <summary>在应用资源（含合并字典，递归）里查资源。</summary>
        public static bool TryLookupResource(object key, out object? value)
        {
            var resources = Application.Current?.Resources;
            if (resources != null)
            {
                if (resources.TryGetValue(key, out value) && value != null) return true;
                if (TryLookupInMerged(resources, key, 0, out value)) return true;
            }
            value = null;
            return false;
        }

        private static bool TryLookupInMerged(ResourceDictionary dictionary, object key, int depth, out object? value)
        {
            value = null;
            if (depth > 4) return false;
            foreach (var merged in dictionary.MergedDictionaries)
            {
                if (merged.TryGetValue(key, out value) && value != null) return true;
                if (TryLookupInMerged(merged, key, depth + 1, out value)) return true;
            }
            return false;
        }

        /// <summary>字形未收录、只能回退字体图标时记一条日志（排查"图标没变成 ThemedIcon"用）。</summary>
        public static void LogFallback(string? glyph, string source)
        {
            if (string.IsNullOrEmpty(glyph)) return;
            lock (FallbackLogged)
            {
                if (!FallbackLogged.Add(glyph + "|" + source)) return;
            }
            try
            {
                var path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FastFluentFilesFolders", "icon.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.AppendAllText(path,
                    $"{DateTime.Now:HH:mm:ss.fff} {source} 回退字体图标 U+{(int)glyph[0]:X4}{Environment.NewLine}");
            }
            catch { }
        }

        private static readonly HashSet<string> FallbackLogged = new();

        /// <summary>
        /// 按图标名（IconData.&lt;名&gt;）生成 IconElement。
        /// 注意：CommandBarFlyout 的二级命令只渲染 <see cref="AppBarButton.Icon"/>；自定义 <c>Content</c>
        /// （ThemedIcon 是 UserControl）不会进入可视树（不触发 Loaded，图层不建），所以这些位置只能用 IconElement。
        /// </summary>
        public static IconElement? CreateIconElementByName(string name, double size)
        {
            if (!TryGetData("IconData." + name, out var data)) return null;
            var geometry = ParseGeometry(data);
            if (geometry == null) return null;
            try
            {
                return new PathIcon { Data = geometry, Width = size, Height = size };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IconLibrary] PathIcon 创建失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 生成菜单项图标：有矢量图标时返回 <see cref="PathIcon"/>（跟随菜单项前景色，
        /// 含禁用/悬停状态），否则回退到原来的 <see cref="FontIcon"/> 字形。
        /// </summary>
        public static IconElement CreateIconElement(string? glyph, double size, double fallbackFontSize = 0)
        {
            var data = GetBaseData(glyph);
            var geometry = data == null ? null : ParseGeometry(data);
            if (geometry != null)
            {
                try
                {
                    return new PathIcon { Data = geometry, Width = size, Height = size };
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[IconLibrary] PathIcon 创建失败: {ex.Message}");
                }
            }

            LogFallback(glyph, "菜单项");
            return new FontIcon
            {
                Glyph = glyph ?? string.Empty,
                FontSize = fallbackFontSize > 0 ? fallbackFontSize : size
            };
        }

        /// <summary>
        /// 解析几何数据。
        /// 注意：WinUI 里同一个 Geometry 实例不能同时挂到多个 Path/PathIcon 上（重用会抛
        /// ArgumentException「Value does not fall within the expected range」），因此这里每次新建。
        /// </summary>
        public static Geometry? ParseGeometry(string pathData)
        {
            try
            {
                var xaml = $"<Geometry xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>{pathData}</Geometry>";
                return (Geometry)XamlReader.Load(xaml);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IconLibrary] 几何数据解析失败: {ex.Message}");
                return null;
            }
        }
    }
}
