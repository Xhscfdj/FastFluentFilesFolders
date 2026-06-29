using CommunityToolkit.Mvvm.ComponentModel;
using LRS.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using static LRS.UserControls.TreeDataGrid;

namespace LRS.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly LocalizationService _loc;

        private Dictionary<SortMode, string> _orderModeMap = new();
        private List<KeyValuePair<SortMode, string>> _orderModePairs = new();

        public List<KeyValuePair<SortMode, string>> OrderModePairs
        {
            get => _orderModePairs;
            private set
            {
                _orderModePairs = value;
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        private KeyValuePair<SortMode, string> _selectedOrderModePair;

        [ObservableProperty]
        private int _selectedLanguageIndex;

        public string SettingsTitle => _loc.GetString("SettingsTitle");
        public string GeneralSection => _loc.GetString("GeneralSection");
        public string HomePagePath => _loc.GetString("HomePagePath");
        public string DefaultSortOrder => _loc.GetString("DefaultSortOrder");
        public string SortBy => _loc.GetString("SortBy");
        public string AppearanceSection => _loc.GetString("AppearanceSection");
        public string MiddleFilesHeight => _loc.GetString("MiddleFilesHeight");
        public string AdvancedSection => _loc.GetString("AdvancedSection");
        public string UseWin32Icon => _loc.GetString("UseWin32Icon");
        public string PerformanceSection => _loc.GetString("PerformanceSection");
        public string IconParallelLoading => _loc.GetString("IconParallelLoading");
        public string DebugSection => _loc.GetString("DebugSection");
        public string UserConfigPath => _loc.GetString("UserConfigPath");
        public string SaveSettings => _loc.GetString("SaveSettings");
        public string LanguageLabel => _loc.GetString("Language");
        public string ConfigFilePath => ViewModels.Configs.UserConfigPath;

        public SettingsViewModel(LocalizationService locService)
        {
            _loc = locService;
            BuildOrderModeMap();
            BuildOrderModePairs();

            var modeStr = App.SharedViewModel?.AppConfigs?.DefaultOrderMode ?? "ModifiedDesc";
            var match = _orderModePairs.FirstOrDefault(kv => kv.Key.ToString() == modeStr);
            _selectedOrderModePair = match.Value != null
                ? match
                : _orderModePairs.First(kv => kv.Key == SortMode.ModifiedDesc);

            var configLang = App.SharedViewModel?.AppConfigs?.Language ?? "zh-Hans";
            _selectedLanguageIndex = configLang == "en" ? 1 : 0;

            _loc.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LocalizationService.CurrentLanguage))
                    RefreshAllStrings();
            };
        }

        partial void OnSelectedOrderModePairChanged(KeyValuePair<SortMode, string> value)
        {
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.DefaultOrderMode = value.Key.ToString();
        }

        partial void OnSelectedLanguageIndexChanged(int value)
        {
            var lang = value == 1 ? "en" : "zh-Hans";
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.Language = lang;
            _loc.SetLanguage(lang);
        }

        private void BuildOrderModeMap()
        {
            _orderModeMap = new Dictionary<SortMode, string>
            {
                { SortMode.NameAsc, _loc.GetString("SortNameAsc") },
                { SortMode.NameDesc, _loc.GetString("SortNameDesc") },
                { SortMode.SizeDesc, _loc.GetString("SortSizeDesc") },
                { SortMode.SizeAsc, _loc.GetString("SortSizeAsc") },
                { SortMode.ModifiedDesc, _loc.GetString("SortModifiedDesc") },
                { SortMode.ModifiedAsc, _loc.GetString("SortModifiedAsc") },
                { SortMode.CreatedDesc, _loc.GetString("SortCreatedDesc") },
                { SortMode.CreatedAsc, _loc.GetString("SortCreatedAsc") },
            };
        }

        private void BuildOrderModePairs()
        {
            OrderModePairs = _orderModeMap.ToList();
        }

        private void RefreshAllStrings()
        {
            BuildOrderModeMap();
            BuildOrderModePairs();
            OnPropertyChanged(nameof(SettingsTitle));
            OnPropertyChanged(nameof(GeneralSection));
            OnPropertyChanged(nameof(HomePagePath));
            OnPropertyChanged(nameof(DefaultSortOrder));
            OnPropertyChanged(nameof(SortBy));
            OnPropertyChanged(nameof(AppearanceSection));
            OnPropertyChanged(nameof(MiddleFilesHeight));
            OnPropertyChanged(nameof(AdvancedSection));
            OnPropertyChanged(nameof(UseWin32Icon));
            OnPropertyChanged(nameof(PerformanceSection));
            OnPropertyChanged(nameof(IconParallelLoading));
            OnPropertyChanged(nameof(DebugSection));
            OnPropertyChanged(nameof(UserConfigPath));
            OnPropertyChanged(nameof(SaveSettings));
            OnPropertyChanged(nameof(LanguageLabel));

            var modeStr = App.SharedViewModel?.AppConfigs?.DefaultOrderMode ?? "ModifiedDesc";
            var match = _orderModePairs.FirstOrDefault(kv => kv.Key.ToString() == modeStr);
            SelectedOrderModePair = match.Value != null
                ? match
                : _orderModePairs.First(kv => kv.Key == SortMode.ModifiedDesc);
        }
    }
}
