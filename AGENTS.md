# AGENTS.md — LRS (LiveRootStorage)

## Project identity
- WinUI 3 file explorer (Windows App SDK 2.2), target `net8.0-windows10.0.19041.0`
- Single-project solution: `LRS.slnx` → `LRS/LRS.csproj`
- Architecture: **MVVM** with `CommunityToolkit.Mvvm` source generators

## Build & run
```powershell
dotnet build
dotnet run --project LRS
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
