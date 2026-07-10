# FastFluentFilesFolders 插件开发指南

## 目录

1. [概述](#概述)
2. [插件架构](#插件架构)
3. [快速入门](#快速入门)
4. [扩展点详解](#扩展点详解)
5. [插件打包与分发](#插件打包与分发)
6. [多语言支持](#多语言支持)
7. [主题扩展](#主题扩展)
8. [完整示例](#完整示例)
9. [API 参考](#api-参考)

---

## 概述

FastFluentFilesFolders 的插件系统允许你通过实现标准接口来为文件管理器添加新功能。插件可以做以下事情：

- 在右键菜单中添加自定义命令
- 在工具栏上添加按钮
- 在设置页面添加配置卡片
- 拦截文件操作（复制、删除、重命名等）
- 提供自定义主题（颜色、图标等）

插件分为两种形式：

| 类型 | 说明 | 注册方式 |
|---|---|---|
| **内置插件** | 随主程序一起编译 | 在 `App.xaml.cs` 的 DI 容器中注册 |
| **外部插件** | 作为独立的 `.zip` 包分发 | 通过设置页面的"导入插件"按钮安装 |

---
## 插件架构

```
IExtension (基础接口)
  ├── IContextMenuExtension   右键菜单
  ├── IToolbarExtension       工具栏按钮
  ├── ISettingsExtension      设置页卡片
  ├── IFileOperationHook      文件操作钩子
  └── ICustomThemeExtension   自定义主题
```

每个插件必须实现 `IExtension` 接口，然后根据需要实现上述一个或多个扩展点接口。

---

## 快速入门

### 第一步：创建项目

创建一个 .NET 8 类库项目，引用以下 NuGet 包：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.2.0" />
    <PackageReference Include="CommunityToolkit.WinUI.Controls.SettingsControls" Version="8.2.251219" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
  </ItemGroup>
</Project>
```

> **重要**：插件项目需要引用 FastFluentFilesFolders 主程序的 DLL（`FastFluentFilesFolders.dll`），以访问 `IExtension`、`ExtensionContext`、`FileSystemNodeViewModel` 等类型。请将 FastFluentFilesFolders 主程序编译输出的 `FastFluentFilesFolders.dll` 复制到插件的引用路径中。

### 第二步：实现基础接口

```csharp
using FastFluentFilesFolders.Extensions.Interfaces;
using System;
using System.Threading.Tasks;

public class HelloPlugin : IExtension
{
    public string Id => "com.example.HelloPlugin";
    public string DisplayName => "HelloPlugin.Name";
    public Version Version => new(1, 0, 0);

    private ExtensionContext? _ctx;

    public Task InitializeAsync(ExtensionContext context)
    {
        _ctx = context;
        // 插件初始化逻辑
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        // 插件清理逻辑
        return Task.CompletedTask;
    }
}
```

### 第三步：实现扩展点

一个插件可以实现多个扩展点接口。例如同时实现 `IContextMenuExtension` 和 `ISettingsExtension`：

```csharp
public class HelloPlugin : IExtension, IContextMenuExtension, ISettingsExtension
{
    // IExtension 实现...
    
    // 右键菜单
    public IEnumerable<ExtensionMenuItem> GetMenuItems(
        FileSystemNodeViewModel? targetNode,
        ContextMenuLocation location)
    {
        // 返回菜单项...
    }
    
    // 设置页
    public IEnumerable<UIElement> CreateSettingsCards()
    {
        // 返回设置卡片...
    }
}
```

### 第四步：注册内置插件（开发阶段）

在 `App.xaml.cs` 的 DI 配置中添加：

```csharp
services.AddSingleton<IExtension, HelloPlugin>();
```

构建并运行，插件即会加载。

---

## 扩展点详解

### 右键菜单扩展 (`IContextMenuExtension`)

向文件/文件夹右键菜单或空白区域右键菜单添加命令。

```csharp
using FastFluentFilesFolders.Extensions.Interfaces;
using FastFluentFilesFolders.ViewModels;
using System.Collections.Generic;
using System.Windows.Input;

public interface IContextMenuExtension : IExtension
{
    IEnumerable<ExtensionMenuItem> GetMenuItems(
        FileSystemNodeViewModel? targetNode,
        ContextMenuLocation location);
}
```

#### `ContextMenuLocation` 枚举

| 值 | 说明 |
|---|---|
| `FileItem` | 右键点击的是文件 |
| `FolderItem` | 右键点击的是文件夹 |
| `Background` | 右键点击的是空白区域 |
| `MultipleSelection` | 选中了多个项目 |

#### `ExtensionMenuItem` 属性

| 属性 | 类型 | 说明 |
|---|---|---|
| `Header` | `string` | 菜单项显示文本（支持多语言 key） |
| `IconGlyph` | `string?` | Segoe Fluent 图标字体的 glyph 码（如 `"\uE8A5"`） |
| `Command` | `ICommand?` | 点击时执行的命令 |
| `CommandParameter` | `object?` | 传递给命令的参数 |
| `IsSeparator` | `bool` | 是否为分隔线 |
| `SubItems` | `List<ExtensionMenuItem>?` | 子菜单项（支持嵌套） |

#### 示例

```csharp
public IEnumerable<ExtensionMenuItem> GetMenuItems(
    FileSystemNodeViewModel? targetNode, ContextMenuLocation location)
{
    if (location == ContextMenuLocation.FileItem && targetNode != null)
    {
        var filePath = targetNode.FullPath;
        
        yield return new ExtensionMenuItem { IsSeparator = true };
        
        yield return new ExtensionMenuItem
        {
            Header = "输出文件路径",
            IconGlyph = "\uE8C8",
            Command = new RelayCommand(() =>
            {
                System.Diagnostics.Debug.WriteLine($"选中文件: {filePath}");
            })
        };
        
        // 带子菜单的示例
        yield return new ExtensionMenuItem
        {
            Header = "高级操作",
            IconGlyph = "\uE712",
            SubItems = new List<ExtensionMenuItem>
            {
                new() { Header = "操作一", IconGlyph = "\uE8BD", Command = DoSomething },
                new() { IsSeparator = true },
                new() { Header = "操作二", IconGlyph = "\uE946", Command = DoAnother },
            }
        };
    }
}
```

> **注意**：当前内置插件的 `GetMenuItems` 在构造时被调用一次（`targetNode` 可能为 `null`），菜单项是静态的。如需根据当前选中项动态变化，建议在命令中通过 `App.SharedViewModel.SelectedFolder` 获取当前上下文。

---

### 工具栏扩展 (`IToolbarExtension`)

向文件浏览器顶部的工具栏区域添加按钮。

```csharp
public interface IToolbarExtension : IExtension
{
    IEnumerable<ToolbarItem> GetToolbarItems();
}
```

#### `ToolbarItem` 属性

| 属性 | 类型 | 说明 |
|---|---|---|
| `Header` | `string` | 按钮文本 |
| `IconGlyph` | `string?` | Segoe Fluent 图标 glyph 码 |
| `Command` | `ICommand?` | 点击命令 |
| `CommandParameter` | `object?` | 命令参数 |
| `ToolTip` | `string?` | 鼠标悬停提示 |

#### 示例

```csharp
public IEnumerable<ToolbarItem> GetToolbarItems()
{
    yield return new ToolbarItem
    {
        Header = "打开终端",
        IconGlyph = "\uE756",
        ToolTip = "在当前位置打开终端",
        Command = new RelayCommand(() =>
        {
            var path = App.SharedViewModel.SelectedFolder?.FullPath ?? "C:\\";
            System.Diagnostics.Process.Start("cmd.exe", $"/k cd /d \"{path}\"");
        })
    };
}
```

---

### 设置页扩展 (`ISettingsExtension`)

向设置页面添加自定义配置卡片。

```csharp
public interface ISettingsExtension : IExtension
{
    IEnumerable<UIElement> CreateSettingsCards();
}
```

#### 示例

```csharp
public IEnumerable<UIElement> CreateSettingsCards()
{
    yield return new SettingsCard
    {
        Header = "我的插件设定",
        HeaderIcon = new FontIcon { Glyph = "\uE946" },
        Description = "这里可以放置插件专属的配置项",
        Content = new ToggleSwitch 
        { 
            IsOn = true,
            OnContent = "开",
            OffContent = "关"
        }
    };
}
```

> 支持的控件：可以使用任何 WinUI 3 控件（`TextBox`、`ToggleSwitch`、`NumberBox`、`ComboBox` 等）。推荐使用 `CommunityToolkit.WinUI.Controls.SettingsCard` 来保持与主程序一致的视觉效果。

---

### 文件操作钩子 (`IFileOperationHook`)

拦截文件操作（复制、移动、删除、重命名、创建），可以在操作前后执行自定义逻辑。

```csharp
public interface IFileOperationHook : IExtension
{
    Task<bool> BeforeExecute(FileOperationContext context);
    Task AfterExecute(FileOperationContext context, bool success);
}
```

#### `FileOperationType` 枚举

| 值 | 说明 |
|---|---|
| `Copy` | 复制文件/文件夹 |
| `Move` | 移动文件/文件夹 |
| `Delete` | 删除文件/文件夹 |
| `Rename` | 重命名 |
| `Create` | 创建新文件/文件夹 |

#### `FileOperationContext` 属性

| 属性 | 类型 | 说明 |
|---|---|---|
| `OperationType` | `FileOperationType` | 操作类型 |
| `SourcePath` | `string` | 源文件路径 |
| `DestinationPath` | `string?` | 目标路径（复制/移动/重命名时有值） |
| `IsDirectory` | `bool` | 是否为目录 |
| `IsCancelled` | `bool` | 设为 `true` 可取消当前操作 |

#### 示例

```csharp
public async Task<bool> BeforeExecute(FileOperationContext context)
{
    // 在删除前弹窗确认
    if (context.OperationType == FileOperationType.Delete)
    {
        var fileName = System.IO.Path.GetFileName(context.SourcePath);
        // 自定义逻辑：例如阻止删除特定文件
        if (fileName == "Important.txt")
        {
            System.Diagnostics.Debug.WriteLine("阻止删除重要文件！");
            context.IsCancelled = true;
            return false;
        }
    }
    return true;
}

public async Task AfterExecute(FileOperationContext context, bool success)
{
    if (success)
    {
        System.Diagnostics.Debug.WriteLine(
            $"操作完成: {context.OperationType} - {context.SourcePath}");
    }
}
```

> **当前状态**：`IFileOperationHook` 接口已定义并通过 `PluginManager.RunBeforeOperationHooks` 和 `PluginManager.RunAfterOperationHooks` 方法可用，但主程序的文件操作命令中尚未集成钩子调用。这是一个即将接入的功能。

---

### 主题扩展 (`ICustomThemeExtension`)

提供自定义的 WinUI 3 资源字典，用于更改应用程序的颜色、字体、图标等视觉效果。

```csharp
public interface ICustomThemeExtension : IExtension
{
    ResourceDictionary? GetThemeResources();
    string ThemeName { get; }
}
```

#### 示例

```csharp
public class DarkGreenTheme : ICustomThemeExtension
{
    public string Id => "com.example.DarkGreen";
    public string DisplayName => "DarkGreenTheme.Name";
    public Version Version => new(1, 0, 0);
    public string ThemeName => "暗绿主题";

    public ResourceDictionary? GetThemeResources()
    {
        var dict = new ResourceDictionary();
        
        // 覆盖系统画刷颜色
        dict.Add("SystemAccentColor", Microsoft.UI.Colors.LimeGreen);
        dict.Add("SystemAccentColorLight1", Microsoft.UI.Colors.PaleGreen);
        
        return dict;
    }
}
```

> **当前状态**：`ICustomThemeExtension` 接口已定义，可通过 `PluginManager.GetThemePlugins()` 获取。主题的自动应用功能将在后续版本中完善。

---

## 插件打包与分发

### 分包结构

将你的插件打包为 `.zip`（或 `.lrsplug`）：

```
MyPlugin.zip  (或 MyPlugin.lrsplug)
  ├── plugin.json          # 清单文件（必须）
  └── MyPlugin.dll         # 插件程序集（必须）
```

> 如果插件有额外依赖的 DLL，也一并放入 zip 根目录。

### `plugin.json` 清单格式

```json
{
    "id": "com.example.MyPlugin",
    "name": "MyPlugin",
    "version": "1.0.0",
    "author": "Your Name",
    "description": "A plugin for FastFluentFilesFolders file explorer.",
    "entryAssembly": "MyPlugin.dll",
    "minAppVersion": "0.0.7"
}
```

| 字段 | 必填 | 说明 |
|---|---|---|
| `id` | 是 | 全局唯一标识符，建议使用反向域名格式，如 `com.yourname.pluginname` |
| `name` | 是 | 插件名称（显示用，支持多语言 key） |
| `version` | 是 | 语义化版本号，如 `1.0.0` |
| `author` | 否 | 作者名称 |
| `description` | 否 | 插件描述（支持多语言 key） |
| `entryAssembly` | 是 | 入口 DLL 文件名，必须包含 `.dll` 扩展名 |
| `minAppVersion` | 否 | 最低支持的 FastFluentFilesFolders 版本，如 `0.0.7` |

### 安装过程

1. 打开 FastFluentFilesFolders → 设置
2. 找到 **"插件管理"** 区域
3. 点击 **"选择 .zip 导入"**
4. 选择你的插件 `.zip` 文件
5. 系统自动验证、解压、加载

插件被安装到 `%LocalAppData%\\FastFluentFilesFolders\\Plugins\{plugin-id}\`，重启 FastFluentFilesFolders 后自动加载。

### 卸载

在设置页面的"已安装的插件"列表中，点击对应插件右侧的删除按钮即可卸载。

### 调试技巧

- 使用 `System.Diagnostics.Debug.WriteLine()` 输出调试日志
- 安装后在 `%LocalAppData%\\FastFluentFilesFolders\\Plugins\{id}\` 检查文件是否正确解压
- 如果加载失败，会显示具体错误信息（清单缺失、DLL 不兼容等）

---

## 多语言支持

插件通过 `ExtensionContext.GetString(key)` 获取本地化文本：

```csharp
public Task InitializeAsync(ExtensionContext context)
{
    _ctx = context;
    // 获取主程序注册的本地化字符串
    var hello = context.GetString("MyPlugin.Hello");
    return Task.CompletedTask;
}
```

### 添加本地化字符串

有两种方式让插件支持多语言：

#### 方式一：注册到主程序（内置插件）

在 `Strings/zh-Hans.json` 和 `Strings/en.json` 中添加插件的字符串 key：

```json
{
    "MyPlugin.Name": "我的插件",
    "MyPlugin.SettingsHeader": "插件设置",
    "MyPlugin.SayHello": "你好！"
}
```

```json
{
    "MyPlugin.Name": "My Plugin",
    "MyPlugin.SettingsHeader": "Plugin Settings",
    "MyPlugin.SayHello": "Hello!"
}
```

#### 方式二：插件自带语言文件（外部插件）

将语言 JSON 文件随插件打包，在 `InitializeAsync` 中手动调用：

```csharp
public Task InitializeAsync(ExtensionContext context)
{
    // 读取插件自带的语言文件
    var langDir = Path.Combine(pluginInstallPath, "lang");
    // ... 自行实现加载逻辑
}
```

> 当前版本的 `LocalizationService` 仅支持单一 JSON 文件加载。外部插件的多语言需要在插件代码中自行管理。后续版本将提供 `RegisterStrings` API。

---

## 完整示例

一个功能完备的插件示例，展示了右键菜单、设置页和消息提示的组合使用：

```csharp
using CommunityToolkit.WinUI.Controls;
using FastFluentFilesFolders.Extensions.Interfaces;
using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;

public class FileInfoPlugin : IContextMenuExtension, ISettingsExtension
{
    public string Id => "com.example.FileInfo";
    public string DisplayName => "FileInfo.Name";
    public Version Version => new(1, 0, 0);

    private ExtensionContext? _ctx;
    private ICommand? _showInfoCommand;
    private ICommand? _openFolderCommand;

    public Task InitializeAsync(ExtensionContext context)
    {
        _ctx = context;
        Debug.WriteLine("[FileInfo] Initialized.");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync() => Task.CompletedTask;

    public IEnumerable<ExtensionMenuItem> GetMenuItems(
        FileSystemNodeViewModel? targetNode, ContextMenuLocation location)
    {
        if (location == ContextMenuLocation.FileItem && targetNode != null)
        {
            var node = targetNode;
            yield return new ExtensionMenuItem { IsSeparator = true };
            yield return new ExtensionMenuItem
            {
                Header = "文件信息",
                IconGlyph = "\uE946",
                SubItems = new List<ExtensionMenuItem>
                {
                    new()
                    {
                        Header = $"名称: {node.Name}",
                        IsSeparator = false
                    },
                    new()
                    {
                        Header = $"大小: {node.VisualSize}",
                        IsSeparator = false
                    },
                    new()
                    {
                        Header = $"路径: {node.FullPath}",
                        IsSeparator = false
                    },
                    new() { IsSeparator = true },
                    new()
                    {
                        Header = "在终端中打开",
                        IconGlyph = "\uE756",
                        Command = _openFolderCommand ??= new RelayCommand<string>(
                            path => Debug.WriteLine($"终端打开: {path}"))
                    }
                }
            };
        }
    }

    public IEnumerable<UIElement> CreateSettingsCards()
    {
        yield return new SettingsCard
        {
            Header = "文件信息插件",
            HeaderIcon = new FontIcon { Glyph = "\uE946" },
            Description = "在右键菜单中显示文件详细信息。",
            Content = new TextBlock 
            { 
                Text = "已启用",
                VerticalAlignment = VerticalAlignment.Center 
            }
        };
    }
}

// 简单 RelayCommand 实现（或使用 CommunityToolkit.Mvvm.Input.RelayCommand）
public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    public RelayCommand(Action<T?> execute) => _execute = execute;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute((T?)parameter);
    public event EventHandler? CanExecuteChanged;
}
```

---

## API 参考

### `IExtension` — 基础接口

所有插件必须实现。

```csharp
public interface IExtension
{
    string Id { get; }                    // 全局唯一标识
    string DisplayName { get; }           // 显示名称（可做多语言 key）
    Version Version { get; }              // 语义化版本号
    
    Task InitializeAsync(ExtensionContext context);  // 初始化
    Task ShutdownAsync();                            // 清理
}
```

### `ExtensionContext` — 插件上下文

在 `InitializeAsync` 中传入，提供对主程序服务的访问。

```csharp
public class ExtensionContext
{
    public IServiceProvider Services { get; }            // DI 容器
    public DispatcherQueue UIDispatcherQueue { get; }    // UI 线程调度器
    public Configs AppConfigs { get; }                   // 应用配置
    public LocalizationService LocalizationService { get; } // 本地化服务
    
    public string GetString(string key);                 // 获取本地化字符串
}
```

### `FileSystemNodeViewModel` — 文件/文件夹节点

右键菜单扩展中 `targetNode` 的类型，包含文件/文件夹的完整信息。

| 属性 | 类型 | 说明 |
|---|---|---|
| `Name` | `string` | 文件/文件夹名称 |
| `FullPath` | `string` | 完整路径 |
| `IsDirectory` | `bool` | 是否为目录 |
| `IsPlaceholder` | `bool` | 是否为占位节点 |
| `Extension` | `string` | 扩展名（含点号） |
| `ExactSize` | `long` | 精确大小（字节） |
| `VisualSize` | `string` | 可读大小（如 "1.2 MB"） |
| `LastModifiedTime` | `DateTime` | 最后修改时间 |
| `FirstCreatedTime` | `DateTime` | 创建时间 |
| `Icon` | `ImageSource?` | 文件图标 |
| `Children` | `ObservableCollection<FileSystemNodeViewModel>` | 子节点集合 |
| `IsLoaded` | `bool` | 子节点是否已加载 |

### UI 线程安全

所有 UI 操作必须在 UI 线程执行。使用 `ExtensionContext.UIDispatcherQueue`：

```csharp
_ctx.UIDispatcherQueue.TryEnqueue(() =>
{
    // 在这里更新 UI
    var dialog = new ContentDialog { ... };
    _ = dialog.ShowAsync();
});
```

> 千万不要在后台线程直接操作 `ObservableCollection` 或 XAML 绑定的属性。

### 支持的 `IconGlyph` 常用值

这些是 Segoe Fluent Icons 字体中的 Unicode 码点，可在右键菜单和工具栏中使用：

| 图标 | Glyph | 含义 |
|---|---|---|
| `\uE8A5` | 📄 | 文件 |
| `\uE8B7` | 📁 | 文件夹 |
| `\uE8C8` | 📋 | 复制 |
| `\uE77F` | 📌 | 粘贴 |
| `\uE74D` | ✂️ | 剪切 |
| `\uE8AC` | 🗑️ | 删除 |
| `\uE946` | ℹ️ | 信息 |
| `\uE756` | 💻 | 终端/命令 |
| `\uE721` | 🔍 | 搜索 |
| `\uE712` | ··· | 更多选项 |
| `\uE8BD` | 💬 | 消息/对话 |
| `\uE710` | ➕ | 新建 |
| `\uE8B9` | ⚙️ | 设置/性能 |
| `\uE90F` | 📝 | 属性 |
| `\uE72C` | 🔄 | 刷新 |
| `\uE8E5` | 📂 | 打开 |

完整的图标列表可参考[WinUI 3 Gallery](https://apps.microsoft.com/detail/9p3jfpwwdzrc?hl=zh-CN&gl=CN)的Designs->Iconography部分，或参考[WinUI 3 Fluent Icons](https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font)。

---

## 常见问题

### Q: 外部插件在 Release 版本中无法加载？

Release 版本启用了 `PublishTrimmed`，会移除未直接引用的代码。如果外部插件使用了未在主程序中出现的类型，可能会失败。建议：

- 外部插件尽量使用基础接口和常用类型
- 必要时在主程序的 `TrimmerRoots.xml` 中添加保留规则

### Q: 如何访问主程序的 ViewModel？

通过 `App.SharedViewModel` 可以访问 `MainWindowViewModel`（但这是内部 API，可能随版本变化）。更稳定的方式是使用 `ExtensionContext.Services` 获取已注册的服务。

### Q: 插件应该放在哪里？

- 内置插件：放在 `FastFluentFilesFolders/Extensions/Extensions/` 目录下
- 外部插件：打包为 `.zip`，通过设置页导入，解压到 `%LocalAppData%\\FastFluentFilesFolders\\Plugins\{id}\`

### Q: 插件可以调用 Win32 API 吗？

可以。项目已启用 `AllowUnsafeBlocks = true`，插件可以使用 P/Invoke 调用 Windows API。但注意托盘图标的窗口句柄需要通过 `App.MainWindow` 获取。
