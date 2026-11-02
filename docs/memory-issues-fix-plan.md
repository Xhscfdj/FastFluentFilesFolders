# 内存问题修复计划

> 针对用户报告的三个内存现象：① 两目录来回切换上涨（与项数正相关）；② 反复切语言每次约 +20MB；③ 浏览页→设置页每次约 +7MB。
> 本文只做诊断与计划，不含代码改动。按“问题 2 → 问题 3 → 问题 1”的顺序执行，每步做完单独验证并记 CHANGELOG。

---

## 0. 总体判断

| 现象 | 置信度 | 性质 | 一句话根因 |
|---|---|---|---|
| ② 切语言 +20MB | 高 | **分配风暴**（非逻辑泄漏） | 一次语言切换把 `PropertyChanged` 广播 150 次，`MiddleFilesView` 对每次通知都**全量重建工具栏 + 5 个右键菜单**，单次切换实际创建数千个 XAML 元素 |
| ③ 进设置页 +7MB | 高 | 反复新建整页 | `NavigationCacheMode=Disabled` + `ContentFrame.Navigate` 每次进入都新建整个设置页（20+ SettingsCard/Expander、4 个 ComboBox、2 个 ListView），旧页回收滞后 |
| ① 切目录上涨 | 中高 | **重复分配 + 回收滞后**（大概率不是逻辑泄漏） | 离开目录立即 `ReleaseChildren()`，回来必重建 N 个节点；每次导航又做两遍分组构建与整批 `Clear()+Add`，丢弃全部行容器 |

三者都不是“一处静态引用钉住整棵树”那种经典泄漏（代码里已经做过大量退订工作，见 `SettingsViewModel.Unsubscribe`、`MiddleFilesView.OnUnloaded`、`LrsTableView` 的稳定 ItemsSource）。所以修复方向是**降低每次操作的分配量 + 消除重复构建**，而不是逐个找引用链。

---

## 1. 取证（探针已移除，本节保留作复用参考）

> **状态：已完成使命并移除。** 三个内存问题定位完成后，`Services/MemoryProbe.cs`、`tools/memory-report.ps1`、
> `LRS_DIAG_MEMORY` 开关文件、以及各处埋点（`App.OnLaunched` / `UpdateCurrentFolderContentAsync` /
> `RefreshCurrentFolderAsync` / `LocalizationService.SetLanguage` / `MainWindowView.NavigateTo` / `OnWindowClosed`）
> 已全部删除，运行时代码零残留。下面是当时的设计与判读标准，**以后再遇到类似"到底是不是泄漏"的问题可以直接照做**。

目的：把“工作集上涨”和“对象真的活着”区分开，否则改完无法证明有效。

### 1.1 内置探针（已实现）

**开启方式（推荐用开关文件）**：

```powershell
# ① 放一个开关文件：任何启动方式都生效（VS / 双击 / 开始菜单都行）
New-Item -ItemType Directory -Force (Join-Path $env:LOCALAPPDATA 'FastFluentFilesFolders') | Out-Null
Set-Content (Join-Path $env:LOCALAPPDATA 'FastFluentFilesFolders\LRS_DIAG_MEMORY') 'on'
# 想关闭就删掉这个文件即可

# ② 环境变量（只对「从这个 shell 里启动的进程」有效；从 VS / 资源管理器启动通常继承不到）
$env:LRS_DIAG_MEMORY = '1'
dotnet run --project FastFluentFilesFolders

# 跑完看你关心的那几项，一键汇总
pwsh -File tools\memory-report.ps1
pwsh -File tools\memory-report.ps1 -Tag lang       # 只看语言切换
```

日志落到 `%LOCALAPPDATA%\FastFluentFilesFolders\memory.log`（和 `crash.log`、`enum.log` 同目录）。

> 踩过的坑：从 VS / 资源管理器启动时环境变量**不会**继承到应用进程，所以只设 `$env:LRS_DIAG_MEMORY` 常常“看起来没生效、日志根本没生成”。这也是加开关文件的原因。

