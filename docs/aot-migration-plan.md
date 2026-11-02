# AOT 迁移计划

> 目标：让 `dotnet publish -p:PublishAot=true` 的产物能正常启动并跑通全部功能。
> 本文只做计划与决策点，不含代码改动。
> 门禁脚本：`tools/check-aot.ps1`（已落地，跑一次 publish 给出「还欠多少 + 退出码」）。

---

## 0. 现状基线（当前代码，`check-aot.ps1` 实测）

```
结论：NOT AOT READY —— 门禁类警告 350 条（项目代码 131 条 / 依赖库 219 条）
```

### 项目代码 131 条（必须清零）

| 文件 | 条数 | 类别 | 是否需要改逻辑 |
|---|---|---|---|
| `Services/ShellLocations.cs` | **60** | `dynamic` + `Type.GetTypeFromProgID` | **是（无 AOT 守卫）** |
| `WinUI.TableView/…/ObjectExtensions.cs` | 19 | 反射表达式树（`GetProperty`/`GetMethod`） | 部分 |
| `ViewModels/MainWindowViewModel.cs` | 17 | `MVVMTK0046`（`[RelayCommand]` 与 `[GeneratedBindableCustomProperty]` 冲突） | 是 |
| `Services/QuickAccessHelper.cs` | 14 | `dynamic` + `ProgID` | **否（已有守卫）** |
| `Extensions/PluginPackage.cs` | 8 | `JsonSerializer` 反射 | 是（可改源生成） |
| `Extensions/ExternalPluginLoader.cs` | 4 | `Assembly.LoadFrom` + `GetTypes` | **是（AOT 下不可行）** |
| `Services/NativeContextMenuHelper.cs` | 2 | COM 封送（`out IShellItem`） | 可选（已有 PIDL 路径） |
| `WinUI.TableView/…/TableView.cs` 等 4 个 | 4 | 反射/泛型解析 | 部分 |
| `ViewModels/FileSystemNodeViewModel.cs` | 1 | `MVVMTK0046` | 是 |
| `Helpers/GroupedFileList.cs` | 1 | `MVVMTK0048` | 是 |
| `Services/ShellIconHelper.cs` | 1 | COM 封送（`out IShellItemImageFactory`） | 可选（已有 PIDL 路径） |

### 依赖库 219 条（改不了）

`IL3050` 123 / `IL2026` 66 / `IL2075` 12 / `IL2070` 9 / `IL2072` 5 / `IL2050` 3 / `IL2067` 1，
来自 WinUI、WindowsAppSDK、CommunityToolkit 等。**只能 `<NoWarn>` 白名单压制 + 风险登记**，目标是项目代码 0 条，而不是总数 0 条。

---

## 1. 为什么不能只看警告：AOT 有三类障碍

| 类别 | 会不会报警告 | 后果 | 本次是否已踩过 |
|---|---|---|---|
| **① 编译期静态警告** | 会（IL2xxx / CsWinRT / MVVMTK） | 裁剪掉依赖的成员 → 运行时空引用/静默失效 | 350 条待清 |
| **② 运行时绑定失效** | **不会** | `{Binding}` 拿不到 `ICustomProperty` → 界面空白，或在布局期崩在 `MeasureOverride`（`COMException 0x80004005`） | **就是设置页那次崩溃** |
| **③ 原生能力不可用** | 部分（IL3050） | `dynamic` / `ProgID` / 动态加载程序集在 AOT 下**直接抛异常** | `ShellLocations` 全裸 |

**②是最危险的**：警告干干净净，一跑就崩。所以只做警告清理不够，必须配合冒烟。

---

## 2. 需要拍板的决策点

### 决策 1：`ShellLocations`（60 条，最大一块）怎么办？

它用 `dynamic` + `Type.GetTypeFromProgID("Shell.Application")` 做三件事，**没有任何 AOT 守卫**：

| 功能 | 方法 |
|---|---|
| 网络位置枚举 | `GetNetworkComputerPaths` |
| Linux/WSL 位置枚举 | `GetWslLocations` |
| 回收站枚举 / 删除 / 还原 | `EnumerateRecycleBin` / `DeleteRecycleBinItems` / `RestoreRecycleBinItems` |

| 选项 | 工作量 | 后果 |
|---|---|---|
| **A. 加守卫 + 降级** | 小（半天） | AOT 版里网络/WSL/回收站**不可用**（返回空或提示），普通版权限不变 |
| **B. 用裸 vtable 重写** | 大（2-3 天） | 功能全保留；`Services/ShellQuickAccess.cs` 已有同类实现可参考（`IShellFolder`/`IEnumIDList`/`IContextMenu` 手工调 vtable） |
| **C. 混合** | 中（1.5 天） | 回收站用 Win32（`SHQueryRecycleBin` + `IShellFolder`，裸调用）+ 网络/WSL 降级 |

