---
name: twincat3-build-verify
description: TwinCAT 3 / TC3 PLC 工程本身的操作：建工程（.tsproj / .plcproj）、建 POU / GVL / DUT / 变量、无头编译并取回编译器报错原文、激活下载、启动 TwinCAT、变量地址与数据区（`AT%M*` / MArea / TMC BitOffs、`%I`/`%Q` 的 IG）。用户说到「建个 PLC 工程」「加个全局变量」「帮我编译一下」「看不到具体报错在哪一行」「把配置下载下去让它跑起来」「TcXaeShell 报 RPC 调用被拒绝」「M区怎么扩的」「M区扩展」「变量地址怎么变了」「地址会移动吗」「TMC 里 BitOffs 怎么换算」「%I/%Q 的 IG 是多少」时加载。只做工具层面的操作，不含往 POU 里写 ST 业务逻辑。不负责：上位机（C#）读 / 写 PLC 变量 → twincat3-ads-csharp。
---

# TwinCAT 3 PLC 工程（建 / 编 / 验）

> 2026-10-13 摸通编译验证；2026-10-15 补上"用 Automation Interface 建工程"。
> 起因：用户是 "改完必须 build 验证" 的人，而 TC3 这条路一开始完全走不通。
>
> **本 skill 覆盖**：**建**（PLC 项目 / POU / GVL / DUT / 变量，见 §六）· **编**（无头编译 + 报错原文，见 §一~五）· **激活下载**（见 §七）
> **边界**：只做**工具层面**的操作 —— **不含往 POU 里写 ST 业务逻辑**（那块我们还没打通，别在这儿找）。
> **判据**：① 改完任何 TC3 PLC 代码，下面这条命令必须自己跑，别让用户去 IDE 里点；
> ② **只要任务碰 TC3，先把本 skill 读完再动手** —— 2026-10-15 没读它就直接抓脚本，在项目配置里白绕了十几轮。

## Codex 执行边界

- 新增的 PLC 变量若需要给 TwinCAT HMI 使用，还需加载本地 `tchmi-symbol-mapping` skill，完成 Map Symbol 并核对实际 HMI Server 的映射和读取；PLC 编译通过不等于 HMI 已 map。在线 PLC 尚无新符号时如实报告，不为验证映射自行下载或激活。

- 编译、读取工程和查询目标信息属于只读验证，可在用户给定范围内执行。
- `ai-activate*.ps1` 会下载配置并可能重启 TwinCAT；`ai-del-plc.ps1` 会删除工程树；创建、链接和写入工程的脚本会修改解决方案。执行这些动作前必须确认确切的解决方案、目标 NetId 和用户授权。
- 远程目标不得从历史示例推断。必须使用用户明确提供或在当前任务中只读确认的 NetId，并在执行前回显目标。
- 所有脚本必须显式传入 `-Sln`。不要复用作者测试工程路径或测试 NetId。
- `guard.json` 是 Claude hooks 的机制，Codex 版不使用；以上边界由执行代理直接遵守。

## 适用环境

- Windows、TwinCAT 3 XAE / TcXaeShell、Windows PowerShell 5.1；脚本依赖本机 TwinCAT Automation Interface 组件。
- 文中的版本、错误表现和性能结论来自注明日期的历史实测，只作为诊断证据；换 TwinCAT、库、控制器或网络后需要复核。
- 官方文档路径指向用户指定的本机 Beckhoff 知识库 `<beckhoff-library-root>`。
- Codex 结构、PowerShell 语法和无设备副作用检查于 2026-11-02 完成。

---

## 〇、坑速查（先扫一眼）

- **x64 编译报 `POINTER TO SINT/GUID/STRING → UDINT`（Tc3_Module / Tc2_MC2）→ 先查 tsproj 的 `<Project ... Target64Bit="true">`**（§六·六）
  —— **不是任务绑定**：实测未绑定项目在 x64 工程里照样编过