已埋的采样点：

| tag | 位置 | 形式 |
|---|---|---|
| `app-start` | `App.OnLaunched`（窗口 Activate 之后） | 单点 |
| `nav` | `UpdateCurrentFolderContentAsync` 的 UI 提交段末尾 | 单点（带 path/items/grouped/preloaded/耗时） |
| `refresh` | `RefreshCurrentFolderAsync` | 成对 before/done |
| `lang` | `LocalizationService.SetLanguage` | 成对 before/done |
| `page` | `MainWindowView.NavigateTo`（explorer↔settings↔plugins） | 成对 before/done |
| `app-exit` | `MainWindowView.OnWindowClosed` | 单点 |

每行字段（本机实测真实日志，Release 产物）：

```
22:34:58.062 nav#1 path=C:\Users\aaron\Downloads items=362 grouped=1 preloaded=0 dt=328ms
              heap=4.0MB live=2.0MB alloc=5.1MB ws=259.9MB->271.3MB gcs=7/7/7
              d_all=+2.09/+1.13/+58.93/+3.12 dt=531ms d_same=+0.30/+0.20/+12.00/+1.20 dt=16ms
```

- `heap`：未强制回收的托管堆已用；`live`：**强制回收后的存活对象**（判断泄漏的唯一可靠口径）；
- `alloc`：`GC.GetTotalAllocatedBytes`，进程启动以来的**累计分配**，只增不减、不受回收时机影响——"这次操作到底分配了多少"最硬的指标；
- `ws=x->y`：回收前 → 回收后的工作集；`gcs`：Gen0/1/2 回收次数；
- `d_all` / `d_same`：相对上一次任意标记 / 上一次同 tag 标记的差值，顺序都是 `heap/live/ws/alloc`。
  **成对标记（lang/page/refresh）只看 done 行**，它的 `d_same` 就是这一次操作的净增。

> **重要认识（实测得出）**：这个应用是 Release + trimming 构建，托管堆非常小——启动后 `heap` 只有 **1.9MB**，导航一个 362 项的目录后也才 4MB。原因是 WinUI 的 XAML 元素（`AppBarButton`、`MenuFlyoutItem`、`TextBlock`…）本质都是**原生对象**，托管侧只有投影包装器。
> 所以「切一次语言 +20MB」这类现象**必然主要发生在原生侧**：`live` 不涨而 `ws` 涨就说明是原生对象在积累。反过来说，`live` 只要有稳定的正增长（哪怕每次只有 0.5MB），才是真的托管对象被钉住。

强制回收默认开启（`GC.GetTotalMemory(true)`，每次多花几十毫秒），所以**这份日志不能用来评价流畅度**；只想看趋势可加 `LRS_DIAG_MEMORY_FORCEGC=0`（此时 `live` 退化为不回收的读数，`ws` 只有一个值）。

**判据**：
- `live`（`d_same` 第二个数）不随操作次数增长 → 没有真泄漏，只是分配量大 / 回收滞后；
- 只有 `ws` 涨、`live` 平 → WinUI 原生侧（XAML 元素、合成资源）滞留 —— **本项目最可能是这一种**（见上面的重要认识）；
- `alloc` 每跳一大截而 `live`、`ws` 都不涨 → 纯分配风暴，东西都被回收了，但那次分配的 GC 开销就是卡顿来源；
- `live` 也稳定涨 → 有真实引用钉住，需要 gcdump 抓类型分布。

`tools/memory-report.ps1` 会按 tag 汇总 `live/ws/alloc` 增量的平均值与峰值，并直接给出一句判断（托管侧积累 / 分配风暴 / 原生侧滞留 / 无明显泄漏）。

### 1.2 存量对象快照（只在需要时）

`dotnet-gcdump collect -p <pid>`（或 VS「拍摄快照」）在操作前后各拍一次，重点看这几个类型的实例数：

