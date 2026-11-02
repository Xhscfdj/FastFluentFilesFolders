using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastFluentFilesFolders.Models;
using System;
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
		[ObservableProperty] public partial string UserConfigPath { get; set; } = Configs.UserConfigPath ?? "";
		private readonly MultiLanguageStringsViewModel _ml;
        public MultiLanguageStringsViewModel ML => _ml;

        public Configs? AppConfigs => App.SharedViewModel?.AppConfigs;

        private List<SortModeItem> _orderModeItems = new();

        private bool _isSaving;

        /// <summary>
        /// 重建下拉框选项期间置位：此时 <c>SelectedXxx</c> 会因为“旧选中项已不在新列表里”被绑定置空/换对象，
        /// 不做保护就会把 null 或重复值写回 <see cref="Configs"/> 并触发多余落盘。
        /// </summary>
        private bool _syncingOptions;

        /// <summary>是否已挂上对单例 AppConfigs 的订阅（与页面生命周期配对）。</summary>
        private bool _subscribed;

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
        public partial SortModeItem SelectedOrderModeItem { get; set; }

        private List<TransitionModeItem> _transitionModeItems = new();

        /// <summary>
        /// 切换动画选项。必须是带通知的属性：语言切换时要整表重建（项文本是构造时按语言固化的），
        /// XAML 那边是 <c>{x:Bind ... , Mode=OneWay}</c>，没有通知就永远显示旧语言。
        /// </summary>
        public List<TransitionModeItem> TransitionModeItems
        {
            get => _transitionModeItems;
            private set
            {
                _transitionModeItems = value;
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        public partial TransitionModeItem? SelectedTransitionModeItem { get; set; }

        // 静态字段：NavigationCacheMode=Disabled 后 VM 每次进入都会重建，
        // 用 static 保留解锁状态（原来依赖 Required 缓存保活）。
        private static bool _fumoUnlocked;

        public ObservableCollection<LanguageOption> LanguageOptions { get; } = new();

        [ObservableProperty]
        public partial LanguageOption? SelectedLanguage { get; set; }

        public ObservableCollection<BackdropOption> BackdropOptions { get; } = new();

        [ObservableProperty]
        public partial BackdropOption? SelectedBackdrop { get; set; }

        [ObservableProperty]
        public partial string NewTimeGroupedFolderPath { get; set; } = "";

        /// <summary>
        /// 与 <see cref="Configs.TimeGroupedFolders"/> 同步的页面私有副本。
        /// 不能把 x:Bind 直接绑到单例 Configs 上的那个集合：OneWay 绑定会在这个集合上挂
        /// PropertyChanged/CollectionChanged 监听器，而监听器登记在**永远不会死**的单例上，
        /// 一旦绑定跟踪对象没被释放（实测 WinUI 3 对 Frame 托管的页面每次都不释放），
        /// 单例就会把整套绑定跟踪对象连同 SettingsViewModel 永久钉住。改成页面私有的集合后，
        /// 这个环只在本页内部，页面一销毁就整块回收。
        /// </summary>
        public ObservableCollection<string> TimeGroupedFolders { get; } = new();

        // ---- 下面四个属性是 Configs 的转发：页面不再直接 x:Bind 到单例 Configs ----
        // 原因同上：TwoWay x:Bind 会在单例 Configs 上挂 PropertyChanged 监听器。

        public string HomePageFullPath
        {
            get => AppConfigs?.HomePageFullPath ?? "";
            set { if (AppConfigs is { } c) c.HomePageFullPath = value; }
        }

        public int MiddleFilesHeight
        {
            get => AppConfigs?.MiddleFilesHeight ?? 40;
            set { if (AppConfigs is { } c) c.MiddleFilesHeight = value; }
        }

        public bool UseWin32ApiIcon
        {
            get => AppConfigs?.IfUsesWin32APIToGetIcon ?? true;
            set { if (AppConfigs is { } c) c.IfUsesWin32APIToGetIcon = value; }
        }

        public int IconParallelLoadingCount
        {
            get => AppConfigs?.IconParallelLoadingCount ?? 0;
            set { if (AppConfigs is { } c) c.IconParallelLoadingCount = value; }
        }

        // ---- 多语言文案：页面不再直接 x:Bind 到单例 ML ----
        // x:Bind 的 OneWay 会在**源对象**的 PropertyChanged 上挂强引用监听器。原来文案直接绑
        // 单例 ML，实测每进一次设置页就永久留下 1 个 SettingsViewModel + 1 份绑定跟踪对象
        // （后者还持有整棵元素树，内存涨得很明显）。现在文案经页面私有的 SettingsViewModel
        // 转发，引用环只在本页内部，页面销毁就能整块回收。
        public string SettingsTitle => _ml.SettingsTitle;
        public string GeneralSection => _ml.GeneralSection;
        public string HomePagePath => _ml.HomePagePath;
        public string DefaultSortOrder => _ml.DefaultSortOrder;
        public string SortBy => _ml.SortBy;
        public string AppearanceSection => _ml.AppearanceSection;
        /// <summary>「中间文件列表高度」的标题文案（同名的 <see cref="MiddleFilesHeight"/> 是数值本身）。</summary>
        public string MiddleFilesHeightLabel => _ml.MiddleFilesHeight;
        public string AdvancedSection => _ml.AdvancedSection;
        public string UseWin32Icon => _ml.UseWin32Icon;
        public string PerformanceSection => _ml.PerformanceSection;
        public string IconParallelLoading => _ml.IconParallelLoading;
        public string DebugSection => _ml.DebugSection;
        /// <summary>「用户配置文件路径」的标题文案（同名的 <see cref="UserConfigPath"/> 是路径本身）。</summary>
        public string UserConfigPathLabel => _ml.UserConfigPath;
        public string LanguageLabel => _ml.LanguageLabel;
        public string AboutSection => _ml.AboutSection;
        public string AuthorTip => _ml.AuthorTip;
        public string RepositoryTip => _ml.RepositoryTip;
        public string AppName => _ml.AppName;
        public string AppAuthorCredit => _ml.AppAuthorCredit;
        public string TimeGroupedFoldersHeader => _ml.TimeGroupedFoldersHeader;
        public string TimeGroupedFolderPlaceholder => _ml.TimeGroupedFolderPlaceholder;
        /// <summary>「添加按时间分组文件夹」按钮文案（同名的 <see cref="AddTimeGroupedFolderCommand"/> 是命令）。</summary>
        public string AddTimeGroupedFolderLabel => _ml.AddTimeGroupedFolder;
        public string PluginsSection => _ml.PluginsSection;
        public string PluginManagement => _ml.PluginManagement;
        public string PluginImport => _ml.PluginImport;
        public string PluginImportBtn => _ml.PluginImportBtn;
        public string PluginInstalled => _ml.PluginInstalled;
        public string SystemBackdropMode => _ml.SystemBackdropMode;
        public string TransitionMode => _ml.TransitionMode;
        public string CheckUpdate => _ml.CheckUpdate;
        public string CheckUpdateDescription => _ml.CheckUpdateDescription;
        public string CheckUpdateButton => _ml.CheckUpdateButton;

        /// <summary>转发文案对应的 ML 属性名（语言切换时按名通知，不整表重刷）。</summary>
        private static readonly HashSet<string> ForwardedStringNames = new(StringComparer.Ordinal)
        {
            nameof(MultiLanguageStringsViewModel.SettingsTitle),
            nameof(MultiLanguageStringsViewModel.GeneralSection),
            nameof(MultiLanguageStringsViewModel.HomePagePath),
            nameof(MultiLanguageStringsViewModel.DefaultSortOrder),
            nameof(MultiLanguageStringsViewModel.SortBy),
            nameof(MultiLanguageStringsViewModel.AppearanceSection),
            nameof(MultiLanguageStringsViewModel.MiddleFilesHeight),
            nameof(MultiLanguageStringsViewModel.AdvancedSection),
            nameof(MultiLanguageStringsViewModel.UseWin32Icon),
            nameof(MultiLanguageStringsViewModel.PerformanceSection),
            nameof(MultiLanguageStringsViewModel.IconParallelLoading),
            nameof(MultiLanguageStringsViewModel.DebugSection),
            nameof(MultiLanguageStringsViewModel.UserConfigPath),
            nameof(MultiLanguageStringsViewModel.LanguageLabel),
            nameof(MultiLanguageStringsViewModel.AboutSection),
            nameof(MultiLanguageStringsViewModel.AuthorTip),
            nameof(MultiLanguageStringsViewModel.RepositoryTip),
            nameof(MultiLanguageStringsViewModel.AppName),
            nameof(MultiLanguageStringsViewModel.AppAuthorCredit),
            nameof(MultiLanguageStringsViewModel.TimeGroupedFoldersHeader),
            nameof(MultiLanguageStringsViewModel.TimeGroupedFolderPlaceholder),
            nameof(MultiLanguageStringsViewModel.AddTimeGroupedFolder),
            nameof(MultiLanguageStringsViewModel.PluginsSection),
            nameof(MultiLanguageStringsViewModel.PluginManagement),
            nameof(MultiLanguageStringsViewModel.PluginImport),
            nameof(MultiLanguageStringsViewModel.PluginImportBtn),
            nameof(MultiLanguageStringsViewModel.PluginInstalled),
            nameof(MultiLanguageStringsViewModel.SystemBackdropMode),
            nameof(MultiLanguageStringsViewModel.TransitionMode),
            nameof(MultiLanguageStringsViewModel.CheckUpdate),
            nameof(MultiLanguageStringsViewModel.CheckUpdateDescription),
            nameof(MultiLanguageStringsViewModel.CheckUpdateButton),
        };

        /// <summary>
        /// 语言切换后重建三个下拉框的选项。
        ///
        /// 这三个下拉的项文本（排序方式 / 背景材质 / 切换动画）是在构造时按**当时的**语言取值的普通字符串，
        /// 项对象也不实现通知；只转发 <c>OnPropertyChanged(name)</c> 不会让它们更新，所以必须整表重建。
        /// （语言下拉本身是写死的「中文 / English」，不需要变。）
        /// </summary>
        private void OnMlLanguageChanged(object? sender, EventArgs e) => RebuildLocalizedOptions();

        private void RebuildLocalizedOptions()
        {
            _syncingOptions = true;
            try
            {
                // 先记住当前选择（按语义值，而不是对象引用 —— 重建后是全 New 的项对象）
                var orderMode = SelectedOrderModeItem?.Mode ?? SortMode.ModifiedDesc;
                var backdrop = SelectedBackdrop?.Value ?? "Mica";
                var transition = SelectedTransitionModeItem?.Value ?? "Default";

                BuildOrderModeItems();
                SelectedOrderModeItem = _orderModeItems.FirstOrDefault(i => i.Mode == orderMode)
                                        ?? _orderModeItems.First(i => i.Mode == SortMode.ModifiedDesc);

                BackdropOptions.Clear();
                AddBackdropOptions();
                SelectedBackdrop = BackdropOptions.FirstOrDefault(b => b.Value == backdrop) ?? BackdropOptions[0];

                BuildTransitionModeItems();
                SelectedTransitionModeItem = TransitionModeItems.FirstOrDefault(i => i.Value == transition)
                                             ?? TransitionModeItems[0];
            }
            finally
            {
                _syncingOptions = false;
            }
        }

        private void OnMlPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MultiLanguageStringsViewModel.MiddleFilesHeight):
                    OnPropertyChanged(nameof(MiddleFilesHeightLabel));
                    return;
                case nameof(MultiLanguageStringsViewModel.UserConfigPath):
                    OnPropertyChanged(nameof(UserConfigPathLabel));
                    return;
                case nameof(MultiLanguageStringsViewModel.AddTimeGroupedFolder):
                    OnPropertyChanged(nameof(AddTimeGroupedFolderLabel));
                    return;
                case null:
                    return;
                default:
                    if (ForwardedStringNames.Contains(e.PropertyName))
                        OnPropertyChanged(e.PropertyName);
                    return;
            }
        }

        public SettingsViewModel(MultiLanguageStringsViewModel ml)
        {
            _ml = ml;
            BuildOrderModeItems();

            var modeStr = App.SharedViewModel?.AppConfigs?.DefaultOrderMode ?? "ModifiedDesc";
            var match = _orderModeItems.FirstOrDefault(i => i.Mode.ToString() == modeStr);
            SelectedOrderModeItem = match ?? _orderModeItems.First(i => i.Mode == SortMode.ModifiedDesc);

            LanguageOptions.Add(new LanguageOption { DisplayName = "中文", Value = "zh-Hans" });
            LanguageOptions.Add(new LanguageOption { DisplayName = "English", Value = "en" });

            var configLang = App.SharedViewModel?.AppConfigs?.Language ?? "zh-Hans";
            SelectedLanguage = configLang == "en" ? LanguageOptions[1] : LanguageOptions[0];
            App.LocalizationService.SetLanguage(configLang);

            PropertyChanged += OnSettingsPropertyChanged;

            AddBackdropOptions();

            var configBackdrop = App.SharedViewModel?.AppConfigs?.SystemBackdropMode ?? "Mica";
            SelectedBackdrop = BackdropOptions.FirstOrDefault(b => b.Value == configBackdrop) ?? BackdropOptions[0];

            BuildTransitionModeItems();
            var transMode = App.SharedViewModel?.AppConfigs?.TransitionMode ?? "Default";
            SelectedTransitionModeItem = TransitionModeItems.FirstOrDefault(i => i.Value == transMode) ?? TransitionModeItems[0];

            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.PropertyChanged += OnAppConfigPropertyChanged;

            if (App.SharedViewModel?.AppConfigs?.TimeGroupedFolders is { } folders)
            {
                foreach (var folder in folders)
                    TimeGroupedFolders.Add(folder);
            }
            _ml.PropertyChanged += OnMlPropertyChanged;
            _ml.LanguageChanged += OnMlLanguageChanged;
            _subscribed = true;
        }

        private void AddBackdropOptions()
        {
            BackdropOptions.Add(new BackdropOption { DisplayName = _ml.BackdropMica, Value = "Mica" });
            BackdropOptions.Add(new BackdropOption { DisplayName = _ml.BackdropMicaAlt, Value = "MicaAlt" });
            BackdropOptions.Add(new BackdropOption { DisplayName = _ml.BackdropAcrylic, Value = "Acrylic" });
            BackdropOptions.Add(new BackdropOption { DisplayName = _ml.BackdropAcrylicThin, Value = "AcrylicThin" });
            BackdropOptions.Add(new BackdropOption { DisplayName = _ml.BackdropNone, Value = "None" });
        }

        private void OnAppConfigPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 转发属性变了要通知 UI（配置可能在别处被改），同时沿用原来的“任何改动都自动落盘”。
            switch (e.PropertyName)
            {
                case nameof(Configs.HomePageFullPath):
                    OnPropertyChanged(nameof(HomePageFullPath));
                    break;
                case nameof(Configs.MiddleFilesHeight):
                    OnPropertyChanged(nameof(MiddleFilesHeight));
                    break;
                case nameof(Configs.IfUsesWin32APIToGetIcon):
                    OnPropertyChanged(nameof(UseWin32ApiIcon));
                    break;
                case nameof(Configs.IconParallelLoadingCount):
                    OnPropertyChanged(nameof(IconParallelLoadingCount));
                    break;
            }

            AutoSaveConfig();
        }

        /// <summary>
        /// 断开对单例 AppConfigs 的事件订阅，防止页面销毁后 VM 仍被静态引用。
        /// 由 SettingsView.OnNavigatedFrom / SV_Unloaded 调用（幂等）。
        /// </summary>
        public void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.PropertyChanged -= OnAppConfigPropertyChanged;
            _ml.PropertyChanged -= OnMlPropertyChanged;
            _ml.LanguageChanged -= OnMlLanguageChanged;
            PropertyChanged -= OnSettingsPropertyChanged;
        }

        /// <summary>
        /// 重新挂上订阅（页面被重新挂载时用）。由 SettingsView.OnNavigatedTo 调用（幂等）。
        /// </summary>
        public void Resubscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.PropertyChanged += OnAppConfigPropertyChanged;
            _ml.PropertyChanged += OnMlPropertyChanged;
            _ml.LanguageChanged += OnMlLanguageChanged;
            PropertyChanged += OnSettingsPropertyChanged;
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
            // value 可能为 null：重建选项列表时，旧选中项不在新列表里，绑定会把它置空。
            // 不挡就会 NRE，或者在语言切换时把 null / 重复值写回配置并多落一次盘。
            if (_syncingOptions || value == null) return;
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.DefaultOrderMode = value.Mode.ToString();
        }

        partial void OnSelectedTransitionModeItemChanged(TransitionModeItem? value)
        {
            if (_syncingOptions || value == null) return;
            if (App.SharedViewModel?.AppConfigs != null)
                App.SharedViewModel.AppConfigs.TransitionMode = value.Value;
        }

        partial void OnSelectedBackdropChanged(BackdropOption? value)
        {
            if (_syncingOptions || value == null) return;
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
                if (!TimeGroupedFolders.Contains(path))
                    TimeGroupedFolders.Add(path);
                AutoSaveConfig();
            }
            NewTimeGroupedFolderPath = "";
        }

        [RelayCommand]
        private void DeleteTimeGroupedFolder(string path)
        {
            var folders = App.SharedViewModel?.AppConfigs?.TimeGroupedFolders;
            folders?.Remove(path);
            TimeGroupedFolders.Remove(path);
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