- 跑成 **`pwsh`(PS7)** → 默认 MTA → `RPC_E_SERVERCALL_RETRYLATER`；**必须 Windows PowerShell 5.1**（§三.1）
- **没注册 `IOleMessageFilter`** → 第一次 COM 调用就 `RPC_E_CALL_REJECTED (0x80010001)`（§三.2）
- **ErrorList 用后期绑定 → 永远 0 条、还不报错**（§三.3 → `references/errorlist-early-binding.md`）
- 建项目设了 **`TcAutomationSettings.SilentMode`** → 不绑 task → **x64 必炸、x86 却编得过**（§六）
- **`MSBuild.exe` / `TcXaeShell.exe /Rebuild` 编不了 `.tsproj`** —— 别再试（`references/excluded-paths.md`）
- 脚本里**写了中文** → PS 5.1 按 GBK 读、截断成语法错误；**只许 ASCII**（§八）
- **`ai-del-plc.ps1` 只删工程树、不删磁盘文件夹** → del 后要**手动清掉 `<项目名>/` 目录**，否则紧接着 `ai-add-plc.ps1` 建同名项目会撞 `plcproj already exist` 失败（§六）
- **`AT%M*` 的地址是编译器分配的** → 编译后从 TMC 的 `BitOffs` 查（字节偏移 = `BitOffs / 8`）；只有 `AT %M100` 这种才写死地址（§六·五）
- **PLC 数据区大小不是固定的** —— TwinCAT 按变量实际占用**编译时自动扩**（实测 640KB→1MB→1.5MB→2.5MB），改小会缩回（§六·五）
- **`AT%M*` 地址会随布局移动**：前面增删变量/改大小，后面的地址可能变。
  实测 `arrSum` 20,000→2,000,000B 使 `ST1` 405152→2385152（+1,980,000）；1B dummy 可被 7B 填充吸收（不动），8B dummy 使 `ST1` +8；同源码重编稳定 → 别写死地址（§六·五）
- **服务 Stop 之后单靠 `Start-Service` 不能恢复**（PLC 端口报 `0x12 Port is disabled`）→ 必须重新 `ActivateConfiguration`（§七）

---

## 一、一句话

**`msbuild` 编不了 TC3。** 只能起一个 `TcXaeShell` 的 DTE 实例，用它的 `SolutionBuild` 编，再从 **Error List** 里把报错捞回来。

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File <skill-dir>\scripts\build-plc.ps1 -Sln <x.sln>
```

可传参（默认值就是那套测试工程，换工程时给）：

| 参数 | 默认 |
|---|---|
| `-Sln` | 必填：目标解决方案的绝对路径 |
| `-Cfg` | `Debug\|TwinCAT RT (x64)` ← **平台就在这个串里** |
| `-PlcPrj` | 可选：目标 `.plcproj`；空字符串 = 编整个解决方案 |
| `-ProgId` | `TcXaeShell.DTE.15.0` |
| `-Visible` | 不传 = 不弹窗口（照样能拿到报错）|

## 二、输出长什么样

**过了**：
```
== creating DTE (TcXaeShell.DTE.15.0), visible=False
== building: Debug|TwinCAT RT (x64)
   project: ...\Untitled1\Untitled1.plcproj
== LastBuildInfo = 0   (0 = OK, N = N failed projects)
== ErrorList: 0 item(s)
== BUILD OK
```

**没过**（故意塞了一行 `fbXml.NoSuchMethodForBuildTest()`）：
```
== LastBuildInfo = 1
== ErrorList: 2 item(s)
   lvl4 <project-dir>\MAIN.TcPOU(61,1): No case label found
   warning 2026/10/13 9:48:07 159 ms  | 'TwinCAT XAE': Project 'Untitled1' build for platform 'TwinCAT RT (x64)' failed.
