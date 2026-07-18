using CommunityToolkit.Mvvm.ComponentModel;
using FastFluentFilesFolders.Services;
using System.Collections.Generic;

namespace FastFluentFilesFolders.ViewModels
{
    public partial class MultiLanguageStringsViewModel : ObservableObject
    {
        private readonly LocalizationService _loc;

        private static readonly string[] AllPropertyNames =
        {
            nameof(SettingsTitle), nameof(GeneralSection), nameof(HomePagePath), nameof(DefaultSortOrder),
            nameof(SortBy), nameof(AppearanceSection), nameof(MiddleFilesHeight), nameof(AdvancedSection),
            nameof(UseWin32Icon), nameof(PerformanceSection), nameof(IconParallelLoading), nameof(DebugSection),
            nameof(UserConfigPath), nameof(SaveSettings), nameof(LanguageLabel), nameof(AboutSection),
            nameof(AuthorTip), nameof(RepositoryTip),
            nameof(ColumnName), nameof(ColumnModifiedDate), nameof(ColumnCreatedDate), nameof(ColumnSize),
            nameof(CalculateSize),
            nameof(ItemCountSuffix),
            nameof(PinnedShortcutsTitle),
            nameof(CmdCut), nameof(CmdCopy), nameof(CmdPaste), nameof(CmdRename), nameof(CmdDelete),
            nameof(CmdPermanentDelete), nameof(CmdOpen), nameof(CmdOpenWith), nameof(CmdCopyPath),
            nameof(CmdProperties), nameof(NewTextDocument), nameof(NewShortcut), nameof(NewFile),
            nameof(NewExcelSpreadsheet), nameof(NewWordDocument), nameof(NewPowerPointPresentation),
            nameof(CmdNew), nameof(CmdNewFolder), nameof(CmdShowMoreOptions),
            nameof(CmdOk), nameof(CmdCancel), nameof(PermanentDeleteConfirmTitle), nameof(PermanentDeleteConfirmMessage),
            nameof(MsgNoOptionsAvailable), nameof(MsgCannotLoadOptions),
            nameof(SortNameAsc), nameof(SortNameDesc), nameof(SortSizeDesc), nameof(SortSizeAsc),
            nameof(SortModifiedDesc), nameof(SortModifiedAsc), nameof(SortCreatedDesc), nameof(SortCreatedAsc),
            nameof(TimeGroupedFoldersHeader), nameof(TimeGroupedFolderPlaceholder), nameof(AddTimeGroupedFolder),
            nameof(PluginsSection),
            nameof(PluginManagement), nameof(PluginImport), nameof(PluginImportBtn), nameof(PluginInstalled),
        };

        public MultiLanguageStringsViewModel(LocalizationService loc)
        {
            _loc = loc;
            _loc.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LocalizationService.CurrentLanguage))
                    RefreshAll();
            };
        }

        public string Get(string key) => _loc.GetString(key);

        public string this[string key] => _loc.GetString(key);

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
        public string AboutSection => _loc.GetString("AboutSection");
        public string AuthorTip => _loc.GetString("AuthorTip");
        public string RepositoryTip => _loc.GetString("RepositoryTip");

        public string ColumnName => _loc.GetString("ColumnName");
        public string ColumnModifiedDate => _loc.GetString("ColumnModifiedDate");
        public string ColumnCreatedDate => _loc.GetString("ColumnCreatedDate");
        public string ColumnSize => _loc.GetString("ColumnSize");
        public string CalculateSize => _loc.GetString("CalculateSize");
        public string ItemCountSuffix => _loc.GetString("ItemCountSuffix");

        public string PinnedShortcutsTitle => _loc.GetString("PinnedShortcuts");

        public string CmdCut => _loc.GetString("CmdCut");
        public string CmdCopy => _loc.GetString("CmdCopy");
        public string CmdPaste => _loc.GetString("CmdPaste");
        public string CmdRename => _loc.GetString("CmdRename");
        public string CmdDelete => _loc.GetString("CmdDelete");
        public string CmdPermanentDelete => _loc.GetString("CmdPermanentDelete");
        public string CmdOpen => _loc.GetString("CmdOpen");
        public string CmdOpenWith => _loc.GetString("CmdOpenWith");
        public string CmdCopyPath => _loc.GetString("CmdCopyPath");
        public string GoHome => _loc.GetString("GoHome");
        public string CmdProperties => _loc.GetString("CmdProperties");
        public string NewTextDocument => _loc.GetString("NewTextDocument");
        public string NewShortcut => _loc.GetString("NewShortcut");
        public string NewFile => _loc.GetString("NewFile");
        public string NewExcelSpreadsheet => _loc.GetString("NewExcelSpreadsheet");
        public string NewWordDocument => _loc.GetString("NewWordDocument");
        public string NewPowerPointPresentation => _loc.GetString("NewPowerPointPresentation");
        public string CmdNew => _loc.GetString("CmdNew");
        public string CmdNewFolder => _loc.GetString("CmdNewFolder");
        public string CmdShowMoreOptions => _loc.GetString("CmdShowMoreOptions");
        public string CmdOk => _loc.GetString("CmdOk");
        public string CmdCancel => _loc.GetString("CmdCancel");
        public string PermanentDeleteConfirmTitle => _loc.GetString("PermanentDeleteConfirmTitle");
        public string PermanentDeleteConfirmMessage => _loc.GetString("PermanentDeleteConfirmMessage");
        public string MsgNoOptionsAvailable => _loc.GetString("MsgNoOptionsAvailable");
        public string MsgCannotLoadOptions => _loc.GetString("MsgCannotLoadOptions");

        public string SortNameAsc => _loc.GetString("SortNameAsc");
        public string SortNameDesc => _loc.GetString("SortNameDesc");
        public string SortSizeDesc => _loc.GetString("SortSizeDesc");
        public string SortSizeAsc => _loc.GetString("SortSizeAsc");
        public string SortModifiedDesc => _loc.GetString("SortModifiedDesc");
        public string SortModifiedAsc => _loc.GetString("SortModifiedAsc");
        public string SortCreatedDesc => _loc.GetString("SortCreatedDesc");
        public string SortCreatedAsc => _loc.GetString("SortCreatedAsc");

        public string TimeGroupedFoldersHeader => _loc.GetString("TimeGroupedFoldersHeader");
        public string TimeGroupedFolderPlaceholder => _loc.GetString("TimeGroupedFolderPlaceholder");
        public string AddTimeGroupedFolder => _loc.GetString("AddTimeGroupedFolder");

        public string PluginsSection => _loc.GetString("PluginsSection");

        public string PluginManagement => _loc.GetString("PluginManagement");
        public string PluginImport => _loc.GetString("PluginImport");
        public string PluginImportBtn => _loc.GetString("PluginImportBtn");
        public string PluginInstalled => _loc.GetString("PluginInstalled");

        public void RefreshAll()
        {
            foreach (var propName in AllPropertyNames)
                OnPropertyChanged(propName);
        }
    }
}
