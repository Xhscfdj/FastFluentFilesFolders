using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.IO;

namespace LRS.ViewModels
{
    public partial class Configs : ObservableObject
    {
        private static readonly string DefaultConfigPath =
            Path.Combine(AppContext.BaseDirectory, "Configs", "configs.json");

        private static readonly string UserConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LRS");

        public static readonly string UserConfigPath =
            Path.Combine(UserConfigDir, "user_configs.json");

        //public string UserConfigPathToDisplay = "C:C:C:C:C";
        public IConfiguration configuration;
        [ObservableProperty] private int _middleFilesHeight = 40;
        [ObservableProperty] private bool _ifUsesWin32APIToGetIcon = true;
        [ObservableProperty] private bool _ifLimitIconLoadingConcurrency = false;
        [ObservableProperty] private int _iconParallelLoadingCount = 30;
        [ObservableProperty] private string _homePageFullPath = "C:\\";
        [ObservableProperty] private string _defaultOrderMode = "ModifiedDesc";
        [ObservableProperty] private string _language = "zh-Hans";

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
            HomePageFullPath = configuration.GetValue("General:HomePageFullPath", "C:\\")!;
            IconParallelLoadingCount = configuration.GetValue("Performance:IconParallelLoadingCount", 30);
            DefaultOrderMode = configuration.GetValue("General:DefaultOrderMode", "ModifiedDesc")!;
            Language = configuration.GetValue("General:Language", "zh-Hans")!;
            if (IconParallelLoadingCount != 0) IfLimitIconLoadingConcurrency = true;
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
                "  }\n",
                "}\n");
            File.WriteAllText(UserConfigPath, json);
            if (configuration != null)
            {
                ((IConfigurationRoot)configuration).Reload();
                ReadConfigs();
            }
        }
    }
}
