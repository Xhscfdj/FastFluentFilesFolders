using LRS.Extensions.Interfaces;
using LRS.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;

namespace LRS.Extensions.Extensions
{
    public class ArchivePlugin : IContextMenuExtension, ISettingsExtension
    {
        private bool _isInitialized;
        private ExtensionContext? _ctx;

        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz",
            ".cab", ".iso", ".tgz", ".tar.gz", ".tar.bz2", ".tar.xz",
            ".lzh", ".arj", ".z", ".lz", ".lzma", ".lzo", ".sz", ".zst"
        };

        public string Id => "LRS.ArchivePlugin";
        public string DisplayName => "ArchivePlugin.Name";
        public Version Version => new(1, 0, 0);

        public Task InitializeAsync(ExtensionContext context)
        {
            _ctx = context;
            _isInitialized = true;
            Debug.WriteLine("[ArchivePlugin] Initialized.");
            return Task.CompletedTask;
        }

        public Task ShutdownAsync()
        {
            _isInitialized = false;
            return Task.CompletedTask;
        }

        public IEnumerable<ExtensionMenuItem> GetMenuItems(
            FileSystemNodeViewModel? targetNode,
            ContextMenuLocation location)
        {
            if (!_isInitialized || _ctx == null || targetNode == null) yield break;

            if (location == ContextMenuLocation.FileItem || location == ContextMenuLocation.FolderItem)
            {
                bool isArchive = !targetNode.IsDirectory && IsArchiveFile(targetNode.Extension);

                if (isArchive)
                {
                    yield return new ExtensionMenuItem
                    {
                        Header = _ctx.GetString("ArchivePlugin.Extract"),
                        ThemedIconKey = "Icon.Archive",
                        Command = new AsyncRelayCommand(async () => await ExtractArchive(targetNode)),
                        CommandParameter = targetNode
                    };
                }
                else
                {
                    yield return new ExtensionMenuItem
                    {
                        Header = _ctx.GetString("ArchivePlugin.Compress"),
                        ThemedIconKey = "Icon.Archive",
                        SubItems = new List<ExtensionMenuItem>
                        {
                            new()
                            {
                                Header = _ctx.GetString("ArchivePlugin.CompressToZip"),
                                IconGlyph = "\uE8B7",
                                Command = new AsyncRelayCommand(async () => await CompressTo(targetNode, "zip")),
                            },
                            new()
                            {
                                Header = _ctx.GetString("ArchivePlugin.CompressTo7z"),
                                IconGlyph = "\uE8B7",
                                Command = new AsyncRelayCommand(async () => await CompressTo(targetNode, "7z")),
                            },
                        }
                    };
                }
            }

            if (location == ContextMenuLocation.Background)
            {
                yield return new ExtensionMenuItem
                {
                    Header = _ctx.GetString("ArchivePlugin.ExtractHere"),
                    ThemedIconKey = "Icon.Archive",
                    Command = new AsyncRelayCommand(async () => await ExtractFromDialog()),
                };
            }
        }

        private bool IsArchiveFile(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return false;
            return ArchiveExtensions.Contains(extension);
        }