== BUILD FAILED
```

**文件名、行号、列号、描述全都有** —— 够直接定位、直接改。`lvl` 1=error / 2=warning / 3=message / 4=... （TwinCAT 的编译错报的是 4）。

---

## 三、三个坑（按踩到的顺序）

### 1. 必须 STA
`pwsh`（PS7）默认 **MTA**，`RPC_E_SERVERCALL_RETRYLATER`。用 **Windows PowerShell 5.1**（`powershell.exe`，默认 STA），或 `pwsh -STA`。

### 2. 必须注册 `IOleMessageFilter`
不注册，第一次 COM 调用就：
```
RPC_E_CALL_REJECTED (0x80010001)  调用被拒绝
```
原因：VS 的消息泵一忙，就拒收 COM 调用。修法是把 `IOleMessageFilter` 注册到**调用线程**上（`CoRegisterMessageFilter`），`RetryRejectedCall` 里对 `rejectType == 2` 返回一个重试延迟（如 99 ms）。
👉 官方手册 §4.2 附近就有现成代码，别自己发明。

### 3. ★ ErrorList 要用 `EnvDTE80.DTE2` 早绑定
`ToolWindows.ErrorList` 只在 **`EnvDTE80.DTE2`** 上；**PowerShell 的 COM 是后期绑定**，`$dte.ToolWindows` 解析的是老的 DTE1 →
**`ErrorItems.Count` 永远返回 0，而且不报错**（最容易骗过自己的一步）。

**修法**：`Add-Type -Path` 载入 `EnvDTE.dll` + `EnvDTE80.dll`，再写一段 C# 桥接类里 `DTE2 _dte2 = (DTE2)raw;`，用早绑定取。

👉 **细节（程序集路径 / 完整代码 / 两个附带坑）见 `references/errorlist-early-binding.md`。**
（`build-plc.ps1` 里已经封好了，一般**不用自己写**。）

---

## 四、已经排除的路（别再试）

**`msbuild` / CLI 这条路是死的** —— 下面这些全试过、全不通，别再花时间。

👉 完整清单（8 条，含各自的报错原文）见 `references/excluded-paths.md`。

## 五、判定与细节

- **判定看 `SolutionBuild.LastBuildInfo`**：`0` = 全部成功，`N` = N 个工程失败。**双向验证过**（正常=0；故意塞错=1 并把报错打出来）。
- **配置文件串里的坑**：`SolutionConfigurations` 里的对象是**按平台拆开的**，`Name` 全叫 `Debug`/`Release`（8 个），而且 `SolutionConfiguration` 上**没有 `PlatformName` 属性**。所以**别想按名字挑平台** —— 直接 `BuildProject("Debug|TwinCAT RT (x64)", <plcproj>, $true)`，平台就写在配置串里。
- **`.tsproj` 与 `.sln` 的 GUID 必须一致**：`.sln` 里那行工程的 GUID 要等于 `.tsproj` 的 `<Project ProjectGUID="...">`；`.plcproj` 里的 `<Application>` / `<TypeSystem>` / `<Implicit_Task_Info>` 那几个 GUID 是挂进 tsproj 时生成的。
- **`_Boot\TwinCAT RT (x64)\Plc\Port_851.app` 的时间戳**是独立的旁证：编过了才会更新。
- 编译产物 `_CompileInfo\*.compileinfo` **只在成功时写**。

---

## 六、建 PLC 项目 / 对象 / NC 轴（Automation Interface）

> 2026-10-15 打通。脚本 `<skill-dir>\scripts\ai-add-plc.ps1`（建）、`ai-del-plc.ps1`（删）、`ai-add-nc-axis.ps1`（建 NC 任务 + 仿真轴）。
> 在作者测试工程中全自动建出 PLC 项目 + 一个 BOOL 变量并通过 x64 编译；该测试工程未随技能提供。

### API 速查

👉 **完整表（建项目 / 建 GVL / 建 POU / subtype 编号 / 删项目 / 保存）见 `references/automation-api.md`。**

### ★ 最大的坑：`TcAutomationSettings.SilentMode = $true` 不能设

设了之后 TcXaeShell **会跳过"新建 PLC 项目 → 自动绑定 task"这一步**：项目实例停在 `Context="Default"`（tsproj 里没有 `ManualConfig` / `TaskPouOids`，`Target64Bit` 也不写）。**在这个未绑定状态下编译**，TC 生成的任务胶水代码在 **x64** 下必然报错：

```
IecTaskModule.TcGetObjectName : Cannot convert type 'POINTER TO SINT'   to type 'UDINT'   [tc3_module, 3.4.5.0]
IecTaskModule.TcSetObjectName : （同理）
GuidsEqual                    : Cannot convert type 'POINTER TO GUID' to type 'UDINT'
```

**x86 恰好编得过**（指针 4 字节，转 UDINT 合法）——所以症状长成"x86 行、x64 不行"，极易误判成"库坏了 / 平台不匹配"。**`Tc3_Module` 库本身没毛病。**
在 XAE 里手动打开保存一次，TC 会自动把绑定补上（多出来的项目还会被自动分到 `PlcTask1`），之后同一份就编得过。**去掉 SilentMode 后，全自动流程一次过。**

> ⚠ **2026-10-22 更正：当时把这串指针报错归因于「未绑定」，不成立。**
> 同日实测：AdsTest（x64 工程）里新建的 `X64Test` 停在 `Context=Default`，x64 编译**照样通过**；
> Motion_Abort 工程里项目**绑定/未绑定都报**同一串错，真因是工程缺 `Target64Bit`（见 §六·六）。
> SilentMode 仍建议不设（它确实跳过自动绑定），但**别再用这串报错判断绑定**。

### 其它坑

- `ITcSmTreeItem::get_Child(i)` 在 **PS 后期绑定里调不到**（手册的 PS 示例是错的）→ 用 `foreach ($c in $node)` 枚举
- 从 `powershell -File` 传**数组参数**会塌成单个字符串（`-Names A,B` → `"A,B"`）→ 参数收 `[string]`，自己 `-split ','`
- 建之前该注册的 `IOleMessageFilter` 一样要注册（§三）

---

## 六·五、变量地址与数据区（`AT%M*` / TMC / DataArea）

### 怎么查变量的实际地址

| 写法 | 地址 |
|---|---|
| `v1 AT %M100 : BYTE;` | 字面第 100 字节，**固定不变** |
| `arr AT%M* : ARRAY[1..20000] OF BYTE;` | **编译器分配**，编译后从 TMC 查 |

TMC（`<工程>\<PLC项目>\<项目>.tmc`）里每个符号都有 `BitOffs`：

```xml
<Symbol>
  <Name>MAIN.A</Name><BitSize>8</BitSize><BaseType>BOOL</BaseType>
  <BitOffs>3081152</BitOffs>          <!-- 位偏移 -->
