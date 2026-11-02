# AGENTS.md — LRS (LiveRootStorage)

## Project identity
- WinUI 3 file explorer (Windows App SDK 2.2), target `net8.0-windows10.0.19041.0`
- Single-project solution: `LRS.slnx` → `LRS/LRS.csproj`
- Architecture: **MVVM** with `CommunityToolkit.Mvvm` source generators

## Build & run
```powershell
dotnet build
dotnet run --project
# At C:\Users\aaron\source\repos\LRS\FastFluentFilesFolders
```
Launch profiles in `Properties/launchSettings.json`: **MsixPackage** (packaged) and **Project** (unpackaged).

## Critical project settings
- `LangVersion` = `preview` — code may use C# preview features
- `Nullable` = **enabled**; `AllowUnsafeBlocks` = **true**
- `PublishTrimmed` = `true` for non-Debug builds — trim-incompatible patterns will fail in Release
- `DefaultLanguage` = `zh-Hans` — debug strings are Chinese

## DI & startup
Application entry: `App.xaml.cs`. `Microsoft.Extensions.Hosting` creates an `IHost` that registers `Configs` and `IIconProvider` as singletons in `App()` constructor. A static `ConfigureServices()` also exists but is **unused**.

The central ViewModel is `App.SharedViewModel` (type `MainWindowViewModel`), created in `OnLaunched` on the UI thread with a `DispatcherQueue`.

## XAML gotchas
New XAML files may need explicit `<Page Update>` entries in the `.csproj`:
```xml
<Page Update="Views\NewView.xaml">
  <Generator>MSBuild:Compile</Generator>
</Page>
```
The `XBindLeakFix` NuGet package is present for known x:Bind memory leaks.
I removed that package, and we just use "Binding" instead of "x:Bind".

## UI thread safety
All UI updates **must** go through `DispatcherQueue`:
```csharp
await _uiDispatcherQueue.EnqueueAsync(() => { /* UI work */ });
```
Never touch `ObservableCollection` or XAML-bound properties from a background thread.

## Configuration
Runtime config is `Configs/configs.json` (copied to output). The file `appSettings.json` is **empty/placeholder** — do not use it. `Configs.cs` supports live reload via `IChangeToken`.

## MVVM source generators
`CommunityToolkit.Mvvm` source generators are used:
- `[ObservableProperty]` — generates `public` property + `partial void OnXxxChanged`
- `[RelayCommand]` — generates `ICommand` from a method

Always run `dotnet build` to regenerate after editing partial classes with these attributes before inspecting generated code.

## Icon providers
Two implementations of `IIconProvider`:
- `WindowsIconProvider` — WinRT `StorageFile.GetThumbnailAsync()` (simpler, slower)
- `ShellIconHelper` — Win32 `SHGetFileInfo` with `ConcurrentDictionary` cache (default per `configs.json`)

Selection is per-node based on `Configs.ifUsesWin32APIToGetIcon`.

## Multi-language support
This project should support Chinese and English.
Don't forget to support multi-language when adding new things.

## Performance
Always keep the performance best.

## IMPORTANT(I use Chinese in this part)
每次文件操作后，保证在Grouped和非Grouped文件夹能立即看到结果，不能不刷新。

## Key files for navigation
| Concern | File |
|---|---|
| App entry / DI | `LRS/App.xaml.cs` |
| Central ViewModel | `LRS/ViewModels/MainWindowViewModel.cs` |
| File tree panel | `LRS/Views/FileTreeView.xaml` |
| File table panel | `LRS/Views/MiddleFilesView.xaml` |
| Breadcrumb bar | `LRS/Views/TopView.xaml` |
| Config live reload | `LRS/ViewModels/Configs.cs` |
| Custom breadcrumb control | `LRS/UserControls/LRSBreadcrumb.xaml` |

## No tests, lint, or CI
This repo has **no test projects**, no linter/formatter config, no CI pipelines. There is nothing to verify beyond `dotnet build`.

## Changelog
唯一的变更记录是仓库根目录的 `CHANGELOG.md`（`C:\Users\aaron\source\repos\LRS\CHANGELOG.md`）。每次成功的改动都要写进去，格式遵循 [Keep a Changelog 2.0.0](https://keepachangelog.com/en/2.0.0/)，版本号遵循 [语义化版本 2.0.0](https://semver.org/lang/zh-CN/spec/v2.0.0.html)。

文件骨架（标题与前言的措辞固定，不要改）：

```markdown
# Changelog

本文件记录本项目所有值得记录的变更。

格式遵循 [Keep a Changelog 2.0.0](https://keepachangelog.com/en/2.0.0/)，版本号遵循 [语义化版本 2.0.0](https://semver.org/lang/zh-CN/spec/v2.0.0.html)。

## [Unreleased]

### Added
- 一条变更一行说明
```

硬性要求：

1. **最新在最上**：新改动先写进 `## [Unreleased]`。发版时把 `## [Unreleased]` 原地改成 `## [x.y.z] - YYYY-MM-DD`（ISO 8601，月、日补零，如 `2026-09-25`），再在文件顶部补一个空的 `## [Unreleased]`，并同步更新底部的引用链接。
2. **只用六种分类**，标题按此固定顺序书写，某类没内容就整块省略：`### Added`、`### Changed`、`### Deprecated`、`### Removed`、`### Fixed`、`### Security`。不要新增 `Optimization`、`Performance`、`Internal`、`Dependencies` 之类的自定义类——性能优化、重构、依赖升级都写进 `Changed`，并在条目里说清效果（例如"分组文件夹构建少一次全量重排，约快 2 倍"）。
3. **条目格式**：一行一条，以 `- ` 开头；条目里**不写日期**（日期只在版本标题上），也不再写 `[Fix]` / `[Function:Add]` / `[Optimization]` 这类旧标签——变更类型由它所在的分类表达。
4. **破坏性变更**：在条目开头加 `**Breaking:**`，仍留在它所属的 `Changed` / `Removed` / `Security` 区块内，并说明破坏的是哪一层（配置文件字段、插件 API、安装包格式等），必要时给出升级动作。
5. **语言**：条目正文用中文；分类标题保持英文原文，方便工具解析。一条只讲一件事，先写"用户看到什么变了"，再补必要的实现要点（关键类型/文件、实测数据）。
6. **每个版本都要有链接定义**，放在文件末尾，`[Unreleased]` 指向 `HEAD`：
   ```
   [Unreleased]: https://github.com/Xhscfdj/FastFluentFilesFolders/compare/v1.1.1...HEAD
   [1.1.1]: https://github.com/Xhscfdj/FastFluentFilesFolders/compare/v1.1.0...v1.1.1
   ```
   最旧的版本没有可比较的前一版本，指向它的 tag：`.../releases/tag/v1.0.2`。
7. **版本号按语义化版本推进**：`Added` / `Changed` → MINOR，`Fixed` → PATCH，出现 `**Breaking:**` → MAJOR。
8. **已知问题不是一种变更**：写在受影响版本的标题下方，用引用块 `> 已知问题：...`；等它被修好时，再正常补一条 `Fixed`。
9. **只记录值得记录的**：用户能感知到的行为，或维护者必须知道的约束（构建配置、发布流程、兼容性）。改注释、跑格式化、纯内部重命名这类噪声不写。
10. **发版时直接从 CHANGELOG 复制**：把该版本区块原样作为 release notes，不要再维护第二份文案。

## Other things
默认使用中文对话。