        private async Task CompressTo(FileSystemNodeViewModel target, string format)
        {
            try
            {
                var ext = format == "7z" ? ".7z" : ".zip";
                var outputPath = Path.Combine(
                    Path.GetDirectoryName(target.FullPath) ?? target.FullPath,
                    Path.GetFileNameWithoutExtension(target.Name) + ext);

                outputPath = GetUniquePath(outputPath);

                await Task.Run(() =>
                {
                    if (File.Exists(outputPath)) File.Delete(outputPath);

                    if (format == "zip")
                    {
                        using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);
                        var basePath = target.FullPath;
                        if (target.IsDirectory)
                        {
                            foreach (var file in Directory.GetFiles(basePath, "*", SearchOption.AllDirectories))
                            {
                                var entryName = file.Substring(basePath.Length).TrimStart('\\', '/');
                                zip.CreateEntryFromFile(file, entryName);
                            }
                        }
                        else
                        {
                            zip.CreateEntryFromFile(basePath, target.Name);
                        }
                    }
                    else
                    {
                        CompressWith7z(target.FullPath, outputPath);
                    }
                });

                await ShowMessageAsync(
                    _ctx!.GetString("ArchivePlugin.Success"),
                    string.Format(_ctx.GetString("ArchivePlugin.CompressSuccessFmt"),
                        Path.GetFileName(outputPath)));

                _ = _ctx.UIDispatcherQueue.EnqueueAsync(() =>
                    App.SharedViewModel.AddItemToCurrentView(outputPath, false));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchivePlugin] Compress failed: {ex.Message}");
                await ShowMessageAsync(_ctx!.GetString("ArchivePlugin.Failed"), ex.Message);
            }
        }

        private async Task ExtractArchive(FileSystemNodeViewModel target)
        {
            try
            {
                var destDir = Path.Combine(
                    Path.GetDirectoryName(target.FullPath)!,
                    Path.GetFileNameWithoutExtension(target.Name));

                destDir = GetUniquePath(destDir);

                await Task.Run(() =>
                {
                    var ext = target.Extension.ToLowerInvariant();
                    if (ext == ".zip")
                    {
                        Directory.CreateDirectory(destDir);
                        ZipFile.ExtractToDirectory(target.FullPath, destDir);
                    }
                    else
                    {
                        ExtractWith7z(target.FullPath, destDir);
                    }
                });

                await ShowMessageAsync(
                    _ctx!.GetString("ArchivePlugin.Success"),
                    string.Format(_ctx.GetString("ArchivePlugin.ExtractSuccessFmt"), destDir));

                _ = _ctx.UIDispatcherQueue.EnqueueAsync(async () =>
                    await App.SharedViewModel.RefreshCurrentFolderAsync());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchivePlugin] Extract failed: {ex.Message}");
                await ShowMessageAsync(_ctx!.GetString("ArchivePlugin.Failed"), ex.Message);
            }
        }

        private async Task ExtractFromDialog()
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                picker.FileTypeFilter.Add(".zip");
                picker.FileTypeFilter.Add(".7z");
                picker.FileTypeFilter.Add(".rar");
                picker.FileTypeFilter.Add(".tar");
                picker.FileTypeFilter.Add(".gz");
                picker.FileTypeFilter.Add(".bz2");

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                var destDir = Path.Combine(
                    Path.GetDirectoryName(file.Path)!,
                    Path.GetFileNameWithoutExtension(file.Path));

                destDir = GetUniquePath(destDir);

                await Task.Run(() =>
                {
                    var ext = Path.GetExtension(file.Path).ToLowerInvariant();
                    if (ext == ".zip")
                    {
                        Directory.CreateDirectory(destDir);
                        ZipFile.ExtractToDirectory(file.Path, destDir);
                    }
                    else
                    {
                        ExtractWith7z(file.Path, destDir);
                    }
                });

                await ShowMessageAsync(
                    _ctx!.GetString("ArchivePlugin.Success"),
                    string.Format(_ctx.GetString("ArchivePlugin.ExtractSuccessFmt"), destDir));

                _ = _ctx.UIDispatcherQueue.EnqueueAsync(async () =>
                    await App.SharedViewModel.RefreshCurrentFolderAsync());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchivePlugin] Extract failed: {ex.Message}");
                await ShowMessageAsync(_ctx!.GetString("ArchivePlugin.Failed"), ex.Message);
            }
        }

        private static void CompressWith7z(string sourcePath, string outputPath)
        {
            var sevenZipPath = Find7zPath();
            var isDir = Directory.Exists(sourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var psi = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = isDir
                    ? $"a -tzip \"{outputPath}\" \"{sourcePath}\\*\" -mx5 -y"
                    : $"a -tzip \"{outputPath}\" \"{sourcePath}\" -mx5 -y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi)!;
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var err = process.StandardError.ReadToEnd();
                throw new InvalidOperationException($"7z compress failed: {err}");
            }
        }

        private static void ExtractWith7z(string archivePath, string destDir)
        {
            var sevenZipPath = Find7zPath();
            Directory.CreateDirectory(destDir);

            var psi = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = $"x \"{archivePath}\" -o\"{destDir}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi)!;
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var err = process.StandardError.ReadToEnd();
                throw new InvalidOperationException($"7z extract failed: {err}");
            }
        }

        private static string Find7zPath()
        {
            string[] paths =
            {
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
            };

            foreach (var p in paths)
            {
                if (File.Exists(p)) return p;
            }

            try
            {
                var psi = new ProcessStartInfo("where", "7z.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(500);
                if (p?.ExitCode == 0) return "7z.exe";
            }
            catch { }

            throw new InvalidOperationException(
                "7-Zip is not installed. Please install 7-Zip from https://7-zip.org/\n" +
                "7-Zip 未安装。请访问 https://7-zip.org/ 安装。");
        }

        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return path;

            var dir = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);

            int i = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(dir, $"{name} ({i}){ext}");
                i++;
            }
            while (File.Exists(newPath) || Directory.Exists(newPath));

            return newPath;
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            await _ctx!.UIDispatcherQueue.EnqueueAsync(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = App.MainWindow!.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };
                await dialog.ShowAsync();
            });
        }

        public IEnumerable<UIElement> CreateSettingsCards()
        {
            if (_ctx == null) yield break;

            yield return new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header = _ctx.GetString("ArchivePlugin.SettingsHeader"),
                HeaderIcon = new FontIcon { Glyph = "\uE8B7" },
                Description = _ctx.GetString("ArchivePlugin.SettingsDescription"),
                Content = new TextBlock
                {
                    Text = _ctx.GetString("ArchivePlugin.SupportedFormats"),
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }
    }
}