`FileSystemNodeViewModel`、`FileGroupHeader`、`BitmapImage`、`TableViewRow`、`TableViewCell`、`TextBlock`、`AppBarButton`、`MenuFlyoutItem`、`SettingsView`、`SettingsViewModel`。

这一步能直接回答“切换 20 次后是否真的留着 20 份工具栏”。

### 1.3 测量步骤（每项各跑一遍，每组 20 次操作）

| 项 | 操作 | 看哪个 tag |
|---|---|---|
| ② | 设置页里在「中文 / English」之间来回切 20 次 | `lang` 的 done 行 |
| ③ | 浏览页 ↔ 设置页来回 20 次 | `page` 的 done 行（`explorer->settings` 与 `settings->explorer` 分开看） |
| ① | A（小目录）↔ B（>500 项）来回 20 次；再在同一页连点刷新 20 次做对照 | `nav` 与 `refresh` |

预期基线（用来对后面的修复做对照）：`lang` 每次 +20MB 左右、`page` 每次 +7MB 左右、`nav` 随目录项数增长而 `refresh` 基本持平。

---

## 2. 问题 ②：反复切语言每次 +20MB（先修这个）

### 2.1 证据链

1. `LocalizationService.SetLanguage()` → `CurrentLanguage` 变更 → `MultiLanguageStringsViewModel.RefreshAll()` 对 `AllPropertyNames`（约 150 个属性）**逐个** `OnPropertyChanged`。

2. `MiddleFilesView` 的订阅完全不区分属性：

```csharp
private void OnMLPropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshAllStrings();
```

3. `RefreshAllStrings()` 里包含 `RefreshToolbar()`：

```csharp
private void RefreshToolbar()
{
    if (!_toolbarBuilt) return;
    ToolbarCmd.PrimaryCommands.Clear();   // 丢掉全部旧按钮
    BuildToolbar();                        // 新建 8 个 AppBarButton + 3 个 MenuFlyout + 20+ MenuFlyoutItem + ThemedIcon/Viewbox/TextBlock
}
```

4. `RefreshAllStrings()` 还会把 `_itemContextFlyout`、`_baseContextFlyout`、`_recycleItemFlyout`、`_recycleBaseFlyout`、`_pinToggleButton` 全部置 null，下次右键再整套重建。

结论：**一次语言切换 = 150 次工具栏全量重建 ≈ 数千个 XAML 元素**。这些元素被 `CommandBar.PrimaryCommands.Clear()` 丢弃后进入 WinUI 内部回收队列，短期不释放，于是“切一次涨 20MB，离开设置页也不降”。

同一模式下还有几个较轻的重复执行（合计不超 1MB，但应一并合并）：`RefreshHeaders()` ×150、`RefreshGroupHeaderNames()` ×150（每个分组头触发 5 列绑定刷新）、`FileOperationsButton.ApplyLocalizedStrings()` ×150。

### 2.2 修复步骤

**S2-1 语言切换只广播一次（核心）**

- 在 `MultiLanguageStringsViewModel` 增加 `public event EventHandler? LanguageChanged;`，在 `RefreshAll()` 末尾触发一次。
- 订阅方改为响应 `LanguageChanged`：
  - `MiddleFilesView`：`OnMLPropertyChanged` → 只在收到 `LanguageChanged` 时做 `RefreshAllStrings()`（保留 PropertyChanged 订阅只为兼容，或直接删掉）。
  - `FileOperationsButton`：同样改为一次 `ApplyLocalizedStrings()`。
  - `SettingsViewModel.OnMlPropertyChanged`：它需要按属性名转发 30 个文案属性，`x:Bind OneWay` 依赖属性通知，因此**保留**逐条通知，但它只做 `OnPropertyChanged(name)` 转发，代价极小，不用改。
- 兜底：给 `RefreshAllStrings` 加合并标志（`_langRefreshQueued` + `DispatcherQueue.TryEnqueue`），即使将来有人再加一条订阅也不会重建成风暴。

