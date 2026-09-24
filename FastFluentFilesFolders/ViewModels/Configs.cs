using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace FastFluentFilesFolders.ViewModels
{
    public partial class Configs : ObservableObject
    {
        public static readonly bool IsDebugMode = true;
        public static readonly string Version = "1.1.0";
        private static readonly string DefaultConfigPath =
            Path.Combine(AppContext.BaseDirectory, "Configs", "configs.json");

        private static readonly string UserConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastFluentFilesFolders");

        public static readonly string UserConfigPath =
            Path.Combine(UserConfigDir, "user_configs.json");

        public IConfiguration configuration;
        [ObservableProperty] private int _middleFilesHeight = 40;
        [ObservableProperty] private bool _ifUsesWin32APIToGetIcon = true;
        [ObservableProperty] private bool _ifLimitIconLoadingConcurrency = false;
        // 0 = 自动（默认，使用安全并发上限 16，避免 Shell/GDI+ 并发过高偶发失败）；
        // >0 表示最多同时解码多少个图标
        [ObservableProperty] private int _iconParallelLoadingCount = 0;
        [ObservableProperty] private string _homePageFullPath = DefaultDownloadsPath;
        [ObservableProperty] private string _lastVisitedPath = "";
        [ObservableProperty] private string _defaultOrderMode = "ModifiedDesc";
        [ObservableProperty] private string _language = "zh-Hans";
        [ObservableProperty] private string _systemBackdropMode = "Mica";
        // 表格内容切换动画：Default = 控件默认过渡；Fade = 整表淡入（掩盖逐行覆盖）；None = 无动画
        [ObservableProperty] private string _transitionMode = "Fade";
        [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<string> _timeGroupedFolders = new();
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
            Language = configuration.GetValue("General:Language", "zh-Hans")!;
            SystemBackdropMode = configuration.GetValue("Appearance:SystemBackdropMode", "Mica")!;
            TransitionMode = configuration.GetValue("Appearance:TransitionMode", "Fade")!;
            IfLimitIconLoadingConcurrency = IconParallelLoadingCount > 0;
            var folderArray = configuration.GetSection("Special:TimeGroupedFolders").Get<string[]>();
            TimeGroupedFolders = new System.Collections.ObjectModel.ObservableCollection<string>(
                folderArray != null && folderArray.Length > 0 ? folderArray : new[] { DefaultDownloadsPath });
        }

        public void SaveConfig()
        {
            var escapedPath = HomePageFullPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var escapedLastVisited = LastVisitedPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
               $"    \"Language\": \"{Language}\"\n",
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
    }
}
