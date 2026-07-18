using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using static FastFluentFilesFolders.UserControls.TreeDataGrid;

namespace FastFluentFilesFolders.ViewModels
{
    public class SortModeItem
    {
        public SortMode Mode { get; set; }
        public string Display { get; set; } = "";
    }

    public class LanguageOption
    {
        public string DisplayName { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public partial class SettingsViewModel : ObservableObject
    {
        private readonly MultiLanguageStringsViewModel _ml;
        public MultiLanguageStringsViewModel ML => _ml;

        public Configs? AppConfigs => App.SharedViewModel?.AppConfigs;

        private List<SortModeItem> _orderModeItems = new();

        public List<SortModeItem> OrderModeItems
        {
            get => _orderModeItems;
            private set
            {
                _orderModeItems = value;
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        private SortModeItem _selectedOrderModeItem;

        private bool _fumoUnlocked;

        public ObservableCollection<LanguageOption> LanguageOptions { get; } = new();

        [ObservableProperty]
        private LanguageOption? _selectedLanguage;

        [ObservableProperty]
        private string _newTimeGroupedFolderPath = "";

        public ObservableCollection<string> TimeGroupedFolders =>
            App.SharedViewModel?.AppConfigs?.TimeGroupedFolders ?? new ObservableCollection<string>();

        public SettingsViewModel(MultiLanguageStringsViewModel ml)
        {
            _ml = ml;
            BuildOrderModeItems();

            var modeStr = App.SharedViewModel?.AppConfigs?.DefaultOrderMode ?? "ModifiedDesc";
            var match = _orderModeItems.FirstOrDefault(i => i.Mode.ToString() == modeStr);
            _selectedOrderModeItem = match ?? _orderModeItems.First(i => i.Mode == SortMode.ModifiedDesc);

            LanguageOptions.Add(new LanguageOption { DisplayName = "中文", Value = "zh-Hans" });
            LanguageOptions.Add(new LanguageOption { DisplayName = "English", Value = "en" });

            var configLang = App.SharedViewModel?.AppConfigs?.Language ?? "zh-Hans";
            _selectedLanguage = configLang == "en" ? LanguageOptions[1] : LanguageOptions[0];
        }

        partial void OnSelectedOrderModeItemChanged(SortModeItem value)
        {
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.DefaultOrderMode = value.Mode.ToString();
        }

        partial void OnSelectedLanguageChanged(LanguageOption? value)
        {
            if (value == null) return;
            var lang = value.Value;
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.Language = lang;
            App.LocalizationService.SetLanguage(lang);
        }

        public void UnlockFumoLanguage()
        {
            if (_fumoUnlocked) return;
            _fumoUnlocked = true;

            LanguageOptions.Add(new LanguageOption { DisplayName = "Fumo语", Value = "fumo" });

            if (SelectedLanguage?.Value != "fumo")
                SelectedLanguage = LanguageOptions[^1];
        }

        [RelayCommand]
        private void AddTimeGroupedFolder()
        {
            var path = NewTimeGroupedFolderPath?.Trim();
            if (string.IsNullOrWhiteSpace(path)) return;
            var folders = App.SharedViewModel?.AppConfigs?.TimeGroupedFolders;
            if (folders != null && !folders.Contains(path))
            {
                folders.Add(path);
                OnPropertyChanged(nameof(TimeGroupedFolders));
            }
            NewTimeGroupedFolderPath = "";
        }

        [RelayCommand]
        private void DeleteTimeGroupedFolder(string path)
        {
            var folders = App.SharedViewModel?.AppConfigs?.TimeGroupedFolders;
            folders?.Remove(path);
            OnPropertyChanged(nameof(TimeGroupedFolders));
        }

        private void BuildOrderModeItems()
        {
            OrderModeItems = new List<SortModeItem>
            {
                new() { Mode = SortMode.NameAsc, Display = _ml.SortNameAsc },
                new() { Mode = SortMode.NameDesc, Display = _ml.SortNameDesc },
                new() { Mode = SortMode.SizeDesc, Display = _ml.SortSizeDesc },
                new() { Mode = SortMode.SizeAsc, Display = _ml.SortSizeAsc },
                new() { Mode = SortMode.ModifiedDesc, Display = _ml.SortModifiedDesc },
                new() { Mode = SortMode.ModifiedAsc, Display = _ml.SortModifiedAsc },
                new() { Mode = SortMode.CreatedDesc, Display = _ml.SortCreatedDesc },
                new() { Mode = SortMode.CreatedAsc, Display = _ml.SortCreatedAsc },
            };
        }
    }
}
