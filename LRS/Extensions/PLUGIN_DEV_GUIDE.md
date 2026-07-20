# LRS Plugin Development Guide

## Table of Contents

1. [Overview](#overview)
2. [Plugin Architecture](#plugin-architecture)
3. [Quick Start](#quick-start)
4. [Extension Points](#extension-points)
5. [Packaging & Distribution](#packaging--distribution)
6. [Multi-Language Support](#multi-language-support)
7. [Theme Extensions](#theme-extensions)
8. [Complete Example](#complete-example)
9. [API Reference](#api-reference)

---

## Overview

The LRS plugin system allows you to extend the file manager by implementing standard interfaces. Plugins can:

- Add custom commands to right-click context menus
- Add buttons to the toolbar
- Add configuration cards to the settings page
- Intercept file operations (copy, delete, rename, etc.)
- Provide custom themes (colors, icons, etc.)

Plugins come in two forms:

| Type | Description | Registration |
|---|---|---|
| **Built-in** | Compiled with the main app | Register in DI container in `App.xaml.cs` |
| **External** | Distributed as a `.zip` package | Installed via the "Import Plugin" button in Settings |

---

## Plugin Architecture

```
IExtension (base interface)
  ├── IContextMenuExtension   Context menu
  ├── IToolbarExtension       Toolbar buttons
  ├── ISettingsExtension      Settings cards
  ├── IFileOperationHook      File operation hooks
  └── ICustomThemeExtension   Custom themes
```

Every plugin must implement `IExtension`, then optionally implement one or more of the above extension point interfaces.

---

## Quick Start

### Step 1: Create a Project

Create a .NET 8 class library referencing these NuGet packages:

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

> **Important**: Your plugin project must reference the LRS main program DLL (`LRS.dll`) to access `IExtension`, `ExtensionContext`, `FileSystemNodeViewModel`, etc. Copy `LRS.dll` from the main program's build output to your plugin's reference path.

### Step 2: Implement the Base Interface

```csharp
using LRS.Extensions.Interfaces;
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
        return Task.CompletedTask;
    }

    public Task ShutdownAsync() => Task.CompletedTask;
}
```

### Step 3: Implement Extension Points

A single plugin can implement multiple extension point interfaces:

```csharp
public class HelloPlugin : IExtension, IContextMenuExtension, ISettingsExtension
{
    // IExtension implementation...
    
    public IEnumerable<ExtensionMenuItem> GetMenuItems(
        FileSystemNodeViewModel? targetNode,
        ContextMenuLocation location)
    {
        // Return menu items...
    }
    
    public IEnumerable<UIElement> CreateSettingsCards()
    {
        // Return settings cards...
    }
}
```

### Step 4: Register Built-in Plugin (Development Phase)

Add to the DI configuration in `App.xaml.cs`:

```csharp
services.AddSingleton<IExtension, HelloPlugin>();
```

Build and run — the plugin will be loaded automatically.

---

## Extension Points

### Context Menu (`IContextMenuExtension`)

Add commands to file/folder or background right-click menus.

```csharp
using LRS.Extensions.Interfaces;
using LRS.ViewModels;
using System.Collections.Generic;
using System.Windows.Input;

public interface IContextMenuExtension : IExtension
{
    IEnumerable<ExtensionMenuItem> GetMenuItems(
        FileSystemNodeViewModel? targetNode,
        ContextMenuLocation location);
}
```

#### `ContextMenuLocation` Enum

| Value | Description |
|---|---|
| `FileItem` | A file was right-clicked |
| `FolderItem` | A folder was right-clicked |
| `Background` | Empty area was right-clicked |
| `MultipleSelection` | Multiple items are selected |

#### `ExtensionMenuItem` Properties

| Property | Type | Description |
|---|---|---|
| `Header` | `string` | Display text (supports localization key) |
| `IconGlyph` | `string?` | Segoe Fluent icon glyph code (e.g. `"\uE8A5"`) |
| `ThemedIconKey` | `string?` | Themed icon style key (e.g. `"Icon.Archive"`) |
| `Command` | `ICommand?` | Command to execute on click |
| `CommandParameter` | `object?` | Parameter passed to the command |
| `IsSeparator` | `bool` | Whether this is a separator line |
| `SubItems` | `List<ExtensionMenuItem>?` | Sub-menu items (supports nesting) |

#### Example

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
            Header = "Output File Path",
            IconGlyph = "\uE8C8",
            Command = new RelayCommand(() =>
            {
                System.Diagnostics.Debug.WriteLine($"Selected: {filePath}");
            })
        };
        
        // Sub-menu example
        yield return new ExtensionMenuItem
        {
            Header = "Advanced",
            IconGlyph = "\uE712",
            SubItems = new List<ExtensionMenuItem>
            {
                new() { Header = "Action A", IconGlyph = "\uE8BD", Command = DoA },
                new() { IsSeparator = true },
                new() { Header = "Action B", IconGlyph = "\uE946", Command = DoB },
            }
        };
    }
}
```

---

### Toolbar (`IToolbarExtension`)

Add buttons to the file explorer's top toolbar area.

```csharp
public interface IToolbarExtension : IExtension
{
    IEnumerable<ToolbarItem> GetToolbarItems();
}
```

#### `ToolbarItem` Properties

| Property | Type | Description |
|---|---|---|
| `Header` | `string` | Button text |
| `IconGlyph` | `string?` | Segoe Fluent icon glyph code |
| `Command` | `ICommand?` | Click command |
| `CommandParameter` | `object?` | Command parameter |
| `ToolTip` | `string?` | Mouse hover tooltip |

#### Example

```csharp
public IEnumerable<ToolbarItem> GetToolbarItems()
{
    yield return new ToolbarItem
    {
        Header = "Open Terminal",
        IconGlyph = "\uE756",
        ToolTip = "Open terminal at current location",
        Command = new RelayCommand(() =>
        {
            var path = App.SharedViewModel.SelectedFolder?.FullPath ?? "C:\\";
            System.Diagnostics.Process.Start("cmd.exe", $"/k cd /d \"{path}\"");
        })
    };
}
```

---

### Settings Page (`ISettingsExtension`)

Add custom configuration cards to the settings page.

```csharp
public interface ISettingsExtension : IExtension
{
    IEnumerable<UIElement> CreateSettingsCards();
}
```

#### Example

```csharp
public IEnumerable<UIElement> CreateSettingsCards()
{
    yield return new SettingsCard
    {
        Header = "My Plugin Settings",
        HeaderIcon = new FontIcon { Glyph = "\uE946" },
        Description = "Plugin-specific configuration goes here.",
        Content = new ToggleSwitch 
        { 
            IsOn = true,
            OnContent = "On",
            OffContent = "Off"
        }
    };
}
```

> Any WinUI 3 control can be used (`TextBox`, `ToggleSwitch`, `NumberBox`, `ComboBox`, etc.). Use `CommunityToolkit.WinUI.Controls.SettingsCard` to maintain visual consistency with the main app.

---

### File Operation Hooks (`IFileOperationHook`)

Intercept file operations (copy, move, delete, rename, create). Execute custom logic before or after operations.

```csharp
public interface IFileOperationHook : IExtension
{
    Task<bool> BeforeExecute(FileOperationContext context);
    Task AfterExecute(FileOperationContext context, bool success);
}
```

#### `FileOperationType` Enum

| Value | Description |
|---|---|
| `Copy` | Copy file/folder |
| `Move` | Move file/folder |
| `Delete` | Delete file/folder |
| `Rename` | Rename |
| `Create` | Create new file/folder |

#### `FileOperationContext` Properties

| Property | Type | Description |
|---|---|---|
| `OperationType` | `FileOperationType` | Operation type |
| `SourcePath` | `string` | Source file path |
| `DestinationPath` | `string?` | Target path (for copy/move/rename) |
| `IsDirectory` | `bool` | Whether the target is a directory |
| `IsCancelled` | `bool` | Set to `true` to cancel the operation |

#### Example

```csharp
public async Task<bool> BeforeExecute(FileOperationContext context)
{
    if (context.OperationType == FileOperationType.Delete)
    {
        var fileName = System.IO.Path.GetFileName(context.SourcePath);
        if (fileName == "Important.txt")
        {
            System.Diagnostics.Debug.WriteLine("Blocking deletion of important file!");
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
            $"Operation completed: {context.OperationType} - {context.SourcePath}");
    }
}
```

> **Status**: `IFileOperationHook` is defined and accessible via `PluginManager.RunBeforeOperationHooks` and `PluginManager.RunAfterOperationHooks`. Full integration into the app's file operation commands is planned for a future release.

---

### Theme Extensions (`ICustomThemeExtension`)

Provide custom WinUI 3 resource dictionaries to change application colors, fonts, icons, etc.

```csharp
public interface ICustomThemeExtension : IExtension
{
    ResourceDictionary? GetThemeResources();
    string ThemeName { get; }
}
```

#### Example

```csharp
public class DarkGreenTheme : ICustomThemeExtension
{
    public string Id => "com.example.DarkGreen";
    public string DisplayName => "DarkGreenTheme.Name";
    public Version Version => new(1, 0, 0);
    public string ThemeName => "Dark Green";

    public ResourceDictionary? GetThemeResources()
    {
        var dict = new ResourceDictionary();
        dict.Add("SystemAccentColor", Microsoft.UI.Colors.LimeGreen);
        dict.Add("SystemAccentColorLight1", Microsoft.UI.Colors.PaleGreen);
        return dict;
    }
}
```

> **Status**: `ICustomThemeExtension` is defined and accessible via `PluginManager.GetThemePlugins()`. Automatic theme application will be improved in a future release.

---

## Packaging & Distribution

### Package Structure

Package your plugin as `.zip` (or `.lrsplug`):

```
MyPlugin.zip  (or MyPlugin.lrsplug)
  ├── plugin.json          # Manifest file (required)
  └── MyPlugin.dll         # Plugin assembly (required)
```

> If your plugin has additional dependency DLLs, include them in the zip root as well.

### `plugin.json` Manifest Format

```json
{
    "id": "com.example.MyPlugin",
    "name": "MyPlugin",
    "version": "1.0.0",
    "author": "Your Name",
    "description": "A plugin for LRS file explorer.",
    "entryAssembly": "MyPlugin.dll",
    "minAppVersion": "0.0.7"
}
```

| Field | Required | Description |
|---|---|---|
| `id` | Yes | Globally unique identifier; use reverse-domain format, e.g. `com.yourname.pluginname` |
| `name` | Yes | Plugin display name (supports localization key) |
| `version` | Yes | Semantic version, e.g. `1.0.0` |
| `author` | No | Author name |
| `description` | No | Plugin description (supports localization key) |
| `entryAssembly` | Yes | Entry point DLL filename; must include `.dll` extension |
| `minAppVersion` | No | Minimum required LRS version, e.g. `0.0.7` |

### Installation

1. Open LRS → Settings
2. Find the **"Plugin Management"** section
3. Click **"Select .zip to Import"**
4. Choose your plugin `.zip` file
5. The system automatically validates, extracts, and loads the plugin

Plugins are installed to `%LocalAppData%\LRS\Plugins\{plugin-id}\` and auto-load when LRS restarts.

### Uninstallation

In the Settings page's "Installed Plugins" list, click the delete button next to the plugin you wish to remove.

### Debugging Tips

- Use `System.Diagnostics.Debug.WriteLine()` for debug logging
- After installation, check `%LocalAppData%\LRS\Plugins\{id}\` to verify correct extraction
- If loading fails, a specific error message is shown (missing manifest, incompatible DLL, etc.)

---

## Multi-Language Support

Plugins retrieve localized text via `ExtensionContext.GetString(key)`:

```csharp
public Task InitializeAsync(ExtensionContext context)
{
    _ctx = context;
    var hello = context.GetString("MyPlugin.Hello");
    return Task.CompletedTask;
}
```

### Adding Localized Strings

Two approaches:

#### Option 1: Register with the Main App (Built-in Plugins)

Add your plugin's string keys to `Strings/zh-Hans.json` and `Strings/en.json`:

```json
{
    "MyPlugin.Name": "My Plugin",
    "MyPlugin.SettingsHeader": "Plugin Settings",
    "MyPlugin.SayHello": "Hello!"
}
```

#### Option 2: Self-Contained Language Files (External Plugins)

Bundle language JSON files with your plugin and load them manually in `InitializeAsync`:

```csharp
public Task InitializeAsync(ExtensionContext context)
{
    var langDir = Path.Combine(pluginInstallPath, "lang");
    // Implement your own loading logic
}
```

> The current `LocalizationService` supports single JSON file loading only. External plugins must manage multi-language strings in plugin code. A `RegisterStrings` API is planned for a future release.

---

## Complete Example

A feature-rich plugin demonstrating context menu, settings page, and message dialogs:

```csharp
using CommunityToolkit.WinUI.Controls;
using LRS.Extensions.Interfaces;
using LRS.ViewModels;
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
                Header = "File Info",
                IconGlyph = "\uE946",
                SubItems = new List<ExtensionMenuItem>
                {
                    new() { Header = $"Name: {node.Name}", IsSeparator = false },
                    new() { Header = $"Size: {node.VisualSize}", IsSeparator = false },
                    new() { Header = $"Path: {node.FullPath}", IsSeparator = false },
                    new() { IsSeparator = true },
                    new()
                    {
                        Header = "Open in Terminal",
                        IconGlyph = "\uE756",
                        Command = new RelayCommand<string>(
                            path => Debug.WriteLine($"Terminal: {path}"))
                    }
                }
            };
        }
    }

    public IEnumerable<UIElement> CreateSettingsCards()
    {
        yield return new SettingsCard
        {
            Header = "File Info Plugin",
            HeaderIcon = new FontIcon { Glyph = "\uE946" },
            Description = "Displays detailed file information in the context menu.",
            Content = new TextBlock 
            { 
                Text = "Enabled",
                VerticalAlignment = VerticalAlignment.Center 
            }
        };
    }
}

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

## API Reference

### `IExtension` — Base Interface

All plugins must implement this.

```csharp
public interface IExtension
{
    string Id { get; }                    // Globally unique identifier
    string DisplayName { get; }           // Display name (can be a localization key)
    Version Version { get; }              // Semantic version number
    
    Task InitializeAsync(ExtensionContext context);  // Initialization
    Task ShutdownAsync();                            // Cleanup
}
```

### `ExtensionContext` — Plugin Context

Passed in `InitializeAsync`, provides access to main app services.

```csharp
public class ExtensionContext
{
    public IServiceProvider Services { get; }            // DI container
    public DispatcherQueue UIDispatcherQueue { get; }    // UI thread dispatcher
    public Configs AppConfigs { get; }                   // App configuration
    public LocalizationService LocalizationService { get; } // Localization service
    
    public string GetString(string key);                 // Get localized string
}
```

### `FileSystemNodeViewModel` — File/Folder Node

The type of `targetNode` in context menu extensions. Contains complete file/folder information.

| Property | Type | Description |
|---|---|---|
| `Name` | `string` | File/folder name |
| `FullPath` | `string` | Full path |
| `IsDirectory` | `bool` | Whether it is a directory |
| `IsPlaceholder` | `bool` | Whether it is a placeholder node |
| `Extension` | `string` | File extension (includes the dot) |
| `ExactSize` | `long` | Exact size in bytes |
| `VisualSize` | `string` | Human-readable size (e.g. "1.2 MB") |
| `LastModifiedTime` | `DateTime` | Last modified time |
| `FirstCreatedTime` | `DateTime` | Creation time |
| `Icon` | `ImageSource?` | File icon |
| `Children` | `ObservableCollection<FileSystemNodeViewModel>` | Child node collection |
| `IsLoaded` | `bool` | Whether children have been loaded |

### UI Thread Safety

All UI operations must run on the UI thread. Use `ExtensionContext.UIDispatcherQueue`:

```csharp
_ctx.UIDispatcherQueue.TryEnqueue(() =>
{
    var dialog = new ContentDialog { ... };
    _ = dialog.ShowAsync();
});
```

> Never touch `ObservableCollection` or XAML-bound properties from a background thread.

### Common `IconGlyph` Values

These are Unicode codepoints from the Segoe Fluent Icons font, usable in context menus and toolbars.
You can browse all available icons in **WinUI 3 Gallery → Design → Iconography**.

| Icon | Glyph | Meaning |
|---|---|---|
| `\uE8A5` | 📄 | File |
| `\uE8B7` | 📁 | Folder |
| `\uE8C8` | 📋 | Copy |
| `\uE77F` | 📌 | Paste |
| `\uE74D` | ✂️ | Cut |
| `\uE8AC` | 🗑️ | Delete |
| `\uE946` | ℹ️ | Info |
| `\uE756` | 💻 | Terminal |
| `\uE721` | 🔍 | Search |
| `\uE712` | ··· | More options |
| `\uE8BD` | 💬 | Message |
| `\uE710` | ➕ | New |
| `\uE8B9` | ⚙️ | Performance |
| `\uE90F` | 📝 | Properties |
| `\uE72C` | 🔄 | Refresh |
| `\uE8E5` | 📂 | Open |

Full icon reference: [WinUI 3 Fluent Icons](https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font)

---

## FAQ

### Q: External plugins fail to load in Release builds?

Release builds enable `PublishTrimmed`, which removes unreferenced code. If your plugin uses types not directly referenced by the main app, it may fail. Recommendations:

- External plugins should stick to basic interfaces and common types
- Add preservation rules in `TrimmerRoots.xml` when necessary

### Q: How do I access the main app's ViewModel?

Use `App.SharedViewModel` to access `MainWindowViewModel` (this is an internal API and may change between versions). A more stable approach is to use `ExtensionContext.Services` to get registered services.

### Q: Where should plugins be placed?

- **Built-in plugins**: in the `LRS/Extensions/Extensions/` directory
- **External plugins**: packaged as `.zip`, imported via Settings, extracted to `%LocalAppData%\LRS\Plugins\{id}\`

### Q: Can plugins call Win32 APIs?

Yes. The project has `AllowUnsafeBlocks = true` enabled. Plugins can use P/Invoke to call Windows APIs. Use `App.MainWindow` to obtain the window handle when needed.