</Symbol>
```

- **字节偏移 = `BitOffs / 8`**（实测 `MAIN.A` = 3081152 位 = 385144 字节）
- ADS 访问：`0x4021` + 位地址，或 `0x4020` + 字节偏移 —— 两者指向同一字节（实测都读到同一个值）
- **`AT%M*` 地址是编译器分配的**：布局变化**可能**移动，但不一定——小增删可被对齐填充吸收。
  实测（本地编译对照）：
  - `ST1` 前加 **1B** dummy → `ST1` 2385152 **不动**（填进 7B 对齐填充）
  - 加 **8B** dummy → `ST1` 2385152 → **2385160**（+8），`arrSt` 同步 +8
  - 删掉 dummy 重编 → 回到 2385152
  - 同一份源码**连续编译两次** → 地址完全一致（385144 / 385145 / 2385152 / 2385968）
  - 另一例：`arrSum` 20,000→2,000,000B，`ST1` 405152→2385152（+1,980,000）
  → 生产代码不要写死地址，用 `ReadSymbol` 或重新查 TMC
  （⚠ 跨编译器版本/换机器未验证）
- 数组元素也有独立 `InstancePath`（`MAIN.arr[1]`…），可单独当符号用

### DataArea 大小不是固定的

TwinCAT 在**编译时**按变量实际占用自动决定 `%M` / `Internal` 区大小（实测同一工程）：

| 最大变量 | 变量结束位置 | MArea |
|---|---|---|
| 无大数组 | — | 512 KB |
| 100KB 数组 | 498809 | 640 KB |
| 500KB 数组 | 897145 | 1 MB |
| 2MB 数组 | 2482297 | 2.5 MB |
| 8MB 数组 | 8773753 | 8.5 MB |

**更大规模实测（2026-10-19，同一工程 `AdsTest`，只编译不下载）：**

| `arrSum` (BYTE) | 变量结束位置 | MArea / Internal |
|---|---|---|
| 2,000,000 | 2,388,416 | 2,555,904（2.44 MB）|
| 4,000,000 | 4,388,416 | 4,521,984（4.31 MB）|
| 8,000,000 | 8,388,416 | 8,519,680（8.13 MB）|
| 16,000,000 | 16,388,416 | 16,515,072（15.75 MB）|
| 32,000,000 | 32,388,416 | 32,505,856（31.00 MB）|
| 67,108,864（64 MiB）| 67,497,280 | 67,633,152（64.50 MB）|

- **一直扩到 64 MiB 都能编译通过**（没撞到上限）
- **余量不恒定**：小数组那组实测 139143 字节（约 136KB），大数组实测 117KB ~ 167KB；区按 4KB 对齐
- **真机验证（2026-10-19，CX5230-0175）**：8,000,000 字节的 `AT%M*` 数组下载后运行正常 ——
  符号表 `MAIN.arrSum` = `ARRAY [1..8000000] OF BYTE`、`IG=0x4020`、`IO=385146`，心跳正常翻转
- **改小会缩回**（实测闭环：500KB → 改成 100KB → 区从 1MB 缩回 640KB）
- 实际值看 TMC：`<DataArea><Name>PlcTask MArea</Name>…<ByteSize>…</ByteSize>`
- **`%M`（`AT` 可寻址）和 `Internal`（普通 GVL 变量）是两个独立的区**，各自按需扩展
- `%I` / `%Q` 由 System Manager 的 I/O 配置决定，PLC 项目没绑 IO 时 TMC 里就没有这两个区
- 所以"换个变量大小、区自己变了"是正常现象，**不是配置被改**

### 任务周期与 BaseTime（2026-10-22 实测打通）

**规则**：任务周期 = **Cycle ticks × BaseTime**；`Cycle ticks` 必须是 **≥1 的整数**（填 0 任务起不来）。

**要改的几处（必须一致，单位不同）**：

| 文件 | 位置 | 单位 | 10ms 的值 |
|---|---|---|---|
| `<PLC>\PlcTask.TcTTO` | `<CycleTime>` | **微秒** | `10000` |
| `*.tsproj` | `<Task Id="N" ... CycleTime="...">` | **100ns** | `100000` |
| `*.tsproj` | `<Context><CycleTime>` | **纳秒** | `10000000` |
| BaseTime（按 CPU 核）| TIRS → CPUs → `CPU id=N` → `<BaseTime>` | **100ns** | 默认 `10000`（1ms）|

- **TcTTO 和 tsproj 的 Task 两处必须一起改**；只改 TcTTO 会编译失败：
  `No Task below System->Tasks exists with Priority 21 and CycleTime 2ms !`
- Context 的 CycleTime 编译时会自动同步（也可手改）
- **BaseTime 是按 CPU 核设的**，默认 1ms。10ms 任务用默认 BaseTime 即可（ticks=10）；
  **非整数倍（如 0.5ms）必须先把对应核 BaseTime 改成 0.5ms**（`5000`），
  否则 PLC 停在 Config、`0x6`（`Target port could not be found`）
- TIRS 可用 Automation Interface 读写：`LookupTreeItem("TIRS").ProduceXml()/ConsumeXml(xml)`
  （⚠ 工程被已打开的 TcXaeShell 实例占用时，新实例保存不了，需要先关掉那个实例）

**操作顺序**：

1. 改 `PlcTask.TcTTO` 的 `<CycleTime>`（µs）
2. 改 `.tsproj` 的 `<Task ... CycleTime="...">`（100ns）
3. （非整数倍时）改对应核 BaseTime（TIRS，100ns）
4. 编译：`build-plc.ps1 -Sln ... -PlcPrj ...`
5. 下载：`ai-activate-remote.ps1 -Sln ... -TargetNetId <本机/现场 NetId>`
   （`-TargetNetId <local-net-id>` = 本机；`<target-net-id>` = 现场）
6. 验证：读 `TwinCAT_SystemInfoVarList._TaskInfo[1].CycleTime`（100ns，`100000` = 10ms），
   同时看 `CycleCount` 增量（2 秒 +200 ≈ 10ms）和 `CycleTimeExceeded`

> ⚠ `ads-speedtest read ... uint` 读 `CycleTime` 会报 `OverflowException`（工具类型映射问题），
> 用 C# 小程序 `ReadValue(..., typeof(uint))` 读。

> ⚠ 每周期翻转的测试变量不要包在 `TON` 里：10ms 任务 + `PT=T#10MS` 的自复位 TON
> 实际要 3 个周期才翻一次（≈30ms）；要每周期翻转就无条件 `bFastToggle := NOT bFastToggle;`

