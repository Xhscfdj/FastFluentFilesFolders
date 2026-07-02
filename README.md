# LRS (LiveRootStorage)

English | [中文](README-zh.md)

A modern file explorer for Windows, built with WinUI 3 and the Windows App SDK.

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)](https://www.microsoft.com/windows)
## I want a starrrrrrrrrrr
If you like it, please give me a starrrrr, thank youuuuuuuuuu!!! meow~
starrr⭐rrratsDA☆ZE

## Install TIP (Very IMPORTANT!!)
Because this software is self-signed, so your computer may not trust this installer.  
If you want to make the installer trusted, you can download and install my [certification](https://www.mishui.city/upload/CertForLRS.cer).  
Then, you can run this powershell command:  
*The content enclosed in '<' and '>' needs to be replaced according to the actual situation.  
`Import-Certificate -FilePath "<Your certification file path>" -CertStoreLocation "Cert:\CurrentUser\TrustedPeople"`  

## Features
### Functions
- **File tree sidebar** -- browse all drives and folders in a collapsible tree view, with lazy-loaded subdirectories
- **Sortable file list** -- 4-column table (Name, Modified, Created, Size) with click-to-sort column headers and resizable columns
- **Breadcrumb navigation** -- custom breadcrumb bar with back/forward/up history, editable address bar, and sub-folder flyouts
- **Inline rename** -- click any file or folder name to rename it inline (Enter to commit, Escape to cancel)
- **Context menus** -- right-click menus for file operations (cut, copy, paste, rename, delete) and item creation (new folder, new text document)
- **File operations** -- full clipboard integration with cut/copy/paste via Windows clipboard
- **Theme-aware icons** -- multi-layered icon rendering that adapts to Windows light/dark theme
- **Configurable** -- settings panel for home path, default sort order, row height, and icon provider options
- **Mica backdrop** -- native Windows 11 materials with title bar integration
- **Time-based grouping** -- files grouped by time periods (Today, Yesterday, This Week, etc.)
- **There will be more functions...**
### Performance
- **Fast and fluent** -- the speed of loading directories which has hundreds even thousands of items. That's because we load the items' info asynchronously.

## Requirements

- Windows 10 version 19041 (20H1) or later / Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build & Run

```powershell
dotnet build
dotnet run --project LRS
```

Two launch profiles are available:
- **MsixPackage** -- packaged (MSIX) deployment
- **Project** -- unpackaged, runs directly

## Architecture

LRS follows the **MVVM** pattern using `CommunityToolkit.Mvvm` source generators.

| Layer | Technology |
|-------|-----------|
| UI | WinUI 3 (Windows App SDK 2.2) |
| ViewModels | `[ObservableProperty]`, `[RelayCommand]` source generators |
| DI | `Microsoft.Extensions.Hosting` |
| Icons | Win32 `SHGetFileInfo` (primary) / WinRT `StorageFile.GetThumbnailAsync()` (fallback) |
| Config | JSON with `IChangeToken` live reload |

### Key components

| Component | Path |
|-----------|------|
| App entry / DI | `LRS/App.xaml.cs` |
| Main ViewModel | `LRS/ViewModels/MainWindowViewModel.cs` |
| File tree | `LRS/Views/FileTreeView.xaml` |
| File list | `LRS/Views/MiddleFilesView.xaml` |
| Breadcrumb bar | `LRS/Views/TopView.xaml` |
| Custom breadcrumb control | `LRS/UserControls/LRSBreadcrumb.xaml` |
| Custom data grid | `LRS/UserControls/TreeDataGrid.xaml` |
| Settings | `LRS/Views/SettingsView.xaml` |
| Config | `LRS/ViewModels/Configs.cs` |

## Configuration

Runtime configuration is stored at `%LocalAppData%\LRS\user_configs.json`. Defaults are in `Configs/configs.json`.

Configurable options (available in the Settings panel):

| Setting | Default |
|---------|---------|
| Home page path | `C:\` |
| Default sort order | Modified (Descending) |
| Row height | 40px |
| Win32 icon API | Enabled |
| Icon parallel loading | 30 |

## License

LRS is licensed under the [GNU General Public License v3.0](LICENSE.txt).

Copyright (C) 2025

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
