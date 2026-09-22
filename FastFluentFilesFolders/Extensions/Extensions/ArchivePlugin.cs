using FastFluentFilesFolders.Extensions.Interfaces;
using FastFluentFilesFolders.Models;
using FastFluentFilesFolders.Services;
using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;

namespace FastFluentFilesFolders.Extensions.Extensions
{
    public class ArchivePlugin : IContextMenuExtension, ISettingsExtension, IArchiveBrowser
    {
        private bool _isInitialized;
        private ExtensionContext? _ctx;

        private static readonly object EncodingLock = new();
        private static bool _codePagesRegistered;

        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz",
            ".cab", ".iso", ".tgz", ".tar.gz", ".tar.bz2", ".tar.xz",
            ".lzh", ".arj", ".z", ".lz", ".lzma", ".lzo", ".sz", ".zst"
        };

        public string Id => "FastFluentFilesFolders.ArchivePlugin";
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

            // 压缩包内部的虚拟条目不支持直接压缩/解压（其 FullPath 为虚拟路径）
            if (targetNode.IsArchiveEntry) yield break;

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

        private static void EnsureCodePagesRegistered()
        {
            if (_codePagesRegistered) return;
            lock (EncodingLock)
            {
                if (_codePagesRegistered) return;
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _codePagesRegistered = true;
            }
        }