> 建议先做 **A** 让 AOT 能跑起来、把门禁清零，之后再单独排期把 B/C 当作功能增强。理由：回收站/网络/WSL 在 AOT 版暂时不可用，比"整个应用崩"好得多，而且可以先用 AOT 版验证其他一切。

### 决策 2：`ExternalPluginLoader`（外部插件）怎么办？

`Assembly.LoadFrom` + `GetTypes()` 在 Native AOT 下**本质上不可行**（运行时没有 JIT，无法加载新程序集）。

| 选项 | 后果 |
|---|---|
| **A. AOT 版禁用外部插件**（运行时守卫 + 置灰入口） | 内置插件（`SamplePlugin` / `ArchivePlugin` 走 DI）不受影响；外部 `.lrsplug` 只在普通版可用 |
| B. 改为可选特性 + 文档说明 | 同 A，只是文案上更明确 |

> 建议 A。这一项没有真正的技术解，只能是产品决策。

### 决策 3：`MainWindowViewModel` 的 17 个命令（`MVVMTK0046`）

`[RelayCommand]` 生成的命令属性**不会**被 `[GeneratedBindableCustomProperty]` 包含 → AOT 下 `{Binding xxxCommand}` 失效（按钮点了没反应，不报错）。

实际被 `{Binding}` 用到的只有 `TopView.xaml` 里 5 个：

```
NavigateToPathCommand / NavigateToSubFolderCommand / GoBackCommand / GoForwardCommand / GoUpCommand
```

| 选项 | 工作量 | 后果 |
|---|---|---|
| **A. 只改被绑定的那 5 个**：手写 `ICommand` 属性（去掉 `[RelayCommand]`） | 小（1 小时） | 剩余 12 个命令仍报 `MVVMTK0046` → 需 `NoWarn` 或逐个抑制 |
| **B. 17 个全改手写 `ICommand`** | 中（半天） | 门禁彻底干净，最推荐 |

> 建议 B。命令逻辑很短（多半是转调一个方法），手写 `ICommand` 每个 6~8 行，机械但一次到位。

### 决策 4：依赖库 219 条

没有技术手段清零（不 fork WinUI）。只能：
- `<NoWarn>` 精确按 `IL3050;IL2026;…` + 在本文档登记"这是已知且接受的风险"；
- 用真实 AOT 发布 + 冒烟覆盖主要界面来兜底。

> 这一项不需要选，只是需要你确认接受"219 条库警告带进 AOT 版"。

---

## 3. 分阶段实施

### 阶段 0：基础设施（半天，先行）

| 项 | 内容 |
|---|---|
| `tools/check-aot.ps1` | ✅ 已完成：publish 一遍 → 统计项目代码/依赖库 → 退出码 |
| `tools/aot-smoke.ps1` | 待建：发布 AOT → 启动 → 等 N 秒 → 检查进程存活 + `crash.log` 无新增 → 判定 |
| `LRS_DIAG_BINDING=1` 诊断 | 待建：`FirstChanceException` 抓 `NotSupportedException` / `XamlParseException` / `COMException` 落 `binding.log`，把"②绑定失效"变成可见的 |
| `build.ps1` 防呆 | 给发布命令显式加 `-p:PublishAot=false`，防止再次误把 AOT 混进普通构建 |

**验收**：`check-aot.ps1` 可以作为发布前必跑项；`aot-smoke.ps1` 能给出"崩没崩"的明确结论。

### 阶段 1：低风险机械项（1 天）

- `PluginPackage` → `System.Text.Json` 源生成（`JsonSerializerContext`），8 条
- `QuickAccessHelper` → 把 `dynamic` 段抽成 `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` 私有方法，调用点（已有 `IsShellComAvailable` 守卫）加 `[UnconditionalSuppressMessage]` + 理由，14 条
- **给 6 个 `Properties*ViewModel` + `FileOperationItem` 加 `[WinRT.GeneratedBindableCustomProperty]`**（它们都没有 `[RelayCommand]`，加特性零冲突）
  - 这一步同时修的是**运行时障碍②**：属性窗口、文件操作浮层的 `{Binding}` 在 AOT 下才能工作

**验收**：131 → **约 109**；属性窗口在普通构建下行为不变。

