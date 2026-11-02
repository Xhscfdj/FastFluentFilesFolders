# Changelog

本文件记录本项目所有值得记录的变更。

格式遵循 [Keep a Changelog 2.0.0](https://keepachangelog.com/en/2.0.0/)，版本号遵循 [语义化版本 2.0.0](https://semver.org/lang/zh-CN/spec/v2.0.0.html)。

## [Unreleased]

> 已知问题：AOT 发布命令里**不能**加 `-p:PublishTrimmed=false`：AOT 必然裁剪，会直接报 `PublishTrimmed is implied by native compilation and cannot be disabled`（这条与 `WinUI.TableView` 无关，它只会出 `WMC1510` 警告）；正确命令是 `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:WindowsPackageType=None -p:PublishAot=true -p:PlatformTarget=x64`。
> 另：不要给工程直接加 `Microsoft.Windows.CsWinRT` 包。它会把 `cswinrt.exe` 拉进构建重新生成整套 WinRT 投影，而本机只有 Windows SDK `10.0.22621.0` / `10.0.26100.0`，缺 `Platforms\UAP\10.0.19041.0\Platform.xml`，构建会直接失败（`Could not read the Windows SDK's Platform.xml`）。AOT 需要的 unsafe 生成已由 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` 满足（app 与 `WinUI.TableView` 都设了）。

### Added

- 新增 **AOT 兼容性门禁** `tools/check-aot.ps1`：用真正的 `dotnet publish -p:PublishAot=true` 跑一遍，把 AOT 下不安全的代码统计成「项目代码 N 条 / 依赖库 M 条」并给退出码，用来回答「现在能不能 AOT 发布」而不是靠人记。实测当前 **NOT AOT READY**：publish 暴露 **350 条**门禁类警告——项目代码 131 条（`ShellLocations.cs` 60 + `QuickAccessHelper.cs` 14 的 `dynamic` / `Type.GetTypeFromProgID`、`MainWindowViewModel.cs` 17 条 `MVVMTK0046` 命令、`PluginPackage.cs` 8 条 `JsonSerializer`、`ExternalPluginLoader.cs` 4 条 `Assembly.LoadFrom` 等），依赖库 219 条（WinUI/WindowsAppSDK/CommunityToolkit 自带，改不了、只能白名单压制）。**注意 build 抓不到这些**：同一份代码 `build -p:PublishAot=true` 只报 19 条，必须走 publish，脚本已按此实现。脚本结束会自动 Rebuild 一次把 `runtimeconfig.json` 恢复成允许反射绑定的正常状态，避免留下带 AOT 标志的产物
- 排序状态持久化：`Configs` 新增 `General:LastSortPath` / `General:LastSortAscending`（写进 `user_configs.json`，沿用导航时那套 2 秒防抖落盘；为此把防抖保存抽成公共的 `DebounceSaveConfig`），启动时的初始标签页与之后 `Ctrl+T` 新建的标签页都沿用上次的排序，重启后排序状态不丢；用户排序只更新内存 + 防抖落盘，不额外增加写盘次数
- `WinUI.TableView` 新增“内容整体替换的入场动画”能力：`AnimateContentReplacement`（默认关）、`ContentReplacementAnimationDuration`（默认 200ms）、`ContentReplacementAnimationWindow`（默认 300ms）、`ContentReplacementStagger` / `ContentReplacementMaxStagger`（逐行错开步长与上限）、`ContentReplacementSlideOffsetY`（入场时从下方偏移多少像素上滑，0 = 纯淡入）、`BeginContentReplacementAnimation()`。原地换内容时 WinUI 会把 Reset 和随后的插入并在**一次布局**里处理，自带的容器过渡（`EntranceThemeTransition` 等）不会播，所以由库显式补一个：调用方在换内容前调用 `BeginContentReplacementAnimation()`，窗口期内实体化的行各播一次入场动画，可按行序号错开（`DoubleAnimation.BeginTime`）形成“依次流入”，也可从下方偏移上滑（`TranslateTransform.Y`）复刻自带过渡的观感。两个挂点都接（`PrepareContainerForItemOverride` + `ContainerContentChanging`，行侧按 token 去重），因为复用容器不一定每次都会走 prepare；窗口按**时间**并在有行播放时续期（总上限 1s）——按“一次 LayoutUpdated”关窗会在行还没实体化时就把窗口关掉。实现细节：①`Opacity` 先**同步落 0**（否则复用容器会先全不透明闪一帧 = “抽一下”）；②动画用 **XAML `Storyboard` 直接动画行的 `Opacity` / 变换**，而不是 element visual 上的 Composition 动画 —— 实测后者会“按时播完但完全不影响渲染”，行一直停在 `Opacity=0`，到完成回调才写回 1，看起来就是没动画（诊断日志里 `probe +100ms: opacities=[0,0,0,0,0]`、`row done opacityAtEnd=1` 就是这个现象）；③行**已加载就立刻起播**，未进树的才挂 `Loaded`（早先版本一律排一轮低优先级回调，要等整个挂载忙完才起播，实测会多出约 130ms 的“空白”，详见下面那条录屏实测）；④容器回收时 `CancelEntranceAnimation()` 取消、恢复不透明度并把上滑变换归位
- 设置里的「切换动画」新增第四项 **「溪流」（Stream）**：每行依次淡入（逐行错开 15ms、封顶 150ms）。字符串补了中英文（`TransitionModeStream` = 溪流 / Stream），设置下拉、`MultiLanguageStringsViewModel` 一起跟上

### Changed

- **发布版本号收口到 `build.ps1 -Version` 这一处**：`FastFluentFilesFolders/build.ps1` 新增必填参数 `-Version`（2~4 段纯数字，带 `-beta` / `+hash` 后缀会被拒），它同时喂给两处——编译安装包时 `ISCC /DDestinationArch=<arch> /DMyAppVersion=<Version>`（`setup.iss` 的 `#define MyAppVersion "1.1.1"` 改为 `#ifndef` 兜底，写死值只在单独手工跑 ISCC 时生效），生成 AppCast 时 `--file-version <Version>`；因此安装包的版本资源与 appcast 的 `sparkle:version` 不可能再各自漂移（此前 setup.iss 是 `1.1.1` 而 `Configs.cs` 已是 `1.2.0`）。构建期加了三道闸：每个架构编译完立刻校验安装包 `VersionInfo.ProductVersion` 等于 `-Version`、生成完校验 appcast 的 `sparkle:version` 等于 `-Version`，任一不符 `exit 1`；`ViewModels/Configs.cs` 里的 `Version` 与 `-Version` 不同时只告警不中断（build.ps1 不改源码，但客户端拿它和 appcast 比对，不同步用户就收不到这次更新）。用法变成 `.\build.ps1 -Version 1.2.0`，原先的无参调用不再可用；脚本开头 `Set-Location $PSScriptRoot`，在任意工作目录执行都能找对 `setup.iss` / `Output` / `AppCast`
- **两目录来回切换不再重复重建子项**：以前离开目录立刻 `ReleaseChildren()`，回到该目录必须重新枚举磁盘 + 重建全部 `FileSystemNodeViewModel`（诊断探针实测：362 项的目录每次导航临时分配约 **3MB**、`live` 存活对象却几乎不涨 —— 说明是重复分配而不是泄漏，`preloaded` 恒为 0 也印证每次都在重新枚举）。现在把「最近离开的目录」放进容量 1 的热缓存（`MainWindowViewModel._warmFolders`），回访时 `IsLoaded` 仍为 true，直接复用既有 `Children`（探针里 `preloaded` 会变成 1）。配套：①保鲜期 **8 秒**，超时回访照常重读磁盘，用户主动点刷新永远强刷；②项数 > 5000 的目录不进缓存；③被其它标签页 / 目录树根 / 固定栏持有的目录不进缓存（否则淘汰时 `ReleaseChildren` 会把它们清空）；④文件操作走 `InvalidateFolderByPath` 时清空缓存，保住「操作后立即看到结果」这条硬要求。纯 C# 逻辑、无反射无委托，AOT 友好
- **内容没变时不再整批重建表格**：`GroupedFileList.SetItems` 增加等价短路——输入是同一批实例且顺序一致时直接返回，不再 `Clear()` + N 次 `Add`（那会丢掉全部行容器并让面板重排）。判定用「引用哈希的组合指纹」而不是保存节点引用，避免这份历史把已 `ReleaseChildren` 的节点钉住；分组目录会额外校验时间组键仍然有效（`SortByTime == GetTimeGroup(LastModifiedTime)`），否则文件改过时间后会留在旧分组里不刷新。刷新路径因此在内容未变时完全不重建（也就不会闪一下）
- **每次导航不再构建两遍表格数据源**：`UpdateCurrentFolderContentAsync` 以前先在后台 `new GroupedFileList()` 并完整 `SetItems` 一遍，前台拿到后又 `SetItems` 一遍（`UpdateSourcePrebuilt` 还要先把分组头过滤掉）——等于每次导航干两遍分组与重排。现在后台只算「按当前排序排好的顺序 + 分组键写回」（`BuildSortedSnapshot`，与 `GroupedFileList` 共用同一套比较器），真正的构建只发生在前台那唯一一次 `SetItems` 里；`PendingPrebuiltSource` 从 `GroupedFileList` 换成只装顺序的 `PrebuiltContent`，`UpdateSourcePrebuilt` 不再重复排序、不再过滤分组头。配合上面的热缓存，A↔B 来回切换时诊断探针的 `alloc` 从约 3MB / `dt` 31~47ms 降到命中态的 **1.6MB / 15~16ms**
- **切换语言不再卡顿、也不再每次涨 20MB**：一次切换会发 ~150 条文案属性通知，而 `MiddleFilesView` 与 `FileOperationsButton` 之前是**逐条响应**——每收到一条就 `RefreshAllStrings()` 一遍，等于把工具栏 `PrimaryCommands.Clear()` + 重建 8 个按钮、3 个菜单、20 多个 `MenuFlyoutItem` 各执行 150 遍（诊断探针实测：**每次切换稳定临时分配 46MB**，而强制回收后的存活对象几乎不涨，所以不是泄漏、是分配风暴加 WinUI 原生控件滞留，体感就是切一次卡好几秒）。现在 `MultiLanguageStringsViewModel` 在把属性逐条通知完之后额外触发一次 `LanguageChanged`，上述两处改为订阅它，工具栏/右键菜单/列头/分组名各只整体刷新一次；设置页里那 150 条逐条转发保留（`x:Bind` 的 `Mode=OneWay` 依赖按属性名通知，且它只做转发、本身很轻）
- 设置页切换语言时**整页文案即时刷新**（以前只有带 `Mode=OneWay` 的那部分会变，`SettingsTitle`、`GeneralSection`、`HomePagePath`、`LanguageLabel`、`DefaultSortOrder`、`SortBy` 这几处停在旧语言，要退出再进才更新）：现在这 6 处也走 OneWay，并且全部文案都经页面私有的 `SettingsViewModel` 转发，语言切换由 VM 按属性名精确通知（`OnMlPropertyChanged`），不会牵动 180 多个字符串整表重刷
- Native AOT 版的「快速访问固定项」与 COM 版**完全对齐**（读取真实固定项 + 能真正写入系统）：新增 `Services/ShellQuickAccess`，完全绕开 .NET 的内置 COM 支持（AOT 下 RCW/CCW 封送、`Type.GetTypeFromProgID`、`dynamic`、`[ComImport]` 全部不可用），改用**裸 vtable 调用**——`SHParseDisplayName`/`SHBindToParent` 拿原始接口指针，再按下标手工调用 `IShellFolder`(EnumObjects/BindToObject/GetUIObjectOf)、`IEnumIDList`(Next)、`IContextMenu`(QueryContextMenu/GetCommandString/InvokeCommand)。枚举用 `ILCombine` + `SHGetNameFromIDList` 取真实路径；固定/取消固定用 canonical verb（`GetCommandString(GCS_VERBW)` 找 `pintohome`/`unpinfromhome`，与系统语言无关）——取消固定的动词挂在「快速访问命名空间里的那一项」上，所以先枚举该命名空间按路径匹配再调。Shell 扩展对线程套间敏感，实际调用都跑在**自行 `CoInitializeEx(STA)` + 预建消息队列**的后台线程上并带超时（AOT 下 CLR 不会替我们初始化套间，缺这一步 `InvokeCommand` 会死等）。另加防挂死判断：对已固定的文件夹不再去查文件系统项上下文菜单（那个分支实测会阻塞到超时）。实测（AOT 发布产物）：侧栏固定项与 COM 版逐项一致（20 项）；`pintohome` / `unpinfromhome` 调用后经 COM 侧真值确认「新固定项出现 / 移除」。配置项 `General:AotPinnedFolders` 退化为最后兜底（裸调用与 COM 都不可用时才用）
- `TabView` 改为显式维护 `TabItems`，不再用 `TabItemsSource` 绑定 `ObservableCollection<ExplorerTab>`：Native AOT 下 TabView 把 `ItemsSource` 转成 `IBindableIterable` / `IIterable` 会失败，布局阶段直接抛 `COMException 0x80004005`（事件日志 `0xc000027b`，与 microsoft-ui-xaml#11248 / #10409 / #11065 是同一路径），表现为 AOT 产物启动 4~5 秒后整个程序崩掉——与表格内容、过渡动画、背景材质都无关，只跟有没有 `TabItemsSource` 有关。现在 `ExplorerPage` 按 `VM.Tabs` 增删 `TabViewItem`（`Tag` 存 tab，`Header` 是代码拼的「文件夹图标 + 标题」，标题/图标变化由 `ExplorerTab.PropertyChanged` 就地刷新），新建、关闭、切换标签页的行为不变，只是不再走「托管集合跨 ABI」那条路。实测：普通 Release 与 AOT 发布产物都能正常启动，AOT 版里新建/关闭标签页后进程依然存活、`crash.log` 零新增
- 开启 `ImplicitUsings`，并去掉"按配置关闭 `Configuration.Binder` 源生成器"的临时 workaround：那个注释为"Release 模式下有 bug"的问题，其实就是该生成器**依赖隐式 using**（生成的代码用 `HashSet<>` 却不带 using）；而它只在 AOT 场景才被 SDK 默认打开，所以以前是"开了 `PublishAot` 就报 `CS0246`、关掉就没事"。现在 Debug、Release x64、Release x64 + AOT 三种配置全部 0 错误，AOT 发布路径既能编译通过，也能用上 trim/AOT 安全的配置绑定生成器（不再退化成反射绑定）。验证：三种配置各跑一次 `-t:Rebuild`，均 `0 个错误`
- **关闭并移除过渡动画的临时诊断**：库侧 `WinUI.TableView.TableViewTransitionTrace`、app 侧 `Helpers/TransitionLog.cs`（写 `%LOCALAPPDATA%\FastFluentFilesFolders\transition.log`）以及库内所有 `arm / prepare / ccc / play / scrolled to top / window closed / row done / probe` 埋点全部删除；`BeginContentReplacement` 里那个 100ms 后回读行 `Opacity` 的探针、2 秒跟踪窗口（`IsContentReplacementEntranceTraceWindow`）与 `_contentReplacementEntranceLastArmTick` 也一并去掉，`FinishContentReplacement` / `OnContentReplacementLayoutUpdatedOrFinish` 不再需要 `reason` 参数。锚定偏移的滚顶复位、本帧末统一开播这些**正经逻辑原样保留**，只是不再写日志。旧日志文件 `%LOCALAPPDATA%\FastFluentFilesFolders\transition.log` 可以直接删掉（两个已成占位注释的空文件 `TransitionLog.cs` / `TableViewTransitionTrace.cs` 也可以删）
- 录屏逐帧实测「默认」的入场动画并对齐 WinUI.TableView SampleApp（30fps 抽帧 + 行剖面能量/位移分析）：改动前是“**空白 ~130ms**（整块为空）→ 233ms 线性淡入 → 位移 ~46px 收到 0”，Sample 侧同一指标是“空白 ~33ms（1 帧）→ 约 100ms 填满、位移约 11px、明显先快后慢”。据此改了两处：①库侧去掉起播前的低优先级排队；②「默认」参数由 240ms/30px 线性改为“短时长 + 小位移 + `CubicEase` EaseOut”（透明度与上滑两条动画都带缓动，参数在 `LrsTableView.ApplyTransitionMode` 的 Default 分支里可调，当前取 130ms/22px），观感从“拖沓”变“利索”
- 进入目录的动画严格按设置分四种：**默认** = 复刻自带容器过渡的观感（每行“从下方小位移上滑 + 淡入”、EaseOut、整批一起不逐行错开 —— 原地换内容时自带的 `ContentThemeTransition`/`EntranceThemeTransition` 不会播，所以按同样的参数自己播一遍，参数取自 Sample 的实测值）；**溪流** = 逐行错开淡入（200ms）；**淡入** = 整表 150ms 淡入（分组文件夹延迟一帧开始，避免动画跑完内容才出现）；**关闭** = 完全无动画。默认模式里不再保留会跟入场动画打架的 `ContentThemeTransition` / `EntranceThemeTransition`（只保留新增/删除/补位动画，粘贴删除的动效不受影响）。三种带入场动画的模式都走“稳定数据源 + 原地换内容”，不换 `ItemsSource`，因此不付当初那笔重建行容器的代价
- 排序应用合并为单遍构建，降低分组文件夹构建开销：`GroupedFileList.SetItems` 增加可选 `sortPath/ascending`，在第一次 `RebuildFlat` 前就完成“分组 + 组内排序”；`UpdateCurrentFolderContentAsync` 不再先 `SetItems` 再 `SortWithinGroups`（少一次完整 `RebuildFlat`）；排序比较器统一收口到 `GetComparison`，并用 `List.Sort` 替换 LINQ `OrderBy(...).ToList()`，减少临时分配
- 手动排序统一“**目录永远在文件上面**”（Windows 资源管理器风格）：`GetComparison` 把 `IsDirectory` 作为第一优先级、所选列作为第二优先级，且目录优先不受升/降序影响——名称降序时表现为“目录 Z→A 在上、文件 Z→A 在下”；平铺视图、分组文件夹的每个时间组内部、以及粘贴/新建后的增量插入，都共用这一条比较规则，不再出现目录和文件被混排
- `ResetScrollPosition()` 降开销：不再每次导航都遍历可视树找所有 `ScrollViewer` 并无条件 `ChangeView`，改为缓存“可纵向滚动的那个（数据行所在）”`ScrollViewer`，命中后只对它动一次；偏移本来就是 0 时直接跳过（`ChangeView` 会触发一次布局，无意义调用就是白多一帧）；缓存未命中时才回退整树遍历，且只对确实有偏移的 `ScrollViewer` 复位。`Unloaded` 时清缓存，避免持有已卸载控件

### Fixed

- 修复点「刷新」时**目录里什么文件都没变就完全看不出反应**（现在会按设置里的「切换动画」重播一遍进入目录的入场动画）：根因是 `GroupedFileList.SetItems` 的等价短路（输入仍是同一批实例、顺序一致时直接返回）同时也把入场动画短路掉了——表格的入场动画是靠「行容器被重新 prepare」才开播的，内容没重建就压根没有容器可播，整次刷新只有磁盘枚举而无任何视觉反馈。现在 `SetItems` 返回 `bool`（content 是否真的重建），`LrsTableView.UpdateSource` / `UpdateSourcePrebuilt` 在拿到 `false` 时调用库新增的 `TableView.ReplayContentReplacementEntrance()`：按当前模式的同一套参数（默认 = 130ms + 22px 上滑、溪流 = 200ms 逐行错开、淡入 = 350ms 纯淡入；「关闭」时不播）对**已实体化、即视口内的行**重播一次入场动画。内容确实变了时 SetItems 会真的重建，仍走原来那次入场动画，不会重复播两批
- 修复设置页展开「按时间分组文件夹」**直接崩溃**（`crash.log` 里只留下 `FrameworkElement.MeasureOverride` 的 `COMException 0x80004005`，看不出原因）：根因是 `FastFluentFilesFolders.csproj` 里**误留了一行裸的 `<PublishAot>true</PublishAot>`**——就在正上方三行注释写着「不要在普通 build 中打开 PublishAot！」的下面，于是每次 build 写出的 `runtimeconfig.json` 都带 `System.Runtime.InteropServices.BuiltInComInterop.IsSupported=false`，**所有 `{Binding}` 的 ICustomProperty 反射查找全部失效**。该列表项模板里那条 `{Binding DataContext.DeleteTimeGroupedFolderCommand, ElementName=SV}` 在展开时随项模板实例化而抛异常，在布局期成为 stowed exception 直接带走进程；列表为空时项模板不实例化，所以一直没暴露（本机配置里恰好有一条 `TimeGroupedFolders`）。修法：①删掉那行 `PublishAot`（要 AOT 就用命令行 `/p:PublishAot=true`，这条规则 CHANGELOG 早写过、又被违反了一次）；②把该删除按钮从运行时绑定改成 `Click` 事件 + `Tag` 传路径，彻底不依赖 ICustomProperty（否则即使不崩，AOT 下按钮也是点不动的）。Rebuild 后 `runtimeconfig.json` 里 `BuiltInComInterop.IsSupported` / `IsDynamicCodeSupported` 两个键都不再出现（= 默认允许反射绑定），应用启动正常
- 修复切换语言后设置页**三个下拉框仍显示旧语言**（排序方式、背景材质、切换动画）：这些选项的文字是 `SettingsViewModel` 构造时按**当时的**语言取值的普通 POCO 字符串，列表之后从不重建，所以只转发属性通知不会让它们更新；另外「排序方式」「语言」两个 `ComboBox` 的 `ItemsSource` 还漏了 `Mode`（`x:Bind` 默认 `OneTime`）。现在 `SettingsViewModel` 订阅 `MultiLanguageStringsViewModel.LanguageChanged`，在里面整表重建三个选项列表、按**语义值**（`Mode` / `Value`）恢复选中——重建期间用 `_syncingOptions` 挡住 `OnSelectedXxxChanged` 的写回，避免把 `null` 或重复值写进 `user_configs.json` 并多落一次盘（`OnSelectedOrderModeItemChanged` 原来还没有 null 检查，重建时会直接 NRE）；`TransitionModeItems` 从裸属性改成带通知的属性，两处 `ItemsSource` 补上 `Mode=OneWay`。列表换新后 `ComboBox` 会重建项容器，`DataTemplate` 里的 `{x:Bind Display}` 重新求值，文字即跟随语言
- 修复 `PublishAot=true` 时**右键一弹出菜单整个程序就崩**（事件日志 `0xc000027b`、故障模块 `Microsoft.UI.Xaml.dll`，`crash.log` 里是 `XamlParseException: Failed to assign to property 'Microsoft.UI.Xaml.UIElement.Visibility'`，WER 签名 `E_XAMLPARSEFAILED 0x802B000A`）：根因是自定义菜单模板 `ThemedMenuItemStyle` 里两处 `{Binding …, RelativeSource=TemplatedParent, Converter=…}` 用到的转换器**没标 `partial`**。它们实现的是 WinRT 接口 `IValueConverter`，要经 `Binding.Converter` 跨 ABI 传递，而只有 partial 类型才会由 CsWinRT 源生成器生成 AOT 安全的 COM 虚表；Native AOT（以及 `PublishAot=true` 连带把 `runtimeconfig.json` 里 `BuiltInComInterop.IsSupported` 写成 `false` 的普通构建）下漏掉 `partial` 时转换器根本封送不进去，XAML 把原始值往 `Visibility` 上一赋就抛异常，异常在布局期变成 stowed exception 直接带走进程——这也是为什么关掉 AOT（走反射封送）就完全正常，而菜单以外的界面看不出来。修复：给 `StringToVisibilityConverter`、`BoolToVisibilityConverter`、`InvertBoolConverter`、`InvertVisibilityConverter`、`ExpandGlyphConverter` 补 `partial`，并按同一条规则补齐 `CopyAddressMenuFlyoutItem`（面包屑里的复制地址菜单项）与 `GroupedFileList`（表格 `ItemsSource`），`CsWinRT` 系列 AOT 警告（`CsWinRT1028`）全部清零。实测：`PublishAot=true` 的 Release 构建与 Native AOT 发布产物里，文件菜单与空白处菜单都能正常弹出、关闭，进程存活、`crash.log` 零新增，表格行数与内容不受影响
- 修复设置页**每进一次就泄漏一份页面**的内存泄漏（换成 `x:Bind` 后的真凶不是 `x:Bind` 本身，而是「页面直接绑定 App 级单例」这个写法）：x:Bind 的 `OneWay` / `TwoWay` 会把监听器挂在**源对象**上，页面原来直接绑单例 `MultiLanguageStringsViewModel`（30 处文案）和单例 `Configs`（4 个配置项 + `TimeGroupedFolders` 集合），而这些监听器只由绑定类的 `StopTracking()` 释放——实测 WinUI 3 对 Frame 托管的页面每次都不释放。于是单例的 `PropertyChanged` 里一直留着这一页的绑定跟踪对象，跟踪对象又持有 `SettingsViewModel` 和整棵元素树，页面连同 VM 永不回收。用 `dotnet-dump` 抓堆实证：改前连点 25 次「设置 / 文件浏览」，进程里积着 26 个 `SettingsView`、26 个 `SettingsViewModel`、26 个 `SettingsView_obj1_BindingsTracking`，`gcroot` 路径为 `Configs → ObservableCollection<String> → PropertyChangedEventHandler → …BindingsTracking → SettingsViewModel`；改后同样跑 16 次，三个类型各只剩 **1** 个（就是当前这一页），私有内存稳定在 ~250 MB 不再爬升。修法：页面不再直接绑单例——4 个配置项与「按时间分组文件夹」列表改为经 `SettingsViewModel` 转发（`HomePageFullPath` / `MiddleFilesHeight` / `UseWin32ApiIcon` / `IconParallelLoadingCount` 与页面私有的 `ObservableCollection<string> TimeGroupedFolders` 副本），32 条多语言文案也改为 VM 转发属性，引用环全部收在本页内部；另外把「订阅/退订」做成幂等的 `Unsubscribe()` / `Resubscribe()`，`SV_Unloaded` 里也退订一次（`Unloaded` 与 `OnNavigatedFrom` 的先后顺序不固定，只靠后者会漏），并且不再在 `Unloaded` 里把 `_settingsVm` / `DataContext` 置空——那会让 x:Bind 的两向写回静默失效（`UpdateTwoWay_*` 依赖 `dataRoot._settingsVm`）
- 修复设置页改用 `x:Bind` 后四个下拉框（语言、普通文件夹默认排序方式、系统背景材质、切换动画）**全是空白项**：`ItemsSource` 本身是好的（下拉里项数正确：语言 2 项、背景材质 5 项、切换动画 4 项、排序方式 8 项），坏的是 `DisplayMemberPath` —— 换成编译绑定后它没再为每个 `ComboBoxItem` 生成文本，行在、字全空。现在四处都改成显式 `ComboBox.ItemTemplate` + `x:DataType` + `{x:Bind}`（`vm:LanguageOption` / `vm:SortModeItem` / `vm:BackdropOption` / `vm:TransitionModeItem`），文本走编译绑定，裁剪/AOT 环境（`PublishTrimmed`）下也不会失效；顺带删掉排序方式那处 `ItemsSource` 路径里误插入的 Tab 字符
- 修复 Native AOT 版里「此电脑 / 网络 / 回收站 / Linux(WSL) / 云盘」图标退化成字体字形（以及连带的普通文件夹图标缺失）：这些位置是 `::{CLSID}` 形式的 Shell 命名空间，原来靠 `IShellItemImageFactory`（`SHCreateItemFromParsingName` + `out` 一个 COM 接口）取系统图标，而 AOT / 关闭 `BuiltInComInterop` 时这种 COM 接口封送不可用（构建期就有 `IL2050` 警告），拿不到图标就落回 `FontIcon`。`ShellIconHelper` 现在多了一条 **PIDL 路径**：`SHParseDisplayName` 把路径/CLSID 解析成 PIDL，再 `SHGetFileInfo(SHGFI_PIDL)` 取图标——全程只有指针和整数，AOT 下可用；查询顺序变成 COM 工厂 → PIDL → 字符串 `SHGetFileInfo`。同一原因也让普通文件夹在 AOT 下取不到图标（按字符串路径调 `SHGetFileInfo` 返回 0，而同一路径的 PIDL 版本正常），现在失败后会自动再用 PIDL 取一次。实测 AOT 产物里五个根节点与表格行都是 `icon=True`
- 修复 Native AOT 版里表格上方的搜索进度条常驻滚动：`MiddleFilesView` 那条 `IsIndeterminate` 的 `ProgressBar` 之前用 `{Binding IsSearching, Converter={StaticResource BoolToVisibilityConverter}}` 控制可见性，绑定一旦没生效就停在 `ProgressBar` 默认的 `Visible` 上一直转；现在改为代码后置直接跟随 `VM.IsSearching`（`SearchProgressBar.Visibility`），XAML 初始值也是 `Collapsed`，binding 失效也不会再常驻
- 给 `ExplorerTab` 补上 `[WinRT.GeneratedBindableCustomProperty]`：`TabView` 的标签页模板用 `{Binding Title}` / `{Binding FolderNode.Icon}` 绑这个类型，而 `{Binding}` 在 AOT / 关闭 `BuiltInComInterop` 的环境里必须靠源生成器提供 `ICustomProperty`
- 修复 `dotnet build` 编译失败：`LRSBreadcrumb.xaml.cs` 里 `nameof(LocalizationService.CurrentLanguage)` 所需的 `using FastFluentFilesFolders.Services;` 被"移除未使用 using"一起删掉，报 `CS0103: 当前上下文中不存在名称"LocalizationService"`；就这一个 C# 错误又让 XAML 编译器 Pass1 汇编编译失败，连锁出 `WMC0001 Unknown type 'ThemedIcon'`、`WMC0909 Cannot resolve DataType local:BreadcrumbSegment`、`WMC1111 DataTemplates containing x:Bind need a DataType` 等一堆假错误（看着像 x:Bind 写错了）。补回 using 后构建恢复干净；同时把该处改成不依赖 using 的 `nameof(App.LocalizationService.CurrentLanguage)`，以后再跑"移除未使用 using"也不会复发
- 修复 `ThemedIcon` 里 `new Path { … }` 在开启隐式 using 后与 `System.IO.Path` 撞名（`CS0104: "Path"是"Microsoft.UI.Xaml.Shapes.Path"和"System.IO.Path"之间的不明确的引用`），两处改为显式写 `Microsoft.UI.Xaml.Shapes.Path`
- 修复 `LRSBreadcrumb` 改成 `x:Bind` 后启动即崩（`XamlParseException: Cannot create instance of type 'FastFluentFilesFolders.UserControls.LRSBreadcrumb' [Line: 0 Position: 0]`，`crash.log` 里从 `App.OnLaunched` → `new MainWindowView()` → `MainWindowView.InitializeComponent()` 一路抛出、窗口根本没出来）。**真凶不是 x:Bind**：把 `LRSBreadcrumb.xaml` 整体回退到改动前的 `{Binding, ElementName=LRSB}` 版本重编重跑，`crash.log` 增长字节数与异常文本**完全一致**，x:Bind 版本与 Binding 版本崩在同一处。用 `AppDomain.CurrentDomain.FirstChanceException` 抓到被 WinRT 吞掉的真实异常链：`System.NotSupportedException: ICustomProperty support used by XAML binding for type 'MainWindowViewModel' (property 'SearchText') requires the type to marked with 'WinRT.GeneratedBindableCustomPropertyAttribute'`，抛出点是 `LRSBreadcrumb` 构造函数里的 `SearchTextBox.SetBinding(TextBox.TextProperty, new Binding{...})`。根因是 csproj 里**无条件**的 `<PublishAot>true</PublishAot>`：它让 `dotnet build -c Release` 就在 `runtimeconfig.json` 写进 `"System.Runtime.InteropServices.BuiltInComInterop.IsSupported": false`（连 AOT 都没做），于是 CsWinRT 关掉了反射式的 `ICustomProperty` 解析——XAML 里声明的 `{Binding}` 会静默失效（启动期实测 FileSystemNodeViewModel 374 次、FileGroupHeader 68 次、MainWindowViewModel 48 次 `NotSupportedException`，界面等于全空），而**代码里 `SetBinding` 这种同步解析会直接抛异常**，XAML 上层就报成"无法创建实体"。修复：`PublishAot` 改为只在 `EnableAotPublish=true` 时开启（注释写明 AOT 发布命令 `dotnet publish -c Release -r win-x64 /p:PublishAot=true`）；同时把 `LRSBreadcrumb` 里残留的两处运行时绑定彻底去掉（`SearchTextBox.Text` 改为 XAML `x:Bind ViewModel.SearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged`，`EnterEditMode` 里对 `PathTextBox` 的 `SetBinding` 改为直接赋 `Text = CurrentPath`，`CopyPathButton` 的 `{Binding CopyPathCommand, ElementName=LRSB}` 改为 `Click="OnCopyPathClick"`）；并为将来真要 AOT 发布做铺垫，给 5 个被 `{Binding}` 绑定的 POCO 加上 `[WinRT.GeneratedBindableCustomProperty]`（`FileSystemNodeViewModel`、`FileGroupHeader`、`MainWindowViewModel`、`MultiLanguageStringsViewModel`、`Configs`）。顺带验证了同一条链上更隐蔽的第三段症状：只加属性、AOT 开关仍开着时，界面能画出来但约 10 秒后崩在 `Microsoft.UI.Xaml.dll` 的 `MeasureOverride`（事件日志 `0xc000027b` + `COMException 0x80004005`），改回 `PublishAot=false` 后消失。实测（Release x64）：`runtimeconfig.json` 中不再有 `BuiltInComInterop` 开关、启动 30 秒 `crash.log` **零新增**、窗口标题正确显示 `FastFluentFilesFolders`、面包屑渲染出 `C: › Users › aaron › Downloads`、点击面包屑 "Users" 通过 x:Bind 的 `NavigateCommand` 成功导航、文件树正常列出目录
- 消除全部 **AOT 兼容性警告**（`MVVMTK0042`：*The field … using [ObservableProperty] will generate code that is not AOT compatible in WinRT scenarios …*）。`CommunityToolkit.Mvvm` 对「字段 + `[ObservableProperty]`」这种老写法生成的是普通 `public` 属性，CsWinRT 无法为它正确生成 WinRT 封送代码，在 WinUI 3 / UWP 场景下 AOT 不安全；改为 **partial 属性**写法（`[ObservableProperty] public partial T Xxx { get; set; } = …`），CsWinRT 才能生成正确的封送代码。按此规则把项目内**全部 88 个**私有字段式声明改成 partial 属性：`Configs`、`FileSystemNodeViewModel`、`MainWindowViewModel` 等文件此前已是新写法，本次补齐仍为旧写法的 **9 个文件**——`Models/ExplorerTab.cs`（`_title`/`_path`/`_folderNode` → `Title`/`Path`/`FolderNode`）、`ViewModels/TagViewModel.cs`（`_name`/`_glyph`）、`ViewModels/SettingsViewModel.cs`（5 个 `_selectedXxx`/`_newTimeGroupedFolderPath`，构造函数里对 backing field 的直接赋值同步改为对属性赋值）、`ViewModels/PropertiesSecurityViewModel.cs`（32 个）、`ViewModels/PropertiesCompatibilityViewModel.cs`（9 个）、`ViewModels/PropertiesGeneralViewModel.cs`（17 个）、`Services/LocalizationService.cs`（`_currentLanguage` → `CurrentLanguage`）。字段名去掉前导下划线并转成大驼峰，生成的属性名与原先完全一致，XAML 绑定、`OnXxxChanged` 分部方法、`OnPropertyChanged(nameof(...))` 均无需改动，行为零变化；`MainWindowViewModel` 里被注释掉的 `_PathsForBreadcrumbBar` 也一并改成新写法作为示例。验证：`dotnet build` 通过（**0 错误**），`MVVMTK` / AOT 相关警告数量为 **0**，剩余警告均为既有的 nullable 类（CS8602/CS8618 等），与本次改动无关
- 修复「淡入」模式进入分组目录时**整张表（含列头）先整块消失、随后才闪一下淡入**：原实现是把**整个 `TableView` 的 `Opacity` 归零**再做一次整表淡入，于是有两个必然的毛病 —— ①归零发生在内容还没挂进可视树的时候（`UpdateSourcePrebuilt` 打印 `mount prebuilt` 的那一刻），从换内容到新行实体化之间的整张表（含列头）都是空的；②分组目录那次淡入还被推迟到**低优先级回调**里才 `Begin()`（当初是为了躲开“容器实体化晚一帧”），空窗更长、随后才闪一下。日志实证（Fade 会话）：`22:51:36.553 mount prebuilt items=352 grouped=True` → 行在 `36.557~36.569` 才逐个实体化 → `36.572 window closed (layout)`，而整表从 `mount` 那一刻就已经被压成透明。现在「淡入」不再碰整表 `Opacity`，改为与另外两种模式**共用库的按行入场动画**（`AnimateContentReplacement=true`、`ContentReplacementSlideOffsetY=0`、`Stagger=0`、150ms，即“默认模式去掉上滑”）：行在容器实体化时（**渲染之前**）就被同步置为透明，本帧末由库统一开播 —— 不存在空窗，列头也不再参与淡入（与默认/溪流的表现一致）；`FadeInContent()` / `_fadeStoryboard` / `_fadeEnabled` 一并删除
- 修复**每次进入另一个目录或刷新都会自动丢失排序状态**：排序以前只藏在表格控件内部（`LrsTableView` 里 `GroupedFileList` 的 `_sortPath/_hasActiveSort` 与列头 `SortDirection`），而换目录/刷新走的是 `UpdateCurrentFolderContentAsync` —— 它在后台**新建**一个 `GroupedFileList`，条目顺序直接来自 `folder.Children`（磁盘枚举时按配置 `DefaultOrderMode` 排好的原始顺序），`SetItems` 只按传入顺序铺内容、**从不重新套用当前排序**（原地留着的 `_hasActiveSort` 又会让之后的增量插入按旧排序插错位置）。现在把排序提升为显式状态：`ExplorerTab.SortPath/SortAscending`（**每个标签页独立**）是唯一来源，`LrsTableView.SyncSortState` 负责同步列头箭头与数据源的活动排序；导航/刷新时在**后台**构建预构建源时就把排序套上（`g.SortWithinGroups`，UI 线程零额外开销），`UpdateSourcePrebuilt` 会比对预构建源实际生效的排序，只有不一致（竞态）才在 UI 线程补排一次；搜索结果与兜底整表刷新路径也带上当前标签页的排序重排。切换标签页时若两标签页排序不同，会强制重建一次内容（否则“同一文件夹”守卫会直接跳过、排序不生效）；列头三态循环第三次“取消排序”时改为让 VM 用原始子项顺序重建一次（`RequestContentRebuild`，不重新枚举磁盘），不再留下“顺序还是排序后的、箭头却没了”的不一致
- 修复进入分组文件夹后视图偶尔不在表格顶部：`UpdateSource/UpdateSourcePrebuilt` 挂载完成后新增低优先级 `ScheduleScrollToTop()`，等本轮内容替换与主动定位都完成后，若当前没有选中项则强制 `ChangeView` 归零（有“打开文件所在位置”等主动定位时自动跳过，避免把刚定位好的目标顶掉）；原有挂载前 `ResetScrollPosition` 保留作过渡防抖
- 修复“刚进入立刻排序，动画会被顶掉”：`ScheduleScrollToTop` 增加 `_contentGeneration` 版本号，用户开始排序时 `ApplyUserSort` 使版本号递增，挂载时排队的低优先级滚顶回调检测到版本变化即自动作废，不再打断正在进行的排序重排动画
- 接着上一条修“**位置对了但入场动画没了**”（只在分组目录出现；日志实证 `window closed (layout) token=8 started=21 scrolledToTop=True offset=1344`）：面板锚定后那一轮布局是在 **offset=1344** 处建的行，动画就播在这批行上；紧接着视图被滚回顶部，这批行滚出视野，而顶部那批行是之后才实体化的（窗口已关）→ 于是只看到跳变、看不到淡入。现改为**两阶段**：①第 1 轮布局结束（渲染之前）**只滚到顶**、放弃这批行的动画并把窗口续期（日志 `scrolled to top (layout) … offsetBefore=1344 - waiting for the next pass to animate`）；②第 2 轮布局把**顶部那批行**建好后才统一开播并关窗（日志 `window closed (layout) … started=N offset=0`）。非分组目录本来 `offset=0`，直接走第二阶段开播，行为不变
- 修复“**进入分组目录时列表位置不对、随后被拽回顶部**”（录屏逐帧实测：进入分组目录后内容先停在相对稳定位置 -41px 的地方、另一次还出现约 800ms 的来回位移）。根因是“原地换内容”时 `ItemsStackPanel` 会**锚定**原来的滚动偏移，而滚顶复位排在**低优先级回调**里 —— 那一帧早已渲染出去，于是用户看到“跳一下”，入场动画也播在了错误位置。现在改成：①`BeginContentReplacementAnimation()` 更名为 **`BeginContentReplacement()`**，窗口**与动画开关无关地**打开（淡入/关闭模式同样需要这次滚顶）；②布局结束时的收尾**先滚顶、再统一开播**，绝不让已经渲染出去的画面被事后纠正（有选中项时跳过，保住“打开文件所在位置”的定位；日志输出 `scrolledToTop=` / `offset=`）；③换内容后**立刻同步**再复位一次（此时旧滚动范围还在，`ChangeView` 同步生效），让随后那次布局从“已经在顶部”开始；④app 侧原来那个低优先级滚顶降级为**兜底**（`force:false`，只在偏移确实不为 0 时才动），不会再制造可见滚动
- 构建/验证流程修正：此前 `dotnet build`（未指定配置/平台）把产物写到别的目录，**用户实际运行的 `bin/x64/Release/net8.0-…` 一直是 10-11 的旧构建**（该版本里没有 `_displayedFolderIsSpecial`、日志也是旧格式 `row begin`），导致多轮“改了没效果”的误判；现在统一用 `dotnet build FastFluentFilesFolders/FastFluentFilesFolders.csproj -c Release -p:Platform=x64`（工程 TFM 已是 net10.0，产物在 `bin/x64/Release/net10.0-windows10.0.19041.0/`），并在日志首行加**构建指纹**（app/lib 版本 + 文件时间），一眼就能确认跑的是哪份产物
- 修复“**进入目录时额外抖一次**”（从普通目录切到分组目录时必现）：`IsCurrentFolderSpecial` 原本是实时由 `SelectedFolder` 推导的，而导航一开始 `SelectedFolder` 就换成了新目录、表格里还是旧目录的内容 —— `MiddleFilesView.UpdateGroupedSource` 于是读到「旧内容 + 新分组标志」这个不匹配组合（去重条件 `(items, special)` 随之失配），把**旧内容按新标志重新挂载了一遍**（还补上分组头），等真内容到达又挂第二次。录屏逐帧可见：t=1800ms 整块清空 → 1867ms 填回（能量 E 从 8.9 冲到 **10.3**，正是因为多出了分组头）→ **1967ms 又清空一次** → 2100ms 才稳定；同一次录屏里不含分组标志变化的那次导航只有一次清空。现在该标志改为**“已展示内容”**的显式字段 `_displayedFolderIsSpecial`，只在内容提交时更新，且必须**先**更新标志再换 `CurrentFolderContent`（顺序反了会在换内容那次通知里读到“新标志 + 旧内容”，同样多挂一次）
- 修复“**换成内容时抖好几次**”（分组大目录最明显）：上一条改成“行一建好就立刻开播”后，每行各按各的建好时刻起播，而面板是**一行一行**建出来的（日志实测 12 行的时间跨度 **326ms**，每行约 20~30ms，单元格模板是大头），于是一次入场动画被拆成十几个错开的小淡入。现在拆成**准备 + 统一开播**两段：行实体化时只 `PrepareEntranceAnimation`（同步落 `Opacity=0`、按错开量建好 `BeginTime`、不播），`TableView` 在**本轮布局结束时**（`LayoutUpdated`，且要求本轮至少实体化出一行，否则继续等下一轮）一次性 `StartPreparedEntranceAnimation` 把整批同时开播 —— 一批一次干净的淡入，之后滚动才出现的行不再补播；另带一个“窗口上限到点”的兜底，保证行不会停在“已准备未开播”的透明状态
- 修复「淡入」模式下分组文件夹没有进入动画：分组文件夹的容器实体化会晚一帧，整表淡入如果立刻开始，150ms 跑完了内容才显示出来、看起来像没动画。现在 `FadeInContent` 对分组文件夹先同步 `Opacity=0`，再排低优先级回调等本轮布局完成后再开始淡入；非分组文件夹保持同步开始
- 修复改地址栏后**整个界面起不来**（不是面包屑显示异常，而是窗口根本没创建）：`LRSBreadcrumb.xaml` 新增的两个尺寸资源写成了 `<x:Int32 x:Key="CtrlWidth">40</x:Int32>`，而 `FrameworkElement.Width/Height` 是 `double`，WinUI 在 `LoadComponent` 解析到 `Width="{StaticResource CtrlWidth}"` 时直接抛 `XamlParseException: Failed to assign to property 'Microsoft.UI.Xaml.FrameworkElement.Width' because the type 'Windows.Foundation.Int32' cannot be assigned to the type 'Windows.Foundation.Double'`（`crash.log` 里 `App.OnLaunched` 的 `new Views.MainWindowView()` 抛在 `MainWindowView.InitializeComponent()`，`_window` 从未被赋值、`Activate()` 也没执行，所以只剩一个没有任何窗口的进程）；注意这类类型不匹配 `dotnet build` 完全编译通过、只在运行时 XAML 解析阶段炸。改用 `x:Double`（与 `FileTreeView.xaml` 的 `TreeViewItemMinHeight`、`WinUI.TableView` 的尺寸资源写法一致）后实测：Release x64 启动出现主窗口、`crash.log` 无新增记录
- 修复上一条修复带来的**“每次进文件夹卡一下”**：`FinishContentReplacement` 里调用 `ForceContainerRebuild()` 是性能杀手——它断开再接回 `base.ItemsSource`，让 ListView 丢掉整套行容器与 `ItemsStackPanel` 的测量缓存后从零重建；而稳定数据源下 `SetItems` 刚把内容铺过一遍，等于**一次导航干两遍活**（实测每次导航会触发 2 次内容替换 → 2 次整面板重建）。改动前 `/` 后的 A/B 实测（自动在 `C:\Users\aaron` ↔ `Downloads` 间来回导航 8 次，统计“导航请求 → UI 线程把这次导航排的活全部跑完”，低优先级探针记录）：带重建 **435/516/517/610/932/955/1012/1065 ms（中位数 771ms）**，去掉重建 **139/156/163/175/240/323/327/368 ms（中位数 169ms）**，即**每次进文件夹省掉数百毫秒的 UI 线程占用、快约 4.5×**；同时同步段耗时几乎不变（17~90ms），说明那几百毫秒正是重建的 `await` 续体 + 面板重新实体化造成的。现在 `FinishContentReplacement` 只做“清选中项”这一件廉价的事，`ForceContainerRebuild()` 回归它本来的位置（仅真的换 `ItemsSource` 时），并实测确认原 bug 仍修复：从 Downloads 选中一行 → 向上返回 `aaron`（位置在顶部、无残留选中）→ 侧栏再进 Downloads（位置在顶部、无残留选中）
- 修复“进入（分组）文件夹后所选位置不在表格顶部、有些项莫名其妙被选中”这一类换内容后的残留状态。数据源改成**全生命周期唯一的稳定实例**后，换文件夹不再换 `ItemsSource`，于是过去靠“换源”顺带完成的清理全都失效了，现在在 `LrsTableView` 的整体替换路径上显式补回：①`PrepareForContentReplacement`（替换前）——`ResetScrollPosition()` 复位滚动偏移、`ResetInteractionStateForContentReplacement()`（`TableView` 新 API）取消正在进行的拖拽选择、清空按行索引记录的锚点（`SelectionStartRowIndex`/`SelectionStartCellSlot`/`_lastDragSelectionRowRange`/`CurrentCellSlot`）并清空选中项；②`FinishContentReplacement`（替换后）——再兜底清一次选中项
- `TableView` 延迟滚动到选中行时增加校验：`TableView_SelectionChanged` 里排队的 `ScrollRowIntoView` 现在会先确认选中项没变（换文件夹会清空选中项），避免按旧索引把新内容滚到中间那一行
- `TableViewRow.EnsureSelectionIndicatorPosition` 不再整体替换选中竖条的 `RenderTransform`：原来 new 一个 `TranslateTransform` 覆盖上去，会把模板里 `SelectionIndicatorScale` 的 `ScaleY=0` 基准值一起换掉，而“选中/未选中”视觉状态正是靠动画它的 `ScaleY` 显示/隐藏竖条——结果行只要经历过选中就再也藏不回去，容器回收给别的条目后仍显示竖条。现在改用模板自带的 `CompositeTransform`（`TableViewRow.xaml` 由 `ScaleTransform` 改为 `CompositeTransform`），`ScaleY` 按 `IsSelected` 显式赋值、`TranslateY` 负责行详情偏移，两者互不覆盖


## [1.1.1] - 2026-09-25

### Added

- 新增 `FastFluentFilesFolders/verify-release.ps1` 发布自检：拿 GitHub 上 latest release 的实际资产和本地构建结果逐项比对（appcast 是否与本地逐字节一致、线上 appcast 的 `.signature` 能否通过 Ed25519 校验、线上安装包大小是否等于本地、线上 appcast 里的 `length` 是否等于线上安装包），不一致时直接列出“需要重新上传”的文件清单并以退出码 1 结束；`build.ps1` 结尾也提示上传 6 个文件后跑一次
- `AutoUpdateService` 把 NetSparkle 的内部日志接入文件 `%LocalAppData%\FastFluentFilesFolders\update.log`（超过 512 KB 自动重建）：它默认只往控制台写，WinUI 程序里看不到，像 “Signature check of appcast failed” 这种关键原因以前根本查不出来
- 设置页“关于”分组下方新增**检查更新 SettingsCard**（同步图标 + 说明文字 + 强调色按钮），点击唤起自绘更新弹窗 `Views/UpdateDialog`：弹窗内按常见布局自上而下排“当前版本 / 最新版本 → 状态行（转圈 + 文案）→ 更新内容（可滚动的说明 + 完整说明链接 + 发布日期）→ 下载进度条”，主按钮随状态在“下载并安装 / 立即安装并重启 / 重试”之间切换，次按钮为“跳过此版本”，关闭按钮为“稍后”；检查中、下载中、安装中不允许关闭，下载中关闭弹窗会取消下载；全程中英文（新增 31 条字符串）

### Changed

- 滚动顺滑与内存的折中：实测 `ItemsStackPanel.CacheLength` 只要大于 0，WinUI 就会为屏外行创建缓存容器，且这些容器的原生 XAML 内存即使把 `base.ItemsSource` 断开再接回（强制重建 ListView 面板）也不回收——`CacheLength=2` 每导航 +29MB、`1` 每导航 +13MB、`0` 才走平。因此保持 `CacheLength=0`，改用“加载完成后异步预热前 160 条图标”来补滚动手感：图标在后台批量解码进缓存，滚动到新行时 `Icon` getter 直接同步命中，不再逐个弹出
- 子项实例化改为两阶段异步：目录枚举完成后，只对**新增/类型变化**的条目在后台按当前排序/分组/图标参数异步实例化（`BuildChildNodesAsync`），已存在的条目在 UI 线程原地更新元数据，既不重复建对象也不阻塞界面；离开目录时继续由 `ReleaseChildren()` 销毁该目录的子项数据（清空 `Children`、重置加载状态与子项计数缓存），目录树中已展开的节点仍保留以免树塌掉
- `AutoUpdateService` 与自绘弹窗对接（核心包 `NetSparkleUpdater.SparkleUpdater` 没有自带 UI，也无法用它的 UIFactory）：新增 `CheckForUpdatesAsync`（`CheckForUpdatesQuietly`，不弹任何自带窗口）、`DownloadUpdateAsync`（`InitAndBeginDownload`）、`InstallUpdateAsync`（`InstallUpdate`）、`SkipVersion`，并把 NetSparkle 的 `CloseApplication` 转发成 `CloseRequested` 事件——没有 UIFactory 时它不会自己关掉宿主，而安装脚本会一直等本进程退出（90 秒后放弃安装），所以弹窗收到事件后先隐藏自己再关闭主窗口；同时在 `MainWindowViewModel` 上暴露 `AutoUpdateService` 供设置页使用

### Fixed

- 修复分组文件夹出现**两个标签**：`LrsTableView.UpdateSourcePrebuilt` 把后台预构建数据源复制进稳定源时，连同已经生成的分组标题行（`FileGroupHeader`）一起交给了 `SetItems` 再次分组，于是“标题”又被当成条目生成了一层标题。现在复制时先过滤 `FileGroupHeader`，只对真实条目按当前参数重新分组；堆转储验证 Downloads（时间分组）只有 7 个 `FileGroupHeader`，不再翻倍
- 修复“反复刷新/进出文件夹工作集每次涨 5~35MB”的真正根因：`TableView` 的 `ItemsStackPanel` 默认 `CacheLength=4`（缓存 4 个视口的行），在 400+ 项的大文件夹上来回切换时每次会创建/销毁上百个 XAML 行容器，而 WinUI 的原生 XAML 内存不及时回收（堆转储显示托管存活对象稳定、但进程工作集每次导航涨约 30MB）。现在把 `CacheLength` 设为 `0`（只实体化当前视口，不再预缓存屏外行），并保留“离开文件夹释放子项 + 稳定数据源 + 换源强制重建容器”的组合；实测连续刷新 20 次 + 进出文件夹 20 次后 WS/Private 稳定在 ~304MB/~190MB，不再单调上涨。代价：快速滚动时屏外行不再预实体化，极端快速滚动可能有一帧空白，可按需调成 0.5/1
- 刷新恢复可见反馈：`RefreshCurrentFolderAsync` 在单飞（快速连点只跑一次）前提下始终重建一次表格，不再因为“内容未变化”而静默跳过；子项实例按路径复用 + `CacheLength=0`，重建式刷新也不会造成内存上涨
- 修复刷新内存上涨与“快速连点刷新瞬间涨 25~35MB”：①`RefreshCurrentFolderAsync` 改为单飞，连点只执行一次，不再并发枚举磁盘、各建一份完整节点集合与表格数据源；②刷新时按路径复用旧的 `FileSystemNodeViewModel` 实例并原地更新元数据，静态文件夹不再整批重建节点；③子项实例与顺序未变化时直接跳过整表重建（不新建 `GroupedFileList`/容器）。堆转储实测：200ms 内连点两次后总存活对象仅 +0.7k（约 0.04MB）；连续刷新 10 次仅 +3.2k（约 0.16MB），`TableViewRow`/`PropertyChangedEventHandler`/`FileSystemNodeViewModel` 全程稳定在 61/778/423
- 修复“刷新/进入新文件夹内存持续上涨”的根因：WinUI ListView 的 `ItemsStackPanel` 在 `TableView` 换数据源（自定义 `ICollectionView` 发 Reset）时不会真正回收上一批容器——堆转储实测每次换源都会多留下 2 个 `TableViewRow`（刷新 10 次 `_rows` 从 61 涨到 81），旧行继续持有单元格、绑定与数据项。现在换源后把 `base.ItemsSource` 断开再接回（仅在控件已加载时执行）强制 ListView 清空并重建面板，并用 `PruneStaleRows` 兜底移除不属于当前视图的残留行；反复刷新 10 次后 `TableViewRow`/`PropertyChangedEventHandler` 稳定在 61/778，不再随刷新增长
- 修复“进入新文件夹内存持续上涨”：`SelectedFolder` 改为集中式属性，离开旧文件夹时释放它的子项缓存（只针对普通物理文件夹与驱动器根，且未在目录树展开、非压缩包内部），覆盖表格双击/目录树/固定栏/地址栏/后退/前进/向上等全部导航入口；切换标签页期间抑制释放，避免影响其它标签页；`ReleaseChildren` 释放后补回占位项，保证目录树仍有展开箭头、展开/回访时能重新枚举
- 修复文件表格与标签页 DataTemplate 仍使用 `x:Bind` 导致的内存泄漏：`MiddleFilesView.xaml` 行模板的名称/日期/大小/原位置等 20 处绑定与 `ExplorerPage.xaml` 的标签页模板全部改用 `{Binding}`，避免 TableView 回收复用行容器时编译绑定继续持有旧的 `FileSystemNodeViewModel`，反复刷新/进入文件夹后内存持续上涨
- 修复 `WinUI.TableView` 的 `CollectionView` 在 Reset 时不解绑 `PropertyChanged` 的内存泄漏：`ObservableCollection.Clear()` 触发的 Reset 事件不带 `OldItems`，旧条目的订阅无法移除；现在用引用比较的跟踪集合记录已附加条目、Reset 时整体解绑，并让 Attach/Detach 幂等，避免 Reset/Replace 时对未变化条目叠加多份订阅
- 记录第二次“检查更新失败”的原因：重新执行 `build.ps1` 会重新编译安装包（体积/签名都变），于是 appcast 也跟着重生成、重新签名——这时候**只补传 `.signature` 是不够的**，线上仍是旧 appcast（`length=49105205`）配新签名（对应 `length=49101589`），依旧校验失败。正确做法是 appcast + `.signature` + 安装包 6 个文件一起传
- 修复**上传了 appcast 之后“检查更新”仍然失败**：`build.ps1` 在 AppCast 生成（生成器同时完成签名）**之后**又用 `Set-Content` 把 enclosure 的下载地址改写成 gh-proxy 前缀，文件内容变了却没有重签，客户端 `SecurityMode.Strict` 于是判定 `Signature check of appcast failed` 并返回 `CouldNotDetermine`（弹窗里就显示“检查更新失败”）。现在把代理前缀直接放进生成器的 `-u`（先写进文件再签名，签的就是最终内容），删除事后改写步骤，并新增生成后的 `--verify` 自检（签名不通过就 `exit 1`，避免再误传坏文件）。用发布器自带 `--verify` 和客户端同款 `Ed25519Checker` 交叉验证：v1.1.1 线上那两份签名均为 Invalid，去掉代理前缀的原文为 Valid（根因确认）
- 重新签发了 v1.1.1 的两份 AppCast 签名（appcast 文件本身与线上逐字节一致，只需重新上传 `.signature`）：`AppCast/x64/appcast-x64.xml.signature`、`AppCast/arm64/appcast-arm64.xml.signature`。实测整条流程恢复正常：检查更新 → 1.1.1 可用 → 经 gh-proxy 下载 49,105,205 字节安装包 → 安装包 Ed25519 签名校验通过
- `AutoUpdateService` 把 NetSparkle 的“已安装版本”由 exe 的 `ProductVersion` 改为 `Configs.Version`（自定义 `IAssemblyAccessor` 包装 `AssemblyDiagnosticsAccessor`，配置仍存同一个注册表键）：本工程没有在 csproj 里写 `<Version>`，默认会读到 `1.0.0+commitHash`，不但和“关于”页显示的版本号对不上，更新成功后也仍会被判定为“有新版本”
- 实测检查更新链路：`AutoUpdateService` 配置的 `appcast-{arch}.xml` 在当前 latest 发布（v1.1.0）里并不存在（那个发布只有 `appcast.xml`），所以现在点“检查更新”会走到“检查更新失败”分支（弹窗提供重试）；把 URL 换成已发布的 `appcast.xml` 后整条链路可用（Ed25519 签名校验通过、返回 `UpdateAvailable`、`Updates[0]` 即最新版本）。下一次用 `build.ps1` 发布（会上传 `appcast-x64.xml` / `appcast-arm64.xml`）后即恢复正常，无需改代码
- 修复**安装到 Program Files 后应用无法启动**：安装器新增 `[InstallDelete]` 段，在复制文件前清理历史遗留的 MSIX/AppX 打包产物与旧构建残留（`resources.pri`、`App.xbf`、`AppxManifest.xml`、`*.build.appxrecipe`、`AppX\`、`Views\*.xbf`、`UserControls\*.xbf`、`publish\`、`runtimes\` 以及 arm64 运行时文件）。这些文件不在发布输出里，`[Files]` 永远不会覆盖它们，但 WinUI 3 的 XAML 资源加载器会读到它们，导致新版程序集加载旧版 XAML，启动即抛 `XamlParseException: Cannot create instance of type 'FastFluentFilesFolders.Views.MiddleFilesView'`（实测：在安装目录模拟 11 个旧残留后覆盖安装，全部被清除，应用正常启动）
- 修复 `build.ps1` 生成 AppCast 后重命名失败：`Rename-Item -Force` 在目标文件已存在时仍抛 IOException，导致 `appcast-x64.xml` / `appcast-arm64.xml` 一直是上一次的旧文件（与最新安装包不匹配），改用 `Move-Item -Force` 覆盖


## [1.1.0] - 2026-09-24

### Added

- 右键菜单与工具栏里的字形图标（FontIcon）全部换成 ThemedIcon 矢量图标：打开/打开方式/还原、固定与取消固定、打开文件所在位置、属性、显示更多选项、彻底删除、新建子菜单（文本文档/快捷方式/Excel/Word/PowerPoint）、新建文件夹、粘贴、清空回收站，以及地址栏（后退/前进/向上/刷新/主页/搜索/复制路径/展开箭头）和工具栏下拉箭头；图标几何数据由 Segoe Fluent Icons 字形轮廓矢量化而来（16×16 设计栅格，线稿层 + 内部色域层），外观与系统字形一致但不再依赖字体渲染
- ThemedIcon 升级：线稿层跟随 `Foreground`（支持红色“彻底删除”、操作岛状态色与主题切换实时重绘），新增 `AccentBrush`（强调色图层画刷，默认系统强调色）、`Mono`（单色模式，整体跟随前景色）、`Glyph`（按 Segoe 字形码位自动取图标，未收录字形自动回退为 FontIcon）；新增 `IconLibrary` 统一管理字形→图标映射与几何解析
- `ThemedIconResources.xaml` 拆分为“几何数据（`IconData.*`，供 PathIcon / 动态字形复用）+ 图标样式（`Icon.*`）”两部分，新增 28 个菜单/工具栏图标样式
- 剪切遇到同名项时弹一次确认框（替换 / 跳过 / 保留两者），选择应用于本次全部冲突；同批次内部重名强制“保留两者”；复制仍自动改名且避免批内重名
- 移动引擎增强：跨盘移动改为“复制成功后删源”；同名目录“替换”执行递归合并；移动/复制到自身或子目录会明确报错
- 回收站专属工具栏（还原 / 彻底删除 / 清空回收站 / 排序），进入回收站自动切换、离开后恢复通用工具栏；无选中项时还原/彻底删除置灰，回收站为空时清空置灰
- 属性弹窗“安全”页新增“更改所有者”能力（对齐资源管理器，所有文件/文件夹类型均显示）：在权限卡片下方新增“所有者”卡片，显示当前所有者并提供“更改”按钮；点击后弹出内置“选择用户或组”对话框（可手动输入账户名，或从常用主体列表中选择），点击窗口底部“应用/确定”时与权限一起写入。写入前自动启用 SeTakeOwnership/SeRestore 特权，受保护系统资源仍会弹出确认警告，全部中英文
- 文件表格新增/删除条目动画：新增条目淡入，删除条目后下方条目自动上移补位（“默认/淡入”动画模式生效，“关闭”模式保持无动画）
- 左侧目录树新增 Windows 侧栏位置：此电脑（所有磁盘入口，U盘/光盘插入或拔出实时增删，磁盘显示“卷标 (X:)”样式并本地化）、网络、Linux（WSL，自动使用 \\wsl$ 或 \\wsl.localhost 并列出发行版）、回收站、云盘（自动检测 OneDrive(个人/企业)/Dropbox/Google Drive/iCloud Drive/坚果云/MEGA/百度网盘，检测不到的自动隐藏）
- 回收站支持在应用内浏览与完整操作：右键条目可“还原/彻底删除”，空白处右键可“清空回收站”，列表增加“原位置”列；还原/彻底删除经 Shell.Application 动词执行，清空回收站经 SHEmptyRecycleBin 执行（均带确认框，中英文）
- 文件夹支持“固定/取消固定”（固定到 Windows 系统快速访问）：文件夹右键菜单按当前状态显示「固定到“快速访问”」或「从“快速访问”取消固定」（仅文件夹行显示），并支持快捷键 Ctrl+P 一键切换；切换后立即刷新左侧“已固定”栏，资源管理器/重开应用均可看到最新固定项（未打包模式实测可用）

### Changed

- 右键菜单**二级列表项**改用自定义菜单项模板（`ThemedMenuItemStyle`），从而在列表里也能渲染两色 ThemedIcon：WinUI 给 overflow 项用的默认模板里没有 `ContentPresenter`（红色方块探针实测：默认模板 0 像素、自定义模板 784 像素），所以直接放 `Content` 不会显示；新模板含图标位（Content）/标题/快捷键提示/子菜单箭头，另补 `AutomationProperties.Name` 供读屏；菜单项由 `ApplyMenuItemStyle()` 统一套用
- 二级列表项回归“真正的 ThemedIcon”：`CommandBarFlyout` 的二级命令在 Popup 里**不会触发 `Loaded`**，ThemedIcon 若等 Loaded 再建层就是空白图标；现在 ThemedIcon 在数据/画刷变化时**立即建层**（不再依赖 `Loaded`），菜单项统一由 `MenuIcon()` 工厂创建（显式 16×16 + `EnsureBuilt()`，Popup 里才能排出图标位）：打开/打开方式/复制路径/固定/打开文件所在位置/属性/显示更多选项/新建/彻底删除/插件项；只有 `MenuFlyoutItem`（子菜单，只能放 IconElement）保留 `Icon` + `PathIcon`
- 插件右键菜单项与字体图标位置（`MenuFlyoutItem.Icon`）改用 `PathIcon` 渲染同一份矢量数据（跟随菜单项前景色与禁用态），插件未收录的字形仍回退 FontIcon；未打包/打包模式均适用
- 侧栏目录树每一项高度由 28 增高至 40，图标与文字更舒展
- 在“此电脑/回收站/网络/WSL”等虚拟位置下双击进入文件夹后，左侧目录树自动失焦（键盘焦点移回文件表格），方向键与快捷键不再误作用到目录树
- 安全页权限/所有者写入不再因缺少管理员权限而直接报错：进程内写入失败时自动通过 UAC 向用户请求管理员授权，并以提升的 PowerShell 进程重试应用变更；用户取消授权时给出提示，不再要求手动以管理员身份重启应用（中英文）
- 驱动器不再平铺为树根，统一收纳进“此电脑”节点；地址/面包屑/标签标题适配侧栏虚拟位置（CLSID 显示友好名），驱动器根的“向上”回到“此电脑”，WSL 发行版根的“向上”回到 Linux
- 回收站/此电脑/网络等特殊节点图标改为直接解析系统 Shell 命名空间图标（图标缓存区分 CLSID），无图标时回退 Segoe 字形，不再误用通用文件夹图标
- 修复回收站/此电脑等虚拟位置禁止粘贴/新建等文件写操作（原来会因路径不存在而报错）

### Fixed

- 修复二级列表无法横向撑满右键菜单：WinUI 的 overflow 列表宽度由“最宽的那个项”决定，而菜单宽度往往由一级命令栏撑开（实测列表 160 DIP vs 菜单 388 DIP，右侧留一条空档）；现在菜单弹出后按**一级命令栏的实际宽度**把二级项撑满（`flyout.Opened` → 低优先级回调 → 设置 `MinWidth`，只在存在一级命令栏时执行、且只增不减，避免“列表自己撑自己”的宽度反馈），实测条目菜单项 760px、菜单 816px（撑满），空白处菜单仍保持按内容自适应（项 320px / 菜单 376px）
- 修复自定义菜单项模板的两个问题：①“每行只显示一个字”——overflow 会按内容宽度排列菜单项，模板里标签列是 `*` 时会被压到一个字宽，改为 `HorizontalAlignment=Stretch` + `MinWidth=160`（实测菜单项宽度 320px、标签 164/208px 完整显示）；②悬停/按下没有过渡动画——改为高亮层 Opacity 动画（悬停 0.12s 淡入、按下 0.06s、离开 0.15s 淡出，配合 SubtleFillColorSecondary/Tertiary 底色），并保留禁用态 0.4 透明度与系统键盘焦点框
- 修复 `Glyph` 字形查表失效（表现为图标静默退化成字体图标、没有强调色）：`ResourceDictionary.TryGetValue` 不会下钻 `MergedDictionaries`，而 `IconData.*` 在合并进来的资源字典里；改为递归查找合并字典，并新增“字形未收录/回退字体图标”日志（`%LocalAppData%\FastFluentFilesFolders\icon.log`）便于排查
- 修复图标几何数据解析后复用同一 `Geometry` 实例导致的 `ArgumentException（Value does not fall within the expected range）`：WinUI 中同一个 Geometry 不能同时挂到多个 `Path`/`PathIcon` 上，改为每层新建；图层应用失败只跳过该层并写调试输出，不再让整个界面出错（此前该异常会让工具栏“新建/排序”两个按钮整块消失、右键菜单“以列表排列的选项”全部没有图标——同一份几何数据第二次挂到 `Path` 上就会抛异常，被全局处理器吞掉）
- 删除/彻底删除与操作岛状态一致：改为按真实磁盘结果逐项推进（成功才移除行，失败保留并显示具体原因）；`SHFileOperation` 返回值/用户取消不再被忽略；Ctrl+Delete、右键、工具栏入口行为统一
- 修复剪切粘贴的 UI/磁盘不一致：先移动成功再移除源视图，逐项容错（单项失败不中断整批），失败项保留待剪切标记可重试
- 修复幽灵条目：剪切/删除成功后按路径清理所有已加载目录缓存；右键“粘贴到子文件夹”只更新目标目录缓存，不再把嵌套项塞进父目录
- 搜索视图/回收站/此电脑/网络/压缩包内部等位置禁用粘贴、剪切、删除、重命名、新建；退出这些位置后自动恢复
- 回收站操作接入操作岛：还原/彻底删除逐项建卡并校验真实结果；清空回收站建卡，`SHEmptyRecycleBin` 失败时回退逐项物理删除；还原动词改用 `FolderItemVerb.DoIt()` 并在 STA 线程执行（本地化系统上 `InvokeVerb` 会静默无效）；彻底删除直接删 `$R` + `$I`，避免系统二次确认
- 驱动器枚举全部移到后台线程（UI 线程不再做 `DriveInfo` I/O），单盘探测 2 秒超时并降级显示“类型名 (X:)”，刷新单飞防止卡死任务堆积；修复驱动器枚举失败时“失败→重试→再失败”的无限重试环；部分枚举成功时保留已收集条目；枚举异常/部分成功/超慢写入 `enum.log`
- 驱动器与侧栏虚拟节点不再套用隐藏/系统属性（`C:\` 自带 System+Hidden，导致 C 盘显示半透明）
- 底部“共 N 项”在导航、粘贴、删除、剪切、新建后都会刷新；目录树子项数量文本在增删后同步更新
- 文件操作改为**增量更新**：删除/粘贴后直接移除/插入条目，不再整表刷新（修复操作后列表跳回顶部）；排序状态下新项按当前排序插入正确位置；增量更新异常时自动回退整表刷新兜底
- 节点元数据加载统一回到 UI 线程赋值，并调整“先补齐分组元数据、再启动异步加载”的顺序，避免加入分组视图时的时序竞争
- 修复侧栏“此电脑/网络/回收站”图标不显示（一直回退 FontIcon 字形）的问题：SHGetFileInfo 无法解析“::{CLSID}”虚拟路径（实测返回 0），改为经 IShellItemImageFactory.GetImage 直接取真实系统命名空间图标
- 侧栏“Linux（WSL）”改用 Windows“Linux”命名空间图标（::{B2B4A4D1-2754-4140-A2EB-9A76D9D7CDC6}），“云盘”分组及云盘目录改用 Shell 项接口取云盘提供商真实图标（OneDrive 等）而非通用文件夹/Segoe 字形
- 修复“此电脑”不显示磁盘的问题：构造函数中过早选中“此电脑”，此时 App.SharedViewModel 尚未赋值，磁盘快照为空却被标记为已加载，之后一直为空；改为等初始化完成（DeferredInitializeAsync）后再选中，并在磁盘提供者未就绪时保持未加载状态以便重试
- 修复“Linux（WSL）”不显示发行版的问题：原实现要求 \\wsl$ / \\wsl.localhost 根路径可被 Directory.Exists 访问，实测两者均返回 false 导致永远枚举不到；改为像资源管理器一样经 Shell“Linux”命名空间（::{B2B4A4D1-…}）枚举发行版，并从中推导可用的 UNC 前缀
- 修复网络/WSL 等 UNC 根节点名称为空（如 \\wsl.localhost\Ubuntu 显示为空白行）的问题：UNC 的“服务器\共享”被 Path.GetFileName 视为根而返回空，改为回退到手动取最后一段
- 修复回收站“原位置”列只有第一行（随机一行）显示、其余行缺失的问题：TableView 复用的行容器在可见列集合变化后未重建单元格，导致新显示的列只落在首个已实体化的行上；现于行内容复用时校验并重建单元格（WinUI.TableView）
- 修复提升进程仍无法更改所有者的问题：提权的 PowerShell 在写入前显式启用 SeTakeOwnership/SeRestore 特权（管理员令牌默认未启用），失败时把真实异常写入临时文件并回传到错误提示，便于定位
- 修复面包屑对 UNC 路径（\\wsl$\...）分段错误导致 WSL 内导航显示错乱的问题；侧栏根只显示一段友好名称
- 修复英文语言包 JSON 中未转义引号导致后续键读不到的问题


## [1.0.2] - 2026-08-30

### Added

- 属性弹窗“常规”页新增“正在使用的进程”项
- 修改受保护系统资源（系统目录或属主为 TrustedInstaller/SYSTEM）权限前弹出警告确认框（继续/取消，中英文）
- 多标签页支持：顶部标签栏（TabView），Ctrl+T 新建标签页、Ctrl+W 关闭标签页、标签页可拖拽排序，每个标签页独立维护浏览历史（前进/后退）与搜索状态
- 切换标签页时目录树自动高亮对应文件夹
- 新增「新建标签页 (Ctrl+T)」「关闭标签页 (Ctrl+W)」提示文案（中/英）
- 添加“系统背景材质”设置项
- 添加“计算文件夹大小”功能
- 搜索结果在文件表格中展示（新增“位置”列，搜索模式下只读）
- “复制文件地址”功能：新建菜单新增“复制文件地址”项，文件右键“复制路径”同步样式（ThemedIcon 复制图标，右对齐灰色 Ctrl+Shift+C 提示），支持快捷键 Ctrl+Shift+C 复制选中项完整路径

### Changed

- 属性弹窗打开文件夹时自动触发后台计算文件夹大小，“大小”用逻辑文件大小，“占用空间”单独计算各文件磁盘分配（GetCompressedFileSize）总值，二者不再相等：文件使用进程异步懒加载显示（无则显示 “-”），文件夹不显示
- 属性弹窗改为 XAML 实现的 WinUI 子窗口：现代化标题栏（无标题文本，仅系统最小化/最大化/关闭），SelectorBar 选项卡涵盖常规、数字签名、安全、详细信息、以前的版本；.exe 额外显示兼容性选项卡，可编辑兼容模式/管理员运行/256 色/640x480/禁用全屏优化/高 DPI 缩放
- 按 Windows 文件资源管理器补齐属性弹窗功能：常规页支持可编辑文件名、描述、占用空间、高级属性（存档/索引）；数字签名分嵌入签名/目录签名并带详细信息；安全页展示对象名、组或用户名与权限（允许/拒绝）；兼容性页补充兼容疑难解答、简化的颜色模式、注册程序重启、旧版 ICC、更改高 DPI、更改所有用户设置；以前的版本页支持查询并带打开/还原按钮
- 隐藏文件与系统文件改为半透明显示（不透明度 50%）：文件表格的名称/修改/创建/大小列与左侧目录树统一淡化，剪切（40%）状态表现保持不变；属性在目录枚举时一次性读取，独立节点、搜索结果与刷新路径同步生效
- 属性窗口按 content-basics 优化视觉效果：统一 24px 页边距与 12px 间距节奏，常规/数字签名/详细信息等内容改为卡片式表面（LayerFill 背景 + CardStroke 描边 + 8px 圆角），章节标题统一层级与下边距，次级说明改用小号次级前景色文本
- 属性窗口“安全”选项卡参照 Files 重构：组或用户名卡片列表 + 所选主体权限卡片，允许/拒绝复选框矩阵（完全控制/修改/读取和执行/列出文件夹目录/读取/写入/特殊权限），继承项置灰并提示；新增“高级(V)”按钮打开高级安全设置对话框（所有者/组 + 每条 ACE 的类型/权限/继承范围）
- “安全”选项卡支持直接编辑权限：允许/拒绝复选框可勾选（完全继承项置灰），点击“应用/确定”时按位重建该主体的显式 ACE 写回磁盘（保留原有继承范围，新 ACE 默认“此文件夹、子文件夹和文件”），失败弹窗提示；特殊权限仍为只读展示，完整粒度编辑需系统高级安全设置
- 属性窗口“详细信息”页改为纯 XAML 布局（PropertiesDetailsView）：使用 TableView 以“属性 | 值”两列表格呈现（表头、列、行高、卡片外壳均在 XAML 定义），仅数据读取与本地化在 C# 中完成；新增中英文表头文案
- 属性窗口其余选项卡全部改为 XAML UserControl：常规（PropertiesGeneralView）、数字签名（PropertiesSignatureView）、安全（PropertiesSecurityView，含权限矩阵与高级对话框）、兼容性（PropertiesCompatibilityView）、以前的版本（PropertiesPreviousVersionsView）；窗口外壳精简为仅标题栏/选项卡/按钮与页面分发，各页界面在 XAML 中定义，仅数据读写、文件系统/注册表/ACL 操作与本地化在 C#；新增签名列表列头与“详细信息”按钮中英文文案
- 属性窗口改为 MVVM：会话选项卡创建独立 ViewModel（PropertiesGeneral/Signature/Security/Details/Compatibility/PreviousVersionsViewModel），所有数据读取（文件系统/签名/ACL/注册表/卷影副本）、写回逻辑与本地化文本下沉到 ViewModel；各视图 XAML 通过 Binding/Command 绑定 ViewModel，代码隐藏仅保留 DataContext 接线与少数 ContentDialog（纯 UI）；文件/ACL/注册表等操作不再直接在 Views 中完成
- 属性窗口“兼容性”页设置改用 SettingsCard/SettingsExpander（CommunityToolkit.WinUI.Controls）呈现：兼容模式与简化颜色改为“开关 + 展开选项”卡片，管理员运行/640x480/禁用全屏优化/注册重启/旧版ICC 等为独立开关卡片，高DPI/所有用户为按钮卡片；交互与注册表写回逻辑保持不变
- 优化面包屑/后退/前进/地址栏导航：优先快速路径查找目标节点，避免对整棵已加载目录树递归搜索；独立路径节点改为懒加载，进入新文件夹只枚举一次目录
- 重复进入已在展示的文件夹时跳过表格重建；首次进入大文件夹时子节点构造移至后台线程，减少 UI 线程卡顿
- 图标并发加载默认不限制（0 = 不限），设置页允许填 0；并发量不大，不会明显占用性能
- 固定栏切换文件夹提速：点击固定项走“当前文件夹→直接子项→同目录（父目录子项）→祖先链”快速路径复用已加载节点，不做全树递归；同目录内切换两个文件夹与从父目录直接进入同等速度
- 表格加载改为“整表一次性替换”：导航时不再 Clear 后逐条 Add（分组模式 O(N²) 插入、先空后逐条出现），而是构建新集合一次赋值 + ItemsSource 断开/接回触发单次整表刷新，双击与其它方式进入文件夹均无替换痕迹
- 目录树节点新增父引用，后退/向上/面包屑上级导航沿祖先链 O(深度) 查找，避免全树递归造成的渲染前卡顿
- 优化启动速度
- 优化内存占用
- 更改文件操作岛样式
- 搜索改为表格(TableView)显示结果：搜索框内联至地址栏（Esc 恢复），新增「位置」列（仅搜索时显示）

### Fixed

- 修复压缩/解压插件中文文件名乱码：ZIP 读取/解压先按默认编码探测，出现替换字符时自动回退到系统 ANSI 代码页（GBK 等）；7z 列表/压缩/解压统一 `-scsUTF-8` 强制 UTF-8 输出，7z 生成 ZIP 时加 `-mcu=on` 保证中文条目名 UTF-8 编码
- 修复粘贴到文件夹时的严重 bug：右键文件夹→粘贴 现在会把文件粘贴进被右键的文件夹（与资源管理器一致），而不再落到当前文件夹
- 地址栏支持相对路径（如 `..\新建文件夹`）：以当前文件夹为基准解析成绝对路径后再导航，粘贴等文件操作不再因相对路径导致复制失败
- 粘贴目标路径强制归一化为绝对路径并校验目标文件夹存在，目标无效时在操作岛给出明确错误提示
- 文件操作岛失败卡片现在会显示具体错误信息：新增 `ErrorMessage`，失败时在卡片下方以红色文字展示真实原因，不再只有被隐藏的进度行
- 剪贴板读取过滤空/无效路径，避免单个无效项导致整批粘贴失败
- 修复数字签名“时间戳/摘要”读不到的问题：从 PE 文件证书表提取内嵌 PKCS#7，用 SignedCms 解析签名者的真实摘要算法（SHA256 等）与签名时间戳（兼容旧式计数器签名与 RFC3161）；新增 System.Security.Cryptography.Pkcs 包引用与“摘要算法/签名时间戳”详情文案
- 修复未打包运行时应用图标丢失：csproj 增加 ApplicationIcon（并生成合法 Assets\app.ico，原 Logo.ico 实为 PNG 改名非法），窗口创建时经 AppWindow.SetIcon 显式设置图标（打包/未打包均生效）；兼容性设置未打包版写真实注册表可被资源管理器读取，打包版仍受 MSIX 容器限制
- 修复修改受保护系统文件（如 TrustedInstaller 独占权限）权限时的报错：写回 ACL 由 AccessControlSections.All（含审核 SACL，需 SeSecurityPrivilege）改为仅 AccessControlSections.Access（仅 DACL），普通用户不再因缺少 SeSecurityPrivilege 报错，管理员也不再出现“未授权操作”；
- 修复兼容性页下拉框折叠时误显示类型名“KeyValueOption”：WinUI 3 ComboBox 选择框不应用 ItemTemplate 时回退 ToString()，为选项模型重写 ToString() 返回显示文本
- 修复兼容性设置写入后系统资源管理器看不到的问题：读写 AppCompatFlags\Layers 时显式使用 64 位注册表视图（RegistryView.Registry64），避免 32 位进程写入被 WOW64 重定向到 HKCU\Software\WOW6432Node 而 64 位资源管理器读不到
- 兼容性设置写入进一步加固：同时写入 64/32 位注册表视图，读取时 64 位优先并回退 32 位；写入后校验回读，失败不再静默吞掉而是弹具体错误；修正“注册此程序以重新启动”的标记（REGISTERINGAPPS → REGISTERAPPRESTART，兼容旧标记读取）
- 修复 MSIX 打包运行时兼容性设置写入“假成功”：打包应用的 HKCU\Software 写入被重定向到包私有容器，资源管理器读真实注册表看不到；已确认打包桌面应用无法从容器内写系统共享注册表（连经 ShellExecute 启动的 reg.exe 也会被重定向）；改为检测打包模式并在应用时弹出明确警告（需改用「未打包」配置运行才能被资源管理器读取），未打包模式走 RegistryKey 双视图写真实注册表；新增 compat.log 调试日志
- 修复“复制文件地址”菜单项被错误放置在“新建”子菜单中的问题：从空白处右键“新建”子菜单与工具栏“新建”下拉菜单中移除（文件右键已有“复制路径”提供同一功能）
- 修复“非双击进入文件夹”的小卡顿：移除目录树单击导航的人为 50ms 延迟，与双击文件夹走同一套即时导航流程
- 切换标签页时目录树程序化高亮不再触发二次导航（此前会重复加载并污染标签页历史栈）
- 图标并发上限接入配置项 IconParallelLoadingCount（此前配置 30 未生效，实际硬编码为 CPU 核数一半，导致图标逐项加载、进入文件夹像“轮流替换”）
- 修复固定栏（PinnedShortcuts）非特殊文件夹图标丢失：Shell/GDI+ 并发解码过高会偶发失败，且固定栏行只实体化一次不重读 Icon；改为自动安全并发上限 16 + 失败图标有界自动重试（0 = 自动）
- 图标加载请求也改为批量合并：此前每行 Icon getter 逐行入队 LoadIconAsync，与赋值排空交错导致热缓存时图标分多批“滚动替换”；现在一次性处理一批请求，缓存命中时图标与行同时铺满
- 图标完成赋值改为批量合并（每 UI 调度周期统一应用一次）：此前每个图标完成都单独入队 UI 线程，解码完成时间不同导致图标跨帧逐行出现（“一行一行出来”）
- 图标赋值改为“防抖合并”：最后一批图标完成后才统一排空一次，彻底避免图标分多批跨帧弹出残留的逐行痕迹
- 修复“从非分组文件夹双击进入分组文件夹后退回时，原文件夹显示为空”的问题：释放独立文件夹子项缓存时同时重置加载状态，退回时重新枚举磁盘
- 修复部分文件夹图标偶发缺失：图标解码失败时重置请求标记，下次访问自动重试；批量赋值增加单点异常保护
- 刷新按钮单独执行磁盘重载；点击面包屑当前段/地址栏重回车不再触发整目录重载
- 修复部分翻译问题

[Unreleased]: https://github.com/Xhscfdj/FastFluentFilesFolders/compare/v1.1.1...HEAD
[1.1.1]: https://github.com/Xhscfdj/FastFluentFilesFolders/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/Xhscfdj/FastFluentFilesFolders/compare/v1.0.2...v1.1.0
[1.0.2]: https://github.com/Xhscfdj/FastFluentFilesFolders/releases/tag/v1.0.2

