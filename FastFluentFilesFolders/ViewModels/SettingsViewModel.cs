using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastFluentFilesFolders.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

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

    public class BackdropOption
    {
        public string DisplayName { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class TransitionModeItem
    {
        public string Value { get; set; } = "";
        public string Display { get; set; } = "";
    }

    public partial class SettingsViewModel : ObservableObject
    {
        private readonly MultiLanguageStringsViewModel _ml;
        public MultiLanguageStringsViewModel ML => _ml;

        public Configs? AppConfigs => App.SharedViewModel?.AppConfigs;

        private List<SortModeItem> _orderModeItems = new();

        private bool _isSaving;

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

        public List<TransitionModeItem> TransitionModeItems { get; private set; } = new();

        [ObservableProperty]
        private TransitionModeItem? _selectedTransitionModeItem;

        // 静态字段：NavigationCacheMode=Disabled 后 VM 每次进入都会重建，
        // 用 static 保留解锁状态（原来依赖 Required 缓存保活）。
        private static bool _fumoUnlocked;

        public ObservableCollection<LanguageOption> LanguageOptions { get; } = new();

        [ObservableProperty]
        private LanguageOption? _selectedLanguage;

        public ObservableCollection<BackdropOption> BackdropOptions { get; } = new();

        [ObservableProperty]
        private BackdropOption? _selectedBackdrop;

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
            App.LocalizationService.SetLanguage(configLang);

            PropertyChanged += OnSettingsPropertyChanged;

            BackdropOptions.Add(new BackdropOption { DisplayName = _ml.BackdropMica, Value = "Mica" });
            BackdropOptions.Add(new BackdropOption { DisplayName = _ml.BackdropMicaAlt, Value = "MicaAlt" });
            BackdropOptions.Add(new BackdropOption { DisplayName = _ml.BackdropAcrylic, Value = "Acrylic" });
            BackdropOptions.Add(new BackdropOption { DisplayName = _ml.BackdropAcrylicThin, Value = "AcrylicThin" });
            BackdropOptions.Add(new BackdropOption { DisplayName = _ml.BackdropNone, Value = "None" });

            var configBackdrop = App.SharedViewModel?.AppConfigs?.SystemBackdropMode ?? "Mica";
            _selectedBackdrop = BackdropOptions.FirstOrDefault(b => b.Value == configBackdrop) ?? BackdropOptions[0];

            BuildTransitionModeItems();
            var transMode = App.SharedViewModel?.AppConfigs?.TransitionMode ?? "Default";
            _selectedTransitionModeItem = TransitionModeItems.FirstOrDefault(i => i.Value == transMode) ?? TransitionModeItems[0];

            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.PropertyChanged += OnAppConfigPropertyChanged;
        }

        private void OnAppConfigPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            AutoSaveConfig();
        }

        /// <summary>
        /// 断开对单例 AppConfigs 的事件订阅，防止页面销毁后 VM 仍被静态引用。
        /// 由 SettingsView.OnNavigatedFrom 调用。
        /// </summary>
        public void Unsubscribe()
        {
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.PropertyChanged -= OnAppConfigPropertyChanged;
            PropertyChanged -= OnSettingsPropertyChanged;
        }

        private void AutoSaveConfig()
        {
            if (_isSaving) return;
            _isSaving = true;
            try
            {
                App.SharedViewModel?.AppConfigs?.SaveConfig();
            }
            finally
            {
                _isSaving = false;
            }
        }

        private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedLanguage))
                ApplySelectedLanguage();
        }

        private void ApplySelectedLanguage()
        {
            var value = SelectedLanguage;
            if (value == null) return;
            var lang = value.Value;
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.Language = lang;
            App.LocalizationService.SetLanguage(lang);
            App.SharedViewModel?.AppConfigs?.SaveConfig();
        }

        partial void OnSelectedOrderModeItemChanged(SortModeItem value)
        {
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.DefaultOrderMode = value.Mode.ToString();
        }

        partial void OnSelectedTransitionModeItemChanged(TransitionModeItem? value)
        {
            if (value == null) return;
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.TransitionMode = value.Value;
        }

        partial void OnSelectedBackdropChanged(BackdropOption? value)
        {
            if (value == null) return;
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.SystemBackdropMode = value.Value;
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
                AutoSaveConfig();
            }
            NewTimeGroupedFolderPath = "";
        }

        [RelayCommand]
        private void DeleteTimeGroupedFolder(string path)
        {
            var folders = App.SharedViewModel?.AppConfigs?.TimeGroupedFolders;
            folders?.Remove(path);
            OnPropertyChanged(nameof(TimeGroupedFolders));
            AutoSaveConfig();
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

        private void BuildTransitionModeItems()
        {
            TransitionModeItems = new List<TransitionModeItem>
            {
                new() { Value = "Default", Display = _ml.TransitionModeDefault },
                new() { Value = "Stream", Display = _ml.TransitionModeStream },
                new() { Value = "Fade", Display = _ml.TransitionModeFade },
                new() { Value = "None", Display = _ml.TransitionModeNone },
            };
        }
    }
}