## 六·六、工程目标平台 `Target64Bit` —— x64 指针报错的真因（2026-10-22 实测）

**症状**：x64 配置编 PLC 报一串
```
GuidsEqual: Cannot convert type 'POINTER TO GUID' to 'UDINT'                 [tc3_module]
IecTaskModule.TcGetObjectName / TcSetObjectName: 'POINTER TO SINT/STRING' → 'UDINT'
_F_Init / _F_ReadStatus / _F_TcMC_IsValidAxisRef: 'POINTER TO …' → 'UDINT'   [tc2_mc2, 需要轴时]
```
同一工程用 x86 配置**编得过**。

**真因**：tsproj 工程根 `<Project ...>` **缺 `Target64Bit="true"`** —— 工程本身是 32 位工程，
x64 配置下库按错误指针宽度编译。实测：补上后 x64 一次通过。

**不是任务绑定**（同一天的反证）：
- AdsTest（x64 工程）里新建的 `X64Test`：`Context=Default`、**未绑定** → x64 编译 **0 错误**
- Motion_Abort（32 位工程）里：手动补 Context 绑定、`LinkedTask=TIRT^PlcTask` 都做了 → **x64 仍报**
- 把 NC 删掉、把所有 DTE 实例关掉 → 不影响

**怎么补**：
1. 直接编辑 tsproj：`<Project ProjectGUID="…" Target64Bit="true" ShowHideConfigurations="#x6">`
2. **注意**：会执行 `File.SaveAll` 的 DTE 脚本会把该属性丢掉（内存里工程仍是 32 位 → 保存时重写）。
   实测 `build-plc.ps1` **不保存**，所以「改文件 → 直接编译」属性会保留。
