using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace FastFluentFilesFolders.ViewModels
{
    // {Binding} 需要 ICustomProperty；Release/AOT 下反射版被关闭，必须由源生成器提供。
    [WinRT.GeneratedBindableCustomProperty]
    public partial class Configs : ObservableObject
    {
        public static readonly bool IsDebugMode = true;
        public static readonly string Version = "1.2.0";
        private static readonly string DefaultConfigPath =
            Path.Combine(AppContext.BaseDirectory, "Configs", "configs.json");

        private static readonly string UserConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastFluentFilesFolders");

        public static readonly string UserConfigPath =
            Path.Combine(UserConfigDir, "user_configs.json");

        public IConfiguration configuration;
		[ObservableProperty] public partial int MiddleFilesHeight { get; set; } = 40;
        [ObservableProperty] public partial bool IfUsesWin32APIToGetIcon { get; set; } = true;
        [ObservableProperty] public partial bool IfLimitIconLoadingConcurrency { get; set; } = false;
        // 0 = 自动（默认，使用安全并发上限 16，避免 Shell/GDI+ 并发过高偶发失败）；
        // >0 表示最多同时解码多少个图标
        [ObservableProperty] public partial int IconParallelLoadingCount { get; set; } = 0;
        [ObservableProperty] public partial string HomePageFullPath { get; set; } = DefaultDownloadsPath;
        [ObservableProperty] public partial string LastVisitedPath { get; set; } = "";
        [ObservableProperty] public partial string DefaultOrderMode { get; set; } = "ModifiedDesc";
        // 上次使用的排序状态（列 + 方向）：空字符串表示未排序。用于重启后保持排序。
        [ObservableProperty] public partial string LastSortPath { get; set; } = "";
        [ObservableProperty] public partial bool LastSortAscending { get; set; } = true;
        [ObservableProperty] public partial string Language { get; set; } = "zh-Hans";
        [ObservableProperty] public partial string SystemBackdropMode { get; set; } = "Mica";
        // 表格内容切换动画：Default = 控件默认过渡；Fade = 整表淡入（掩盖逐行覆盖）；None = 无动画
        [ObservableProperty] public partial string TransitionMode { get; set; } = "Fade";
        [ObservableProperty] public partial System.Collections.ObjectModel.ObservableCollection<string> TimeGroupedFolders { get; set; } = new();
        // Native AOT 版没有内置 COM，改不了系统「快速访问」的固定项，这里自己存一份
        // （普通构建不使用它，仍以系统快速访问为准）。
        [ObservableProperty] public partial System.Collections.ObjectModel.ObservableCollection<string> AotPinnedFolders { get; set; } = new();
        private static readonly string DefaultDownloadsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        public Configs()
        {
            EnsureUserConfigExists();
            BuildConfiguration();
            ReadConfigs();
            Debug.WriteLine($"Configs initialized. MiddleFilesHeight: {MiddleFilesHeight}, UserConfig: {UserConfigPath}");
        }

        private void EnsureUserConfigExists()
        {
            if (!File.Exists(UserConfigPath))
            {
                Directory.CreateDirectory(UserConfigDir);
                File.WriteAllText(UserConfigPath, "{}");
            }
        }

        private void BuildConfiguration()
        {
            configuration = new ConfigurationBuilder()
                .AddJsonFile(DefaultConfigPath, optional: false, reloadOnChange: false)
                .AddJsonFile(UserConfigPath, optional: true, reloadOnChange: false)
                .Build();
        }

        public void ReadConfigs()
        {
            MiddleFilesHeight = configuration.GetValue("Appearance:MiddleFilesHeight", 40);
            IfUsesWin32APIToGetIcon = configuration.GetValue("Advanced:ifUsesWin32APIToGetIcon", true);
            HomePageFullPath = configuration.GetValue("General:HomePageFullPath", DefaultDownloadsPath)!;
            LastVisitedPath = configuration.GetValue("General:LastVisitedPath", "")!;
            IconParallelLoadingCount = configuration.GetValue("Performance:IconParallelLoadingCount", 0);
            DefaultOrderMode = configuration.GetValue("General:DefaultOrderMode", "ModifiedDesc")!;
            LastSortPath = configuration.GetValue("General:LastSortPath", "")!;
            LastSortAscending = configuration.GetValue("General:LastSortAscending", true);
            Language = configuration.GetValue("General:Language", "zh-Hans")!;
            SystemBackdropMode = configuration.GetValue("Appearance:SystemBackdropMode", "Mica")!;
            TransitionMode = configuration.GetValue("Appearance:TransitionMode", "Fade")!;
            IfLimitIconLoadingConcurrency = IconParallelLoadingCount > 0;
            var folderArray = configuration.GetSection("Special:TimeGroupedFolders").Get<string[]>();
            TimeGroupedFolders = new System.Collections.ObjectModel.ObservableCollection<string>(
                folderArray != null && folderArray.Length > 0 ? folderArray : new[] { DefaultDownloadsPath });
            var aotPinned = configuration.GetSection("General:AotPinnedFolders").Get<string[]>();
            AotPinnedFolders = new System.Collections.ObjectModel.ObservableCollection<string>(
                aotPinned ?? Array.Empty<string>());
        }

        public void SaveConfig()
        {
            var escapedPath = HomePageFullPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var escapedLastVisited = LastVisitedPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var escapedLastSort = (LastSortPath ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            var json = string.Concat(
                "{\n",
                "  \"Appearance\": {\n",
                $"    \"MiddleFilesHeight\": {MiddleFilesHeight},\n",
                $"    \"SystemBackdropMode\": \"{SystemBackdropMode}\",\n",
                $"    \"TransitionMode\": \"{TransitionMode}\"\n",
                "  },\n",
                "  \"Advanced\": {\n",
               $"    \"ifUsesWin32APIToGetIcon\": {IfUsesWin32APIToGetIcon.ToString().ToLower()}\n",
                "  },\n",
                "  \"General\": {\n",
               $"    \"HomePageFullPath\": \"{escapedPath}\",\n",
               $"    \"LastVisitedPath\": \"{escapedLastVisited}\",\n",
               $"    \"DefaultOrderMode\": \"{DefaultOrderMode}\",\n",
               $"    \"LastSortPath\": \"{escapedLastSort}\",\n",
               $"    \"LastSortAscending\": {LastSortAscending.ToString().ToLower()},\n",
               $"    \"Language\": \"{Language}\",\n",
               $"    \"AotPinnedFolders\": [{BuildStringArrayJson(AotPinnedFolders)}]\n",
                "  },\n",
                "  \"Performance\": {\n",
               $"    \"IconParallelLoadingCount\": {IconParallelLoadingCount}\n",
                "  },\n",
                "  \"Special\": {\n",
               $"    \"TimeGroupedFolders\": [{BuildFoldersJsonArray()}]\n",
                "  }\n",
                "}\n");
            File.WriteAllText(UserConfigPath, json);
            if (configuration != null)
            {
                ((IConfigurationRoot)configuration).Reload();
                ReadConfigs();
            }
        }

        public bool IsTimeGroupedFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            foreach (var f in TimeGroupedFolders)
            {
                if (string.Equals(path.TrimEnd('\\'), f.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private string BuildFoldersJsonArray()
        {
            var escaped = new List<string>();
            foreach (var f in TimeGroupedFolders)
            {
                escaped.Add($"\"{f.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
            }
            return string.Join(",\n      ", escaped);
        }

        private static string BuildStringArrayJson(System.Collections.Generic.IEnumerable<string> values)
        {
            var escaped = new List<string>();
            foreach (var v in values)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                escaped.Add($"\"{v.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
            }
            return string.Join(", ", escaped);
        }
    }
}