**S2-2 工具栏就地更新，不重建**

- `RefreshToolbar()` 改成：
  - 若“当前形态”（普通目录 / 回收站）未变 → 遍历 `ToolbarCmd.PrimaryCommands`，就地更新 `Label`、`ToolTipService.ToolTip`、内部 `TextBlock.Text`；
  - 只有形态切换（进入/离开回收站）才 `Clear()` + `BuildToolbar()`。
- 需要给 `TbIconBtn`/`TbLabelBtn`/`TbGlyphBtn` 产出的按钮打上“可更新标签”的标记（例如 `Tag = "localizable"`），或者统一走一个小型 `LocalizableButton` 包装，避免遍历时靠类型猜。

**S2-3 右键菜单改为“打开时刷新”**

- 语言切换时**不再**把 4 个 flyout 置 null。
- 改为在 `BuildXxxFlyout()` 里给 `flyout.Opening += (_,_) => Relocalize(flyout);`，每次弹出时按当前语言就地改文本。
- 好处：语言切换零成本；右键菜单文本也永远是最新的（现在是“切换后才重建”，如果切换时菜单正好开着会失效）。

### 2.3 验收

- 连续切换语言 20 次：`GetTotalMemory(true)` 增量 < 2MB，`WorkingSet64` 增量 < 5MB 且能回落；
- 期间 `ToolbarCmd.PrimaryCommands.Count` 恒定；gcdump 里 `AppBarButton`/`MenuFlyoutItem` 实例数不随切换次数增长；
- 功能回归：中/英/日（fumo）三种语言下工具栏、右键菜单、列头、分组头、设置页文案全部正确。

### 2.4 风险

- `RefreshAll` 的属性名列表若将来漏加，语言切换后某些文案不更新 —— 这是既有问题，本次不改（但 S2-1 后“漏加”只会影响那一个文案，不会造成风暴）。
- 就地更新要覆盖 `ToolTipService` 和 `AppBarButton.Label` 两个位置，漏一处会显示旧语言。

---

## 3. 问题 ③：浏览页 → 设置页每次 +7MB

### 3.1 证据链

1. `MainWindowView.NavigateTo("settings")` 每次都执行：

```csharp
ExplorerArea.Visibility = Visibility.Collapsed;
ContentFrame.Visibility = Visibility.Visible;
ContentFrame.Navigate(typeof(SettingsView));
```

2. `SettingsView.xaml` 是 `NavigationCacheMode="Disabled"` → **每次都构造一个全新的 SettingsView**：`new SettingsViewModel(App.ML)` + `InitializeComponent()`（20+ `ccl:SettingsCard`/`SettingsExpander`、4 个 `ComboBox`、2 个 `ListView`、`NumberBox`、`ToggleSwitch`）+ `LoadPluginSettings()` + `RefreshInstalledPluginsList()`（读 `LoadManifest()` 磁盘）。
3. 反向（设置页 → 浏览页）只切 `Visibility`，不新建任何东西 —— 与“反向不涨”的观察完全吻合。
4. `OnNavigatedFrom` / `SV_Unloaded` 已经正确退订（这点不用改），所以剩下的就是“整页反复分配、回收滞后”。

### 3.2 修复步骤

**S3-1 设置页常驻，不再走 Frame.Navigate**

- 在 `MainWindowView` 里持有 `private SettingsView? _settingsView;`，首次进入时 `_settingsView = new SettingsView(); ContentFrame.Content = _settingsView;`。
- 之后进入/离开只切 `Visibility`，并显式调用页面的 `OnEnter()` / `OnLeave()`（在 `SettingsView` 上新增两个 public 方法，内部就是现在的 `Resubscribe()` / `Unsubscribe()` + 刷新插件列表）。
- 顺带修掉 `ContentFrame.BackStack` 每次压入一个 `PageStackEntry` 的历史累积。

**S3-2 插件区块不要每次重建**