### 阶段 2：命令与绑定（1~2 天）

- 17 + 1 个命令改手写 `ICommand`（决策 3 = B）
- `FileGroupHeader` 去掉 `[RelayCommand]` 继承（`MVVMTK0048`，1 条）
- `SettingsViewModel` 的 2 个 `[RelayCommand]`：设置页已无 `{Binding}` 使用，可保留 + 抑制，或一并改手写

**验收**：131 → **约 90**；`TopView` 的导航按钮在 AOT 下可点。

### 阶段 3：`ShellLocations`（决策 1）

按 A 做：加 `IsShellComAvailable` 同款守卫 + AOT 下返回空/友好提示；顺手把 `dynamic` 段包进标注方法。

**验收**：131 → **约 30**；AOT 版网络/WSL/回收站显示为空而不是崩。

### 阶段 4：插件与 COM 封送（1 天）

- `ExternalPluginLoader`：`RuntimeFeature.IsDynamicCodeSupported` 守卫 + AOT 下禁用外部插件入口（决策 2 = A），4 条
- `NativeContextMenuHelper` / `ShellIconHelper`：确认 PIDL 路径已覆盖 → 加抑制（3 条），或彻底删掉 COM 分支

**验收**：**约 23 条**。

### 阶段 5：`WinUI.TableView` 库（1 天）

`ObjectExtensions`（19）+ `TableView`/`TableViewDateColumn`/`TableViewTimeColumn`/`TypeExtensions`（4）：
给反射入口补 `[DynamicallyAccessedMembers]` 标注，标注做不到的用抑制 + 说明。

**验收**：项目代码 **0 条**。

### 阶段 6：依赖库白名单 + 全量验收（1 天）

- `<NoWarn>` 精确压制 219 条库警告，本文档登记风险
- 真实 AOT 发布 + 冒烟矩阵：

| 界面 | 验证点 |
|---|---|
| 启动 | 窗口出现、`crash.log` 无新增 |
| 表格 | 导航、排序、分组、右键菜单、双击进入 |
| 设置页 | **展开「按时间分组文件夹」**（就是崩过那次）、三个下拉框切换语言 |
| 属性窗口 | 常规 / 安全 / 兼容性 / 数字签名 / 详细信息 各标签页 |
| 文件操作浮层 | 复制/移动进度、失败提示 |
| 目录树 / 面包屑 / 固定栏 | 展开、导航、复制地址 |
| 语言切换 | 中/英切换后各处文案 |

**验收**：`check-aot.ps1` 退出码 0 + 冒烟矩阵全过。

---

## 4. 总量与排期

| 阶段 | 内容 | 预计 |
|---|---|---|
| 0 | 基础设施 | 0.5 天 |
| 1 | JSON / QuickAccessHelper / Properties*ViewModel | 1 天 |
| 2 | 命令手写 | 1~2 天 |
| 3 | ShellLocations（决策 1 = A 降级） | 0.5 天 |
| 4 | 插件 + COM 封送 | 1 天 |
| 5 | WinUI.TableView | 1 天 |
| 6 | 白名单 + 冒烟 | 1 天 |

**合计约 6~7 天**（若决策 1 选 B 裸 vtable 重写，再加 2~3 天）。

---

## 5. 风险登记（需要接受的部分）

| 风险 | 说明 |
|---|---|
| 依赖库 219 条 AOT 警告 | WinUI/WindowsAppSDK 未完全 AOT 化。AOT 版可能出现**未知的**界面异常；靠冒烟覆盖主要路径 |
| `{Binding}` 无声失效 | 本计划靠"给被绑定类型加特性 + x:Bind 化 + 冒烟"三重覆盖，但无法保证 212 处全部场景 |
| 功能降级 | 网络 / WSL / 回收站（决策 1 = A）、外部插件（决策 2 = A）在 AOT 版不可用 |
| 两条发布线 | AOT 版与普通版行为会有差异，需要分别验证；建议先让 AOT 版本作为"实验性发布"并行一段时间 |

---

## 6. 已完成的准备工作（本会话内）

- `tools/check-aot.ps1`：AOT 门禁（publish 模式，区分项目代码/依赖库，自动恢复 bin 的 runtimeconfig）
- 修复了误留的 `<PublishAot>true</PublishAot>`（它导致普通构建也带 AOT 标志 → 全部 `{Binding}` 失效 → 设置页崩溃）
- 内存三问题已修复（语言切换分配风暴、目录热缓存、内容等价短路），新代码均无反射/`dynamic`/运行时绑定
