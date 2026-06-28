# LRS (LiveRootStorage)

[English](README.md) | 中文

基于 WinUI 3 和 Windows App SDK 构建的新一代 Windows 文件资源管理器。

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)](https://www.microsoft.com/windows)

## 功能特性

- **文件树侧边栏** -- 以可折叠的树状视图浏览所有驱动器和文件夹，子目录按需懒加载
- **可排序的文件列表** -- 四列表格（名称、修改日期、创建日期、大小），支持点击列标题排序和拖拽调整列宽
- **面包屑导航** -- 自定义面包屑栏，支持前进/后退/向上历史记录、可编辑地址栏和子文件夹弹出菜单
- **行内重命名** -- 点击文件或文件夹名称即可行内重命名（Enter 确认，Escape 取消）
- **右键菜单** -- 右键菜单支持文件操作（剪切、复制、粘贴、重命名、删除）和新建项目（新建文件夹、新建文本文档）
- **文件操作** -- 通过 Windows 剪贴板实现完整的剪切/复制/粘贴功能
- **主题感知图标** -- 多层图标渲染，可自适应 Windows 浅色/深色主题
- **可配置** -- 设置面板支持首页路径、默认排序方式、行高和图标提供程序选项
- **Mica 材质** -- 原生 Windows 11 云母材质和标题栏一体化设计
- **按时间分组** -- 文件按时间段分组（今天、昨天、本周等）

## 环境要求

- Windows 10 版本 19041 (20H1) 或更高 / Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 构建与运行

```powershell
dotnet build
dotnet run --project LRS
```

提供两种启动配置：
- **MsixPackage** -- 打包（MSIX）部署
- **Project** -- 解包模式，直接运行

## 架构

LRS 遵循 **MVVM** 模式，使用 `CommunityToolkit.Mvvm` 源代码生成器。

| 层级 | 技术 |
|-------|------|
| UI | WinUI 3 (Windows App SDK 2.2) |
| ViewModel | `[ObservableProperty]`、`[RelayCommand]` 源代码生成器 |
| 依赖注入 | `Microsoft.Extensions.Hosting` |
| 图标 | Win32 `SHGetFileInfo`（主要）/ WinRT `StorageFile.GetThumbnailAsync()`（备用） |
| 配置 | JSON，支持 `IChangeToken` 实时重载 |

### 核心组件

| 组件 | 路径 |
|------|------|
| 应用入口 / DI | `LRS/App.xaml.cs` |
| 主 ViewModel | `LRS/ViewModels/MainWindowViewModel.cs` |
| 文件树 | `LRS/Views/FileTreeView.xaml` |
| 文件列表 | `LRS/Views/MiddleFilesView.xaml` |
| 面包屑栏 | `LRS/Views/TopView.xaml` |
| 自定义面包屑控件 | `LRS/UserControls/LRSBreadcrumb.xaml` |
| 自定义数据表格 | `LRS/UserControls/TreeDataGrid.xaml` |
| 设置页面 | `LRS/Views/SettingsView.xaml` |
| 配置 | `LRS/ViewModels/Configs.cs` |

## 配置

运行时配置存储在 `%LocalAppData%\LRS\user_configs.json`。默认配置位于 `Configs/configs.json`。

可配置项（通过设置面板修改）：

| 设置项 | 默认值 |
|--------|-------|
| 首页路径 | `C:\` |
| 默认排序方式 | 修改日期（降序） |
| 行高 | 40px |
| Win32 图标 API | 开启 |
| 图标并行加载数 | 30 |

## 许可证

LRS 采用 [GNU General Public License v3.0](LICENSE.txt) 许可。

版权所有 (C) 2025

本程序为自由软件：您可以依据自由软件基金会发布的 GNU 通用公共许可证条款，将其重新分发和/或修改，无论是许可证的第 3 版还是（由您选择）任何更高版本。
