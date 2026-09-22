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
			nameof(OpenFileLocation),
            nameof(CalculateSize),
            nameof(ItemCountSuffix),
            nameof(PinnedShortcutsTitle),
            nameof(CmdCut), nameof(CmdCopy), nameof(CmdPaste), nameof(CmdRename), nameof(CmdDelete),
            nameof(CmdPermanentDelete), nameof(CmdOpen), nameof(CmdOpenWith), nameof(CmdCopyPath),
            nameof(CmdPinToQuickAccess), nameof(CmdUnpinFromQuickAccess),
            nameof(CmdCopyFileAddress), nameof(CmdProperties), nameof(NewTextDocument), nameof(NewShortcut), nameof(NewFile),
            nameof(NewExcelSpreadsheet), nameof(NewWordDocument), nameof(NewPowerPointPresentation),
            nameof(CmdNew), nameof(CmdNewFolder), nameof(CmdShowMoreOptions),
            nameof(CmdSort),
            nameof(CmdOk), nameof(CmdCancel), nameof(PermanentDeleteConfirmTitle), nameof(PermanentDeleteConfirmMessage),
            nameof(MsgNoOptionsAvailable), nameof(MsgCannotLoadOptions),
            nameof(SortNameAsc), nameof(SortNameDesc), nameof(SortSizeDesc), nameof(SortSizeAsc),
            nameof(SortModifiedDesc), nameof(SortModifiedAsc), nameof(SortCreatedDesc), nameof(SortCreatedAsc),
            nameof(TimeGroupedFoldersHeader), nameof(TimeGroupedFolderPlaceholder), nameof(AddTimeGroupedFolder),
            nameof(PluginsSection),
            nameof(SystemBackdropMode), nameof(BackdropMica), nameof(BackdropMicaAlt),
            nameof(BackdropAcrylic), nameof(BackdropAcrylicThin), nameof(BackdropNone),
            nameof(TransitionMode), nameof(TransitionModeDefault), nameof(TransitionModeFade), nameof(TransitionModeNone),
            nameof(PluginManagement), nameof(PluginImport), nameof(PluginImportBtn), nameof(PluginInstalled),
            nameof(NavExplorer), nameof(NavPlugins), nameof(AppAuthorCredit),
            nameof(PluginsPageTitle), nameof(PluginsSettingsHeader),
            nameof(TooltipBack), nameof(TooltipForward), nameof(TooltipUp), nameof(TooltipRefresh),
            nameof(TooltipHome), nameof(TooltipSearch), nameof(TooltipCopyPath),
            nameof(TooltipNewTab), nameof(TooltipCloseTab),
            nameof(SearchPlaceholder), nameof(SearchStartHint), nameof(SearchLoading),
            nameof(SearchNoResults), nameof(SearchInProgress),
            nameof(FlyoutEmptyFolder), nameof(FlyoutInaccessible),
            nameof(PropertiesTitle), nameof(PropertiesType), nameof(PropertiesPath),
            nameof(PropertiesSize), nameof(PropertiesModified), nameof(PropertiesCreated),
            nameof(PropertiesProcesses),
            nameof(PropertiesClose), nameof(PropertiesFolder), nameof(PropertiesFile), nameof(PropertiesBytesFmt),
            nameof(FileOpProgressFmt), nameof(FileOpFailed), nameof(FileOpClipboardFailed),
            nameof(FileOpClipboardEmpty),
            nameof(ConflictTitle), nameof(ConflictMessageFmt), nameof(ConflictHint), nameof(ConflictReplace),
            nameof(ConflictKeepBoth), nameof(ConflictSkip),
            nameof(FileOperationsTitle), nameof(ClearCompleted), nameof(NoFileOperations),
            nameof(NewFileDefault), nameof(NewFolderDefault), nameof(NewTextDocumentDefault),
            nameof(NewShortcutDefault), nameof(NewExcelDefault), nameof(NewWordDefault), nameof(NewPPTDefault),
            nameof(AppName), nameof(AppVersion),
            nameof(LanguageChinese), nameof(LanguageEnglish), nameof(LanguageFumo),
            nameof(TimeGroupToday), nameof(TimeGroupYesterday), nameof(TimeGroupEarlierThisWeek),
            nameof(TimeGroupLastWeek), nameof(TimeGroupEarlierThisMonth), nameof(TimeGroupLastMonth),
            nameof(TimeGroupEarlierThisYear), nameof(TimeGroupLastYear), nameof(TimeGroupLongAgo),
            nameof(TreeThisPC), nameof(TreeNetwork), nameof(TreeLinux), nameof(TreeRecycleBin), nameof(TreeCloudDrives),
            nameof(RecycleRestore), nameof(RecycleDeleteConfirmTitle), nameof(RecycleDeleteConfirmMessage),
            nameof(RecycleEmpty), nameof(RecycleEmptyConfirmTitle), nameof(RecycleEmptyConfirmMessage),
            nameof(RecycleRestoreConflict), nameof(RecycleRestoreFailed),
            nameof(RecycleDeleteFailed), nameof(RecycleEmptyFailed),
            nameof(ColumnOriginalLocation),
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
        public string TransitionMode => _loc.GetString("TransitionMode");
        public string TransitionModeDefault => _loc.GetString("TransitionModeDefault");
        public string TransitionModeFade => _loc.GetString("TransitionModeFade");
        public string TransitionModeNone => _loc.GetString("TransitionModeNone");
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
        public string OpenFileLocation => _loc.GetString("OpenFileLocation");
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
        public string CmdPinToQuickAccess => _loc.GetString("CmdPinToQuickAccess");
        public string CmdUnpinFromQuickAccess => _loc.GetString("CmdUnpinFromQuickAccess");
        public string CmdCopyFileAddress => _loc.GetString("CmdCopyFileAddress");
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
        public string CmdSort => _loc.GetString("CmdSort");
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

        public string SystemBackdropMode => _loc.GetString("SystemBackdropMode");
        public string BackdropMica => _loc.GetString("BackdropMica");
        public string BackdropMicaAlt => _loc.GetString("BackdropMicaAlt");
        public string BackdropAcrylic => _loc.GetString("BackdropAcrylic");
        public string BackdropAcrylicThin => _loc.GetString("BackdropAcrylicThin");
        public string BackdropNone => _loc.GetString("BackdropNone");

        public string PluginManagement => _loc.GetString("PluginManagement");
        public string PluginImport => _loc.GetString("PluginImport");
        public string PluginImportBtn => _loc.GetString("PluginImportBtn");
        public string PluginInstalled => _loc.GetString("PluginInstalled");

        public string NavExplorer => _loc.GetString("NavExplorer");
        public string NavPlugins => _loc.GetString("NavPlugins");
        public string AppAuthorCredit => _loc.GetString("AppAuthorCredit");
        public string PluginsPageTitle => _loc.GetString("PluginsPageTitle");
        public string PluginsSettingsHeader => _loc.GetString("PluginsSettingsHeader");

        public string TooltipBack => _loc.GetString("TooltipBack");
        public string TooltipForward => _loc.GetString("TooltipForward");
        public string TooltipUp => _loc.GetString("TooltipUp");
        public string TooltipRefresh => _loc.GetString("TooltipRefresh");
        public string TooltipHome => _loc.GetString("TooltipHome");
        public string TooltipSearch => _loc.GetString("TooltipSearch");
        public string TooltipCopyPath => _loc.GetString("TooltipCopyPath");
        public string TooltipNewTab => _loc.GetString("TooltipNewTab");
        public string TooltipCloseTab => _loc.GetString("TooltipCloseTab");

        public string SearchPlaceholder => _loc.GetString("SearchPlaceholder");
        public string SearchStartHint => _loc.GetString("SearchStartHint");
        public string SearchLoading => _loc.GetString("SearchLoading");
        public string SearchNoResults => _loc.GetString("SearchNoResults");
        public string SearchInProgress => _loc.GetString("SearchInProgress");
        public string FlyoutEmptyFolder => _loc.GetString("FlyoutEmptyFolder");
        public string FlyoutInaccessible => _loc.GetString("FlyoutInaccessible");

        public string PropertiesTitle => _loc.GetString("PropertiesTitle");
        public string PropertiesType => _loc.GetString("PropertiesType");
        public string PropertiesPath => _loc.GetString("PropertiesPath");
        public string PropertiesSize => _loc.GetString("PropertiesSize");
        public string PropertiesModified => _loc.GetString("PropertiesModified");
        public string PropertiesCreated => _loc.GetString("PropertiesCreated");
        public string PropertiesProcesses => _loc.GetString("PropertiesProcesses");
        public string PropertiesClose => _loc.GetString("PropertiesClose");
        public string PropertiesFolder => _loc.GetString("PropertiesFolder");
        public string PropertiesFile => _loc.GetString("PropertiesFile");
        public string PropertiesBytesFmt => _loc.GetString("PropertiesBytesFmt");
        public string FileOpProgressFmt => _loc.GetString("FileOpProgressFmt");
        public string FileOpFailed => _loc.GetString("FileOpFailed");
        public string FileOpClipboardFailed => _loc.GetString("FileOpClipboardFailed");
        public string FileOpClipboardEmpty => _loc.GetString("FileOpClipboardEmpty");
        public string ConflictTitle => _loc.GetString("ConflictTitle");
        public string ConflictMessageFmt => _loc.GetString("ConflictMessageFmt");
        public string ConflictHint => _loc.GetString("ConflictHint");
        public string ConflictReplace => _loc.GetString("ConflictReplace");
        public string ConflictKeepBoth => _loc.GetString("ConflictKeepBoth");
        public string ConflictSkip => _loc.GetString("ConflictSkip");
        public string FileOperationsTitle => _loc.GetString("FileOperationsTitle");
        public string ClearCompleted => _loc.GetString("ClearCompleted");
        public string NoFileOperations => _loc.GetString("NoFileOperations");

        public string NewFileDefault => _loc.GetString("NewFileDefault");
        public string NewFolderDefault => _loc.GetString("NewFolderDefault");
        public string NewTextDocumentDefault => _loc.GetString("NewTextDocumentDefault");
        public string NewShortcutDefault => _loc.GetString("NewShortcutDefault");
        public string NewExcelDefault => _loc.GetString("NewExcelDefault");
        public string NewWordDefault => _loc.GetString("NewWordDefault");
        public string NewPPTDefault => _loc.GetString("NewPPTDefault");

        public string AppName => _loc.GetString("AppName");
        public string AppVersion => _loc.GetString("AppVersion");

        public string LanguageChinese => _loc.GetString("LanguageChinese");
        public string LanguageEnglish => _loc.GetString("LanguageEnglish");
        public string LanguageFumo => _loc.GetString("LanguageFumo");

        public string TimeGroupToday => _loc.GetString("TimeGroup.Today");
        public string TimeGroupYesterday => _loc.GetString("TimeGroup.Yesterday");
        public string TimeGroupEarlierThisWeek => _loc.GetString("TimeGroup.EarlierThisWeek");
        public string TimeGroupLastWeek => _loc.GetString("TimeGroup.LastWeek");
        public string TimeGroupEarlierThisMonth => _loc.GetString("TimeGroup.EarlierThisMonth");
        public string TimeGroupLastMonth => _loc.GetString("TimeGroup.LastMonth");
        public string TimeGroupEarlierThisYear => _loc.GetString("TimeGroup.EarlierThisYear");
        public string TimeGroupLastYear => _loc.GetString("TimeGroup.LastYear");
        public string TimeGroupLongAgo => _loc.GetString("TimeGroup.LongAgo");

        // ===== 侧栏位置与回收站操作 =====
        public string TreeThisPC => _loc.GetString("Tree.ThisPC");
        public string TreeNetwork => _loc.GetString("Tree.Network");
        public string TreeLinux => _loc.GetString("Tree.Linux");
        public string TreeRecycleBin => _loc.GetString("Tree.RecycleBin");
        public string TreeCloudDrives => _loc.GetString("Tree.CloudDrives");
        public string RecycleRestore => _loc.GetString("Recycle.Restore");
        public string RecycleDeleteConfirmTitle => _loc.GetString("Recycle.DeleteConfirmTitle");
        public string RecycleDeleteConfirmMessage => _loc.GetString("Recycle.DeleteConfirmMessage");
        public string RecycleEmpty => _loc.GetString("Recycle.Empty");
        public string RecycleEmptyConfirmTitle => _loc.GetString("Recycle.EmptyConfirmTitle");
        public string RecycleEmptyConfirmMessage => _loc.GetString("Recycle.EmptyConfirmMessage");
        public string RecycleRestoreConflict => _loc.GetString("Recycle.RestoreConflict");
        public string RecycleRestoreFailed => _loc.GetString("Recycle.RestoreFailed");
        public string RecycleDeleteFailed => _loc.GetString("Recycle.DeleteFailed");
        public string RecycleEmptyFailed => _loc.GetString("Recycle.EmptyFailed");
        public string ColumnOriginalLocation => _loc.GetString("Column.OriginalLocation");

        public void RefreshAll()
        {
            foreach (var propName in AllPropertyNames)
                OnPropertyChanged(propName);
        }
    }
}
