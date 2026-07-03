using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using static LRS.UserControls.TreeDataGrid;

namespace LRS.ViewModels
{
    public class SortModeItem
    {
        public SortMode Mode { get; set; }
        public string Display { get; set; } = "";
    }

    public partial class SettingsViewModel : ObservableObject
    {
        private readonly MultiLanguageStringsViewModel _ml;
        public MultiLanguageStringsViewModel ML => _ml;

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

        [ObservableProperty]
        private int _selectedLanguageIndex;

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

            var configLang = App.SharedViewModel?.AppConfigs?.Language ?? "zh-Hans";
            _selectedLanguageIndex = configLang == "en" ? 1 : 0;
        }

        partial void OnSelectedOrderModeItemChanged(SortModeItem value)
        {
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.DefaultOrderMode = value.Mode.ToString();
        }

        partial void OnSelectedLanguageIndexChanged(int value)
        {
            var lang = value == 1 ? "en" : "zh-Hans";
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.Language = lang;
            App.LocalizationService.SetLanguage(lang);
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
