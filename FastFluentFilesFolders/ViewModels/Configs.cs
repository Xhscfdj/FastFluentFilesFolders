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
        private static readonly string DefaultConfigPath =
            Path.Combine(AppContext.BaseDirectory, "Configs", "configs.json");

        private static readonly string UserConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastFluentFilesFolders");

        public static readonly string UserConfigPath =
            Path.Combine(UserConfigDir, "user_configs.json");

        //public string UserConfigPathToDisplay = "C:C:C:C:C";
        public IConfiguration configuration;
        [ObservableProperty] private int _middleFilesHeight = 40;
        [ObservableProperty] private bool _ifUsesWin32APIToGetIcon = true;
        [ObservableProperty] private bool _ifLimitIconLoadingConcurrency = false;
        [ObservableProperty] private int _iconParallelLoadingCount = 30;
        [ObservableProperty] private string _homePageFullPath = DefaultDownloadsPath;
        [ObservableProperty] private string _defaultOrderMode = "ModifiedDesc";
        [ObservableProperty] private string _language = "zh-Hans";
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
            IconParallelLoadingCount = configuration.GetValue("Performance:IconParallelLoadingCount", 30);
            DefaultOrderMode = configuration.GetValue("General:DefaultOrderMode", "ModifiedDesc")!;
            Language = configuration.GetValue("General:Language", "zh-Hans")!;
            if (IconParallelLoadingCount != 0) IfLimitIconLoadingConcurrency = true;
            var folderArray = configuration.GetSection("Special:TimeGroupedFolders").Get<string[]>();
            TimeGroupedFolders = new System.Collections.ObjectModel.ObservableCollection<string>(
                folderArray != null && folderArray.Length > 0 ? folderArray : new[] { DefaultDownloadsPath });
        }

        public void SaveConfig()
        {
            var escapedPath = HomePageFullPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var json = string.Concat(
                "{\n",
                "  \"Appearance\": {\n",
               $"    \"MiddleFilesHeight\": {MiddleFilesHeight}\n",
                "  },\n",
                "  \"Advanced\": {\n",
               $"    \"ifUsesWin32APIToGetIcon\": {IfUsesWin32APIToGetIcon.ToString().ToLower()}\n",
                "  },\n",
                "  \"General\": {\n",
               $"    \"HomePageFullPath\": \"{escapedPath}\",\n",
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