        private static Encoding GetSystemAnsiEncoding()
        {
            EnsureCodePagesRegistered();
            try
            {
                return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchivePlugin] Failed to get ANSI encoding: {ex.Message}");
                return Encoding.UTF8;
            }
        }

        // .NET 的 ZipArchive 对未设置 UTF-8 标志的条目默认按 UTF-8 解码，
        // 遇到 Windows 传统工具用系统代码页（如 GBK）生成的中文文件名会变成 U+FFFD。
        // 这里先用默认编码探测，一旦出现替换字符就回退到系统 ANSI 编码读取。
        private static Encoding GetZipEntryNameEncoding(string archivePath)
        {
            try
            {
                using var probe = ZipFile.OpenRead(archivePath);
                foreach (var entry in probe.Entries)
                {
                    if (entry.FullName.Contains('\uFFFD'))
                    {
                        return GetSystemAnsiEncoding();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchivePlugin] ZIP encoding probe failed: {ex.Message}");
            }

            return Encoding.UTF8;
        }

        private async Task CompressTo(FileSystemNodeViewModel target, string format)
        {
            var ext = format == "7z" ? ".7z" : ".zip";
            var outputName = Path.GetFileNameWithoutExtension(target.Name) + ext;
            var opItem = new FileOperationItem
            {
                Text = $"{_ctx.GetString("ArchivePlugin.Compress")} {outputName}",
                FileCount = 1,
                IconGlyph = "\uE7B8"
            };
            FileOperationReporter.ReportOperation(opItem);

            try
            {
                var outputPath = Path.Combine(
                    Path.GetDirectoryName(target.FullPath) ?? target.FullPath,
                    outputName);

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

                await NotifyItemCreatedAsync(outputPath, false);
                _ctx!.UIDispatcherQueue.TryEnqueue(() =>
                {
                    opItem.Progress = 100;
                    opItem.Process = "100%";
                    opItem.RemainTime = "0";
                    opItem.State = FileOperationState.Successful;
                });

                await ShowMessageAsync(
                    _ctx!.GetString("ArchivePlugin.Success"),
                    string.Format(_ctx.GetString("ArchivePlugin.CompressSuccessFmt"),
                        Path.GetFileName(outputPath)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchivePlugin] Compress failed: {ex.Message}");
                _ctx!.UIDispatcherQueue.TryEnqueue(() => { opItem.Progress = 0; opItem.Process = _ctx.GetString("ArchivePlugin.Failed"); opItem.State = FileOperationState.Error; });
                await ShowMessageAsync(_ctx!.GetString("ArchivePlugin.Failed"), ex.Message);
            }
        }

        private async Task ExtractArchive(FileSystemNodeViewModel target)
        {
            var opItem = new FileOperationItem
            {
                Text = $"{_ctx!.GetString("ArchivePlugin.ExtractHere")}: {target.Name}",
                FileCount = 1,
                IconGlyph = "\uE7B8"
            };
            FileOperationReporter.ReportOperation(opItem);

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
                        var entryNameEncoding = GetZipEntryNameEncoding(target.FullPath);
                        ZipFile.ExtractToDirectory(target.FullPath, destDir, entryNameEncoding);
                    }
                    else
                    {
                        ExtractWith7z(target.FullPath, destDir);
                    }
                });

                await NotifyItemCreatedAsync(destDir, true);
                _ctx!.UIDispatcherQueue.TryEnqueue(() =>
                {
                    opItem.Progress = 100;
                    opItem.Process = "100%";
                    opItem.RemainTime = "0";
                    opItem.State = FileOperationState.Successful;
                });

                await ShowMessageAsync(
                    _ctx!.GetString("ArchivePlugin.Success"),
                    string.Format(_ctx.GetString("ArchivePlugin.ExtractSuccessFmt"), destDir));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchivePlugin] Extract failed: {ex.Message}");
                _ctx!.UIDispatcherQueue.TryEnqueue(() => { opItem.Progress = 0; opItem.Process = _ctx.GetString("ArchivePlugin.Failed"); opItem.State = FileOperationState.Error; });
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
                        var entryNameEncoding = GetZipEntryNameEncoding(file.Path);
                        ZipFile.ExtractToDirectory(file.Path, destDir, entryNameEncoding);
                    }
                    else
                    {
                        ExtractWith7z(file.Path, destDir);
                    }
                });

                await NotifyItemCreatedAsync(destDir, true);

                await ShowMessageAsync(
                    _ctx!.GetString("ArchivePlugin.Success"),
                    string.Format(_ctx.GetString("ArchivePlugin.ExtractSuccessFmt"), destDir));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchivePlugin] Extract failed: {ex.Message}");
                await ShowMessageAsync(_ctx!.GetString("ArchivePlugin.Failed"), ex.Message);
            }
        }

        private async Task NotifyItemCreatedAsync(string createdPath, bool isDirectory)
        {
            try
            {
                var vm = App.SharedViewModel;
                var parentDir = Path.GetDirectoryName(createdPath);
                if (string.IsNullOrEmpty(parentDir)) return;

                var currentDir = vm.SelectedFolder?.FullPath ?? vm.CurrentBreadcrumbPath;
                if (!string.IsNullOrEmpty(currentDir) &&
                    string.Equals(
                        Path.TrimEndingDirectorySeparator(parentDir),
                        Path.TrimEndingDirectorySeparator(currentDir),
                        StringComparison.OrdinalIgnoreCase))
                {
                    await _ctx!.UIDispatcherQueue.EnqueueAsync(() =>
                    {
                        vm.AddItemToCurrentView(createdPath, isDirectory);
                        vm.RequestBreadcrumbRefresh();
                    });
                    return;
                }

                var parentNode = vm.FindNodeByPath(parentDir);
                if (parentNode != null)
                    await parentNode.ReloadChildrenAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchivePlugin] View update failed: {ex.Message}");
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
                    ? $"a -tzip -mcu=on -scsUTF-8 \"{outputPath}\" \"{sourcePath}\\*\" -mx5 -y"
                    : $"a -tzip -mcu=on -scsUTF-8 \"{outputPath}\" \"{sourcePath}\" -mx5 -y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi)!;
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"7z compress failed: {stdErrTask.Result}");
            }
        }

        private static void ExtractWith7z(string archivePath, string destDir)
        {
            var sevenZipPath = Find7zPath();
            Directory.CreateDirectory(destDir);

            var psi = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = $"x -scsUTF-8 \"{archivePath}\" -o\"{destDir}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi)!;
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"7z extract failed: {stdErrTask.Result}");
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

        private static string TryFind7zPath()
        {
            try { return Find7zPath(); }
            catch { return string.Empty; }
        }

        // ==================== IArchiveBrowser 压缩包预览 ====================

        public bool CanBrowse(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            if (!File.Exists(filePath)) return false;
            var ext = Path.GetExtension(filePath);
            if (!IsArchiveFile(ext)) return false;
            if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase)) return true;
            return !string.IsNullOrEmpty(TryFind7zPath());
        }

        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, string relativePath)
        {
            return Task.Run<IReadOnlyList<ArchiveEntry>>(() =>
            {
                var flat = ReadFlatEntries(archivePath);
                return BuildDirectChildren(flat, relativePath);
            });
        }

        public Task<string?> ExtractEntryToTempAsync(string archivePath, string relativePath)
        {
            return Task.Run<string?>(() =>
            {
                var safeName = string.Concat(Path.GetFileName(archivePath)
                    .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                var tempDir = Path.Combine(Path.GetTempPath(), "LRS_ArchivePreview",
                    safeName + "_" + Math.Abs(archivePath.GetHashCode()));
                Directory.CreateDirectory(tempDir);

                var fileName = Path.GetFileName(relativePath.Replace('/', '\\').TrimEnd('\\'));
                var outputPath = Path.Combine(tempDir, fileName);

                var ext = Path.GetExtension(archivePath).ToLowerInvariant();
                if (ext == ".zip")
                {
                    using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Read, GetZipEntryNameEncoding(archivePath));
                    var normalized = relativePath.Replace('\\', '/').TrimEnd('/');
                    var entry = archive.Entries.FirstOrDefault(e =>
                        e.FullName.TrimEnd('/').Equals(normalized, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return null;
                    entry.ExtractToFile(outputPath, true);
                }
                else
                {
                    var sevenZip = TryFind7zPath();
                    if (string.IsNullOrEmpty(sevenZip)) return null;
                    var psi = new ProcessStartInfo
                    {
                        FileName = sevenZip,
                        Arguments = $"e \"{archivePath}\" -o\"{tempDir}\" \"{relativePath.Replace('\\', '/')}\" -y",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var process = Process.Start(psi)!;
                    var stdOutTask = process.StandardOutput.ReadToEndAsync();
                    var stdErrTask = process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    if (process.ExitCode != 0) return null;
                }

                return File.Exists(outputPath) ? outputPath : null;
            });
        }

        // ---- 内部辅助 ----
        private readonly record struct FlatEntry(string Path, bool IsDirectory, long Size, DateTime Modified);

        private List<FlatEntry> ReadFlatEntries(string archivePath)
        {
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            return ext == ".zip" ? ReadZipEntries(archivePath) : Read7zEntries(archivePath);
        }

        private static List<FlatEntry> ReadZipEntries(string archivePath)
        {
            var result = new List<FlatEntry>();
            try
            {
                using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Read, GetZipEntryNameEncoding(archivePath));
                foreach (var e in archive.Entries)
                {
                    var path = e.FullName.Replace('/', '\\').TrimEnd('\\');
                    if (string.IsNullOrEmpty(path)) continue;
                    bool isDir = e.FullName.EndsWith("/") || string.IsNullOrEmpty(e.Name);
                    result.Add(new FlatEntry(path, isDir, e.Length, e.LastWriteTime.DateTime));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchivePlugin] ReadZipEntries failed: {ex.Message}");
            }
            return result;
        }

        private static List<FlatEntry> Read7zEntries(string archivePath)
        {
            var result = new List<FlatEntry>();
            var sevenZip = TryFind7zPath();
            if (string.IsNullOrEmpty(sevenZip)) return result;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"l -slt -scsUTF-8 \"{archivePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using var process = Process.Start(psi)!;
                var stdErrTask = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                string? path = null;
                bool isDir = false;
                long size = 0;
                DateTime modified = DateTime.MinValue;

                foreach (var rawLine in output.Split('\n'))
                {
                    var line = rawLine.TrimEnd('\r');
                    if (path != null && line.Length == 0)
                    {
                        result.Add(new FlatEntry(path.Replace('/', '\\').TrimEnd('\\'), isDir, size, modified));
                        path = null; isDir = false; size = 0; modified = DateTime.MinValue;
                        continue;
                    }
                    if (line.StartsWith("Path = "))
                        path = line.Substring(7).Trim();
                    else if (line.StartsWith("Size = "))
                        long.TryParse(line.Substring(7).Trim(), out size);
                    else if (line.StartsWith("Folder = ") && line.Substring(9).Trim() == "+")
                        isDir = true;
                    else if (line.StartsWith("Attributes = ") && line.Substring(13).Trim().StartsWith("D"))
                        isDir = true;
                    else if (line.StartsWith("Modified = "))
                        DateTime.TryParse(line.Substring(11).Trim(), out modified);
                }
                if (path != null)
                    result.Add(new FlatEntry(path.Replace('/', '\\').TrimEnd('\\'), isDir, size, modified));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchivePlugin] Read7zEntries failed: {ex.Message}");
            }
            return result;
        }

        private static IReadOnlyList<ArchiveEntry> BuildDirectChildren(List<FlatEntry> flat, string relativePath)
        {
            var prefix = string.IsNullOrEmpty(relativePath)
                ? string.Empty
                : relativePath.Replace('/', '\\').TrimEnd('\\') + "\\";

            var dirs = new Dictionary<string, ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            var files = new List<ArchiveEntry>();

            foreach (var entry in flat)
            {
                var full = entry.Path;
                if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var remainder = full.Substring(prefix.Length).TrimStart('\\');
                if (string.IsNullOrEmpty(remainder)) continue;

                var segments = remainder.Split('\\');
                var firstName = segments[0];
                var childRelative = prefix + firstName;

                if (segments.Length == 1 && !entry.IsDirectory)
                {
                    files.Add(new ArchiveEntry
                    {
                        Name = firstName,
                        RelativePath = childRelative,
                        IsDirectory = false,
                        Size = entry.Size,
                        LastModified = entry.Modified
                    });
                }
                else
                {
                    if (!dirs.ContainsKey(firstName))
                    {
                        dirs[firstName] = new ArchiveEntry
                        {
                            Name = firstName,
                            RelativePath = childRelative,
                            IsDirectory = true,
                            Size = 0,
                            LastModified = entry.Modified
                        };
                    }
                }
            }

            var list = new List<ArchiveEntry>(dirs.Count + files.Count);
            list.AddRange(dirs.Values);
            list.AddRange(files);
            return list;
        }

        // ==================== 压缩包预览结束 ====================

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
                    CloseButtonText = _ctx.GetString("CmdOk"),
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

            yield return new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header = _ctx.GetString("ArchivePlugin.PreviewSettingHeader"),
                HeaderIcon = new FontIcon { Glyph = "\uE8A5" },
                Description = _ctx.GetString("ArchivePlugin.PreviewSettingDescription")
            };
        }
    }
}