- `LoadPluginSettings()` 的 `PluginSettingsStack.Children.Clear()` + 重新问插件要卡片，只在“插件集合变化”（导入/卸载）时执行；`OnEnter()` 时直接跳过。
- `RefreshInstalledPluginsList()` 的 `PluginImporter.LoadManifest()` 是磁盘 I/O，进入页面时最多做一次，并缓存结果。

**S3-3（可选）会话级保留 vs 常驻的取舍**

- 常驻：固定多占约一份设置页（~7MB），但消除增长，且再次打开是瞬时的。
- 若担心固定开销，替代方案是保留 `Disabled` 但把页面实例缓存成“单槽复用池”（本质与常驻相同，只是能主动丢弃）。
- **建议先按常驻做**，因为设置页是高频入口，7MB 固定成本可以接受。

### 3.3 验收

- 浏览 ↔ 设置来回切换 30 次：`WorkingSet64` 增量 < 2MB 且稳定；gcdump 里 `SettingsView`/`SettingsViewModel` 实例数恒为 1；
- 功能回归：设置项改动即时生效并落盘（`AutoSaveConfig` 路径不能断）、语言下拉、背景模式、过渡模式、时间分组文件夹增删、插件导入/卸载。

### 3.4 风险

- 常驻后 `SettingsViewModel` 的“订阅/退订”必须靠 `OnEnter/OnLeave` 成对调用，漏调用会让旧的 `OnNavigatedFrom` 兜底失效；建议在 `MainWindowView` 的导航分支里集中处理，不要分散。
- 设置页不再重建，意味着“进入时重新读配置”的动作要显式补上（`HomePageFullPath` 等转发属性从 `AppConfigs` 实时读，问题不大）。

---

## 4. 问题 ①：两目录来回切换，内存随项数上涨

### 4.1 证据链

1. **离开目录立即清空子项缓存**：`OnSelectedFolderChanged` 里 `tab.FolderToRelease.ReleaseChildren()`，以及 `TryReleaseChildrenOnLeaving()` 里 `oldFolder.ReleaseChildren()`（未在目录树中展开时）。
   → 回到 A 必须重新枚举磁盘 + `BuildChildNodesAsync` **全新构造 N 个 `FileSystemNodeViewModel`**。

2. **每次导航做两遍分组构建**：`UpdateCurrentFolderContentAsync` 在后台 `new GroupedFileList()` + `SetItems` 建好 `PendingPrebuiltSource`，同时新建一个 `ObservableCollection` 快照；随后 `MiddleFilesView.UpdateGroupedSource` → `LrsTableView.UpdateSourcePrebuilt` **又把内容灌进稳定源 `_groupedSource` 做第二遍 `SetItems`**（含 `Clear()` + N 次 `Add`）。

3. **整批换内容会丢弃全部行容器**：`Clear()` 触发 `Reset` 通知，ListView 把 N 个容器（每行 `TableViewRow` + 若干 `TableViewCell` + `ThemedIcon`/`TextBlock`/`Image`）全部退回回收池，再按新内容重建。

4. **每次导航无条件预热 160 个图标**：`PrefetchIcons(newContent)`。节点是全新的，`Icon` getter 全部重新走一遍（缓存命中所以不解码，但有排队与赋值开销）。

5. **刷新路径是反证**：`RefreshCurrentFolderAsync` → `ReloadChildrenAsync` **按路径复用旧节点实例**，不新建 VM。所以“同一页反复刷新不涨”恰恰证明增长来自“新节点 + 新容器”的重复分配，而不是渲染本身。

“到达临界点停止增长”符合“托管堆到达 GC 水位后进入回收/分配平衡”的阶梯式表现，加上 WinUI 原生对象回收滞后，工作集口径看起来像缓慢泄漏。

### 4.2 修复步骤（按性价比排序）

**S4-1 目录“热缓存”：来回切换不重建节点（首选，收益最大）**