3. 激活 solution 的 x64 配置（`SolutionConfiguration.Activate()`）**不会**写这个属性（实测）。
4. Automation Interface 没找到设置它的 API；XAE UI 的工程属性里设 x64 是正路。
   （判据：改完 tsproj 后 grep 到 `Target64Bit="true"`，编译后仍在 → 生效。）

**判据表**：

| 工程 Target64Bit | 编译配置 | 结果 |
|---|---|---|
| 有 `true` | x64 | ✅ 通过 |
| 缺失 | x64 | ❌ 指针报错 |
| 缺失 | x86 | ✅ 通过（指针 4 字节，转 UDINT 合法）|

---

## 七、激活 / 下载 + ADS 读写

> 2026-10-15 打通（本机 851 端口实测）。激活脚本位于 `scripts/ai-activate.ps1`。

### 激活配置

```powershell
$sysManager.ActivateConfiguration()    # 编译 + 下载配置
$sysManager.StartRestartTwinCAT()      # runtime 进 Run
```

### 切换目标系统（本机 ↔ 现场）

TcXaeShell 的工程会**记住上次的目标系统**（工具栏下拉：`<Local>` 或 `CX-xxx (NetId)`）。
直接跑 `ai-activate.ps1` 时，它会对着**工程里保存的目标**激活 —— 即使下拉显示的是现场目标（`ERROR`），也不会自动切回本机。

```powershell
# 切到现场
ai-activate-remote.ps1 -Sln <x.sln> -TargetNetId <target-net-id>

# 切回本机（本机 NetId）
ai-activate-remote.ps1 -Sln <x.sln> -TargetNetId <local-net-id>
```

- 本机 NetId 用 `AdsClient.Connect(851)` 后打印的地址获取（本机实测 `<local-net-id>`）
- 脚本内部：`ITcSysManager2::SetTargetNetId(netId)` → `ActivateConfiguration()` → `StartRestartTwinCAT()`
- ⚠ **本机激活需要管理员权限**：非管理员时 `StartRestartTwinCAT` 报 `E_ACCESSDENIED (0x80070005)`；用提权的 PowerShell 跑
- 目标断线时下拉显示 `ERROR`；先确认目标在线（`Get-TcTargetInfo -NetId ...`）再激活
- 激活输出里的 `target before/after` 就是实际目标；**真机下载前确认已备份**
- 下载后用 ADS 读 `TwinCAT_SystemInfoVarList._AppInfo.ProjectName` 核对程序名

### 读目标型号 / 系统信息（只读）

`TcXaeMgmt` 模块自带（`C:/TwinCAT/AdsApi/Powershell/TcXaeMgmt/`）：

```powershell
Import-Module 'C:/TwinCAT/AdsApi/Powershell/TcXaeMgmt/TcXaeMgmt.psd1'
Get-TcTargetInfo -NetId <target-net-id> | Format-List *
```

返回：

```
TargetType     : CX52x0-WIN 10.0
TargetVersion  : 3.1.4024.65
TargetHardware : Model: CX5230-0175, Serial: 0, Date: ...
OsImage        : Microsoft Windows NT 10.0 (TwinCAT: 3.1.4024)
```

- **这是查控制器型号的正路**（ADS `ReadDeviceInfo` 只给 “TwinCAT System”，不给硬件）
- 同一模块还有 `Get-TcRouterInfo` / `Get-TcVersion` / `Get-AdsState` / `Read-TcValue` 等
- `ai-target-info.ps1` 只返回 SYSTEM 节点的 AmsAddress/BootSettings，**不含型号**

- ⚠ **会重启 TwinCAT** —— 本机没接硬件随便跑；**真机上动手前先问用户**
- 两个都**返回空串 = 没报错**（有错才返回错误描述文本）
- ⚠ `StartRestartTwinCAT()` 是**发起**重启就返回 —— **紧接着查 `IsTwinCATStarted()` 会是 `False`**（runtime 还在起），别被吓到。
- ⚠ **PLC 真正进 Run 要等几十秒**（实测激活后 ~40 秒内 ADS 仍报 `0x6 Port not found` / `0x12 Port is disabled`）——
  急着跑上位机会误判"没激活成功"。**验证看这两处**：
  - `C:\TwinCAT\3.1\Boot\CurrentConfig.xml` 的 `CreateTime` 应该刷新
  - `C:\TwinCAT\3.1\Boot\Plc\Port_851.app` 时间戳应该刷新（851 = 该 PLC 项目的 AmsPort）

### ADS 读写

- 本机有老库 `C:\TwinCAT\AdsApi\.NET\v4.0.30319\TwinCAT.Ads.dll`（**4.4.7**，.NET Framework）
- **但既有项目与 ads-bench 统一用 NuGet `Beckhoff.TwinCAT.Ads` 7.0.172 + net8.0** → 新工具跟着这个版本走，保持"测的是同一套实现"
- 最小可用：

```csharp
using TwinCAT.Ads;
using AdsClient plc = new();
plc.Connect(851);                                        // 第二/三个 PLC 项目 = 852 / 853
bool v = (bool)plc.ReadValue("GVL_Test.bTest", typeof(bool))!;
plc.WriteValue("GVL_Test.bTest", true);
```

- 实测（2026-10-15）：读 `False` → 写 `True` 读回 `True` → 写 `False` 读回 `False` ✅
- NuGet 源连不上会报 `NU1900` warning，**包有本地缓存就不影响**

---

## 八、相关

- **官方手册**：`<beckhoff-library-root>\automation\twincat3\TC3_Automation_Interface_EN.pdf` = TE1000《TwinCAT 3 Automation Interface》v1.6.2
  - 编译：§4.2 `IOleMessageFilter` / §4.3.1.1 `BuildProject` / §4.3.1.6 Error List / §6.1 STA
  - 建对象：§4.3.5.1 PLC best practice（建项目）/ **§5.3.3 Tree item sub types**（那串编号表）/ §5.3.6 `CreateChild` / §5.3.7 `DeleteChild` / §5.8 `ITcPlcIECProject`
- 原始 PDF：`<beckhoff-library-root>\automation\twincat3\TC3_Automation_Interface_EN.pdf`
- 参考实现（社区 PoC，思路来源）：GitHub `benhar-dev/ps-dte-errorlist`
- **脚本**（都在 `scripts/`）：`build-plc.ps1`（编译 + 报错原文）· `ai-add-plc.ps1`（建项目 / 建 GVL）· `ai-add-nc-axis.ps1`（建 NC 任务 / 仿真轴）· `ai-add-motion-plc.ps1`（Tc2_MC2 轴引用 + MC_Power 使能程序）· `ai-link-plc-axis.ps1`（PLC AXIS_REF ↔ NC 轴）· `ai-link-plc-task.ps1`（LinkedTask，效果有限）· `ai-del-plc.ps1`（删项目 / 删 task）· `dump-build-output.ps1`（导 Output 全文）· `ai-activate.ps1`（本机激活 + 启动）· **`ai-activate-remote.ps1`（指定 NetId 的远程激活）**。
- ★ **本 skill 的配套脚本都在上面这个 `scripts/`** —— 以后为它写的新脚本也放这儿，别散在项目目录里。
- ⚠ **脚本只许写 ASCII** —— PS 5.1 读无 BOM 的 UTF-8 会按 GBK 解，中文字面量会截断成语法错误