- 在 `MainWindowViewModel` 增加一个小容量队列 `_warmFolders`（容量 1 即可，见下条“淘汰顺序”）。
- 把现在两处**立即** `ReleaseChildren()`（`SelectedFolder` setter → `TryReleaseChildrenOnLeaving`，以及 `OnSelectedFolderChanged` 里的 `tab.FolderToRelease`）改成“先入队”，入队时若超出容量再对最旧的执行 `ReleaseChildren()`。
- 回访命中：`UpdateCurrentFolderContentAsync` 已有 `folder.IsLoaded` 分支，命中后跳过 `LoadChildrenAsync`，直接用既有的 `Children` 建源 —— 也就是说**只需不提前释放，回访路径天然是零分配**。
- 失效必须严格：任何文件操作（粘贴/删除/剪切/重命名/新建/还原/彻底删除）之后都要清空 `_warmFolders` 并走现有的 `InvalidateFolderByPath`，保证“操作后立即看到结果”这条硬要求不被破坏。
- 目录树展开中的节点本来就不释放（`oldFolder.IsExpanded` 早退），保持现状。

五个必须写对的细节（复核后补充）：

1. **淘汰顺序**：容量 1 时若“先入队再淘汰”，B→A 会把 A 挤掉（A 正好是即将进入的目录），结果每次切换都重建、还多付一次 `ReleaseChildren()` + 重新入队，**比现状更差**。正确顺序：进入 X 时先 `_warmFolders.Remove(X)`（认领命中项）→ 再把旧目录入队 → 最后按容量淘汰。
2. **不能与被持有的目录冲突**：切标签页时 `_suppressFolderRelease` 会抑制释放，目录仍被 `tab.FolderNode` 持有；若热缓存又把它入队，淘汰时会对**另一个标签页正在用的目录**执行 `ReleaseChildren()`，直接把它清空。入队前必须用 `CollectLoadedDirectoryNodes()`（现成的，遍历 RootDirectories / PinnedShortcuts / Tabs / SelectedFolder）过滤掉仍被持有的节点。每次导航一次，成本可接受。
3. **新鲜度 TTL**：命中缓存不再重新枚举磁盘，外部程序改动会看不到。每项记 `LastLoadedUtc`，命中但超过阈值（建议 5~10s）走完整重载。用户主动点“刷新”走 `RefreshCurrentFolderAsync`，不受影响（总是强刷）。
4. **超大目录阈值**：节点数超过阈值（建议 5000）不进热缓存，直接释放，避免常驻内存翻倍。
5. **只对普通目录生效**：与 `TryReleaseChildrenOnLeaving` 的既有白名单保持一致（`Folder`/`Drive`、非展开、非压缩包内部节点）。

**S4-2 一次导航只构建一次数据源（方案已按复核修正）**

原计划的 (a)“删掉 `PendingPrebuiltSource`、前台直接 `UpdateSource`” **不可取**：`prebuilt` 存在的唯一理由就是把分组构建与排序挡在 UI 线程之外（`UpdateCurrentFolderContentAsync` 的注释写得很明确），删掉它等于把 O(N log N) 排序搬回 UI 线程，大目录会明显卡顿回归。

正确做法 (b')：**后台只算“排序结果 + 分组键”，不再构建 `GroupedFileList`**。

- 后台 `Task.Run` 里：`items` 快照 → `List.Sort(比较器)` → 分组目录把 `SortByTime` 写回节点。**不 new `GroupedFileList`、不 `SetItems`、不 `new ObservableCollection(items)`**。
- 前台：`FileGrid` 新增“已排序”入口，`SetItems` 跳过排序，只做分组桶 + `RebuildFlat`。
- 净省（每次导航）：后台 N 次 `Add`（含 CollectionChanged 开销）、一次分组字典构建、一个 `GroupedFileList` 对象、一份 N 长度集合拷贝。
- **前台那 1 次 `Reset` + N 次 `Add` 必须保留**——它是把内容挂进稳定 `ItemsSource` 的必要成本，不能省。
- 风险：`SetItems` 的“已排序”分支要保证与 `SortEntries` 的默认顺序语义一致，别把 `SortByTime` 写回与 `GetTimeGroup` 的时序搞错。

**S4-3（原“按路径差分”方案）—— 已否决**

复核结论：切目录时节点实例本来就是全新的，差分会退化为 `Remove×N + Add×N`，产生 2N 次集合通知，**比现在的 1 次 Reset + N 次 Add 更慢**；对刷新路径又不如下面的“等价跳过”。收益为负、复杂度为正，不做。

**S4-3' 换内容前的“序列等价”短路（替代方案，纯收益）**

- 在 `GroupedFileList.SetItems` 开头做一次 O(N) 引用比较：新内容与现有内容**逐引用相同且顺序相同** → 直接 `return`（不 `Clear`、不 `Add`、不动滚动位置）。
- 这对 `RefreshCurrentFolderAsync` 是纯收益：它现在把 `_displayedFolderNode` 置 null 后走完整重建，而 `ReloadChildrenAsync` 已按路径复用节点 → 文件没变时本来可以零操作，现在是整批 Clear+Add（视觉闪一下 + 全部行容器丢弃）。
- 元数据变化由 `ApplyMetadata` 就地更新属性、绑定自动刷新，不需要重建；只有顺序或成员变化才走重建。

**S4-4 预热收敛**

- `PrefetchIcons` 从“每次导航前 160 项”改成“仅在首次加载该目录时预热”（热缓存命中时不预热），或按可见区 + 前后各一屏预热。

### 4.3 验收

- A（小目录）↔ B（500+ 项）来回切换 50 次：
  - `GC.GetTotalMemory(true)` 在切换 10 次后应基本持平；
  - `WorkingSet64` 增量 < 10MB 且不再单调上升；
  - gcdump 里 `FileSystemNodeViewModel` 实例数 ≈ 当前目录项数 × 热缓存层数（不再随切换次数线性增长）。
- **加载性能不能退化**（与内存同等重要）：
  - 首次进入大目录（5000 项）的耗时不变；
  - A↔B 来回切换的首次可交互时间**明显下降**（应省掉磁盘枚举与 N 个节点构造，预期从几十~几百 ms 降到帧级）；
  - 点“刷新”的响应不慢于现状（S4-3' 命中时应趋近 0）。
- 硬性功能回归：文件操作（新建/重命名/删除/粘贴/剪切/还原/彻底删除/清空回收站）后，无论当前目录是否被热缓存，表格与目录树都**立即**看到结果。

### 4.4 风险

- 热缓存让常驻节点从 1 个目录变成 2 个（项数翻倍）。若用户目录里动辄几万项，固定开销会明显上升 —— 用阈值保护（节点数 > 5000 不进热缓存）。
- **新鲜度风险**：命中缓存不重读磁盘，外部程序新建/删除的文件在 TTL 窗口内看不到。这是本次唯一的行为变化，必须靠 TTL + 手动刷新兜底，并在 CHANGELOG 里写明。
- 失效逻辑漏掉任何一个文件操作入口，都会出现“操作后看不到结果”，这是本项目最敏感的行为，必须有清单逐项核对：`AddNewItemToView`、`Paste`、`Delete`、`PermanentDelete`、`Rename`、`RestoreRecycleItemAsync`、`PermanentDeleteRecycleItemsAsync`、`EmptyRecycleBinAsync`、`CommitRenameAsync`。
- 与 `_suppressFolderRelease`（切标签页抑制释放）叠加时，释放责任要唯一：被标签页持有的节点归标签页管，不进热缓存。

### 4.5 对加载性能的影响（逐环节复核）

热缓存命中（A→B→A 的第二趟）相对现状省掉 / 保留的环节：

| 环节 | 线程 | 现状 | 热缓存命中后 |
|---|---|---|---|
| `SafeEnumerateEntries`（`EnumerateFileSystemInfos` 逐项取 size/时间/属性） | 后台 | 有，真磁盘 I/O | **省掉** |
| `SortEntries` × 2（目录/文件各一次 O(N log N)） | 后台 | 有 | **省掉** |
| N 个 `FileSystemNodeViewModel` 构造（`Path.GetFileName`/`GetExtension`/`IsSpecialFolder`，目录项还额外 `new PlaceholderNodeViewModel()`） | 后台 | 有 | **省掉** |
| N 次 `ApplyMetadata`（含 2 次 `DateTime.ToString("yyyy-MM-dd HH:mm:ss")` → 2N 个字符串） | 后台 | 有 | **省掉** |
| UI 线程 `Children.Clear()` + `AddChildWithSort` × N（每次 Add 触发目录树 CollectionChanged） | UI | 有 | **省掉** |
| 后台分组构建（S4-2 后只剩排序 + 分组键） | 后台 | 有 | **仍有** |
| 前台挂载：1 次 `Reset` + N 次 `Add`（稳定 ItemsSource） | UI | 有 | **仍有**（必要成本） |
| `PrefetchIcons` 160 项 | UI | 有（新节点发请求） | 有，但全部缓存命中（getter 直接返回 `_icon`），开销接近 0 |

结论：热缓存省掉的正是这条链上最重的部分（磁盘枚举 + 节点构造 + 元数据字符串 + 树节点批量 Add），**加载只会更快，不会更慢**；它不省的是表格挂载本身，那部分由 S4-2 减少重复构建、由 S4-3' 让刷新短路。

`IsSpecialFolder` 已确认是纯字符串比较（无 I/O），`SafeEnumerateEntries` 的 `GetDefaultOrderMode()` 需要访问 `App.SharedViewModel`，在后台线程读 VM 属性属于既有用法，本次不动（若要更严谨，可在导航开始时把 `DefaultOrderMode` 作为参数传进枚举，顺带去掉跨线程读 VM）。

---

## 5. 执行顺序与工作量

| 步骤 | 内容 | 预计 | 依赖 |
|---|---|---|---|
| 1 | ~~加 `LRS_DIAG_MEMORY` 探针~~ **已完成并移除**；基线已取到、三项修复均已验证 | 完成 | — |
| 2 | S2-1 / S2-2 / S2-3 + 复测 + CHANGELOG | 2~3h | 1 |
| 3 | S3-1 / S3-2 + 复测 + CHANGELOG | 1.5~2h | 2（避免语言风暴干扰测量） |
| 4 | S4-1 + S4-2 + S4-3' + 复测 + CHANGELOG | 3~4h | 3 |
| 5 | （视测量结果）S4-4 | 1h | 4 |

按项目规则，每一步落地后写 `CHANGELOG.md`：性能类改动进 `### Changed`，明确写“用户看到什么变了 + 实测数据”。

---

## 6. 顺带加固（本次不展开，登记备查）

以下都是“目前只创建一次所以没出事，但只要被重建就会永久泄漏”的隐患：

1. `LRSBreadcrumb.xaml.cs` 构造函数里两个匿名 lambda 永久订阅单例事件且从不退订：

```csharp
App.LocalizationService.PropertyChanged += (_, e) => { ... };
App.SharedViewModel.PropertyChanged += (_, e) => { ... };
```

→ 改成具名方法 + `Loaded`/`Unloaded` 配对订阅。

2. `FileOperationsButton.xaml.cs` 构造函数里 `App.ML.PropertyChanged += OnMLPropertyChanged;` 从不退订，且它在工具栏重建路径上会被重新创建（虽然目前是 XAML 里的单实例）。

3. `SettingsViewModel` 在构造函数里就订阅 `AppConfigs.PropertyChanged`，然后依赖 `OnNavigatedFrom` 退订 —— 如果页面从未导航离开（例如窗口直接关闭）就一直挂着。影响很小。

4. `FileSystemNodeViewModel` 每项都带一个 `TagViewModel` 实例；若目录是几万项，这份常驻开销值得评估（惰性创建）。
