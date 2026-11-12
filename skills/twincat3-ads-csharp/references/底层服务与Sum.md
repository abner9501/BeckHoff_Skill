# ADS 底层服务（IndexGroup）与通用 Sum

> 背景知识：变量"怎么被找到"、句柄和内存寻址底下是什么、Sum 怎么混装任意寻址。
> 正文 §一 只留一句话结论，这里放完整表。
> 官方命令表出处：`adsnet_v7.txt:92780-92849`（PDF 提取，部分宏名有串行）。
> **PLC 侧的数据区大小 / `AT%M*` 地址**不在这（属 `twincat3-build-verify` §六·五）。

## 一、变量是怎么"找到"的

PLC 编译下载时把**符号表**（名字 / 类型 / 地址）带进 runtime；上位机用 SymbolLoader 把它**上传**下来，
之后按 `InstancePath` 取。底层全是一组 IndexGroup：

| IG | 宏名 | 用途 | 对应库 API |
|---|---|---|---|
| `0xF003` | `ADSIGRP_SYM_HNDBYNAME` | **名字 → 句柄** | `CreateVariableHandle` |
| `0xF004` | `ADSIGRP_SYM_VALBYNAME` | **按名字读写值**（名字放写缓冲区，IO=0）| `ReadAny("符号名", type)` |
| `0xF005` | `ADSIGRP_SYM_VALBYHND` | **按句柄读写** | `ReadAny/WriteAny(handle)` |
| `0xF006` | `ADSIGRP_SYM_RELEASEHND` | 释放句柄 | `DeleteVariableHandle` |
| `0xF007` | `ADSIGRP_SYM_INFOBYNAME` | 按名取符号信息 | — |
| `0xF009` | `ADSIGRP_SYM_INFOBYNAMEEX` | 按名取扩展符号信息 | `SymbolCache` 首次查符号 |
| `0xF00B` | `ADSIGRP_SYM_UPLOAD` | **上传整棵符号表** | `loader.GetSymbolsAsync()` |
| `0xF00C` / `0xF00F` | `SYM_UPLOADINFO` / `...INFO2` | 符号上传信息（大小 / 版本） | 同上（内部先问） |
| `0xF008` | `ADSIGRP_SYM_VERSION` | 取符号版本 | — |
| `0xF080` / `0xF081` | `ADSIGRP_SUMUP_READ/WRITE` | Sum 批量 | `SumHandleRead/Write` |

- **`0xF003` 只是"名字→句柄"的一次映射**；之后所有读写都走 `0xF005` ——
  这解释了为什么**句柄一坏整条链就断**（重连必须作废旧句柄）。
- **符号表有版本**（`0xF008`）：PLC 重新下载程序 → 版本变 → 本地缓存的符号信息必须失效。
- ⚠ 原文 `0xF00C` 那行的宏名在 PDF 提取时串行了（和 `0xF00B` 重复）；**IndexGroup 编号本身是清楚的**。
- ⚠ loader 内部"先 info 再 upload"是按服务语义**推断**的，手册只给了命令映射表，没写调用顺序。

### ★ 反编译核对：符号名读到底走哪条服务

2026-10-19 反编译 `TwinCAT.Ads 7.0.172` 核对（不是推断）：

- **`AdsClient.ReadValue(path, type)`** → 内部 `SymbolCache`，默认
  `SymbolLoaderSettings(Flat, ValueAccessMode.SymbolicByHandle)`
  → `AdsValueAccessor.ReadByHandle`：**每次 `0xF003` 建句柄 → `0xF005` 读 → 释放**
  （`TryCreateVariableHandle` 先查客户端 `_handleCache`，命中才复用）。
  ⚠ 首次查符号走 **`0xF009`**（INFOBYNAMEEX），之后走本地符号缓存。
- **`AdsClient.ReadAny(path, type)`** → **`0xF004`**，`IO = 0`，符号名放在写缓冲区。
- **`ValueAccessMode.IndexGroupOffset`** 的访问器才会用
  `symbol.IndexGroup / IndexOffset`（例如 `0x4020 / 405152`）直接读内存。

**结论**：`IAdsSymbol.IndexGroup/IndexOffset` 是**符号解析出的物理地址（元数据）**，
**不是符号名读请求里的 IG/IO**。符号名读默认是“建句柄 + 按句柄读”，不是内存寻址。

实测对得上（本机，7.0.172）：

| 路径 | 请求 | 8B | 816B |
|---|---|---:|---:|
| `ReadValue(path,type)`，无缓存句柄 | `0xF003` + `0xF005` + 释放 | 3.63 ms | 3.61 ms |
| `ReadValue(path,type)`，句柄缓存命中 | `0xF005` | 1.18 ms | 1.25 ms |
| `ReadAny(path,type)` | `0xF004` | 1.20 ms | 1.52 ms |
| `ReadAny(handle)` / `Read(0xF005,h,buf)` | `0xF005` | 1.09 ms | 1.26 ms |
| `Read(0x4020, offset, buf)` | 内存地址 | 1.08 ms | 1.24 ms |

→ **`ReadAny(path,type)`（0xF004）是“一次请求按名读”，不建句柄**；
`ReadValue` 慢是因为它每次都走“建句柄 + 按句柄读”，只有在句柄已缓存时才降到 1.2ms。

> 大数组/结构体：**整变量建一个句柄 + `Read(0xF005, h, buf)`**；
> 本机 2448B 结构体数组实测读 **1.25ms** / 写 **1.49ms**，与内存法同档。
> `ReadAny<byte[]>(handle)` 不行（长度未知），要自己给缓冲区。

### ★ 符号表是怎么“拉”的：两种模式，别混

| 用的 API | 行为 |
|---|---|
| `SymbolLoaderFactory.Create(...).Symbols` | **整棵符号表上传**（`0xF00B` 等），之后名字/类型/地址/成员偏移全是本地查表 |
| `AdsClient.ReadValue(path,...)` / `ReadSymbol(path)` | **按需**：本地符号缓存里没有这个符号时，走 `0xF009`（INFOBYNAMEEX）查一次再缓存 |

- 所以“用了 `ReadValue` 就等于符号表已经拉全”是**错的**——它只查你用到的那个符号。
- 本探针打印的 36 个成员偏移来自 `loader.Symbols` 的**整表上传**（一次请求），不是逐个读出来的。
- ⚠ **按符号名注册通知**：库会先 `_handleCache.CreateVariableHandle(path)`（`0xF003`），
  再以 `IG=0xF005 / IO=handle` 注册。**这条路径会占用一个变量句柄**；
  句柄因 PLC 重下程序失效时，通知也一起失效。

### ★ 句柄缓存 vs 显式句柄（不是两条数据通路）

`HandleCache` 是**按符号名索引 + 引用计数**的（反编译核对）：

- `CreateVariableHandle(path)`：缓存命中且 `Handle != 0` → **直接返回，0 次往返**；否则 `0xF003`
- `DeleteVariableHandle(h)`：`refCount--`；**只有 `refCount == 0` 才真正发 `0xF006` 释放**
- `ReadValue` 内部的 `ReadByHandle` 是 `Create → 0xF005 → Delete`：
  - 你已持有该符号的句柄 → Create/Delete 只是加减计数 → **只剩 1 次 `0xF005`**（实测 1.1ms）
  - 没有 → **每次** `0xF003` + `0xF005` + 释放（实测 3.6ms）
- `Resurrect()`：符号版本失效时，按**缓存的符号路径**批量重建句柄（PLC 重下程序后能自愈）
- **裸 handle 数字没有路径信息**，失效后无法复活；要自己保存 path 重建
- 直接用 IG/IO（0x4020 + 偏移）读写/注册：PLC 重新下载后**数据区布局可能变**，
  旧地址会指到别处；名字/句柄路径能跟着重建，裸地址不能

→ **“走缓存”和“走显式句柄”最终都是 `0xF005 / handle`；区别在“谁持有句柄、何时释放、
失效后能不能按路径重建”。**

### 内存寻址（0x4020 / 0x4021）不是"另一个区"

`%M`（按字节/字/双字）和 `%MX`（按位）是**同一块内存的两种访问粒度**，对应两个 IndexGroup：

| IG | 含义 | IndexOffset |
|---|---|---|
| `0x4020` | READ_M / WRITE_M | **字节偏移** |
| `0x4021` | READ_MX / WRITE_MX | **位地址**（`字节号 × 8 + 位号`）|

同一个 BOOL（TMC 里 `BitOffs = 3081152`）：
`0x4021 + 3081152`（位）和 `0x4020 + 385144`（字节 = 3081152÷8）指向同一个字节 —— 实测两边都读到同一个值。

> 别被"MX 区"这个叫法误导：没有第二块物理内存，只有两种寻址语法。

### ★ 现场弯管程序的 I/O IG（2026-10-19 实测，只读符号表）

| 区 | IG | IO | 说明 |
|---|---|---|---|
| 输入 `%I*` | `0xF020` | 字节偏移 | 例 `MAIN.Auto_Start` IO=460335、`MAIN.bEStop` IO=460331 |
| 输入位 `%IX*` / BIT | `0xF021` | 位偏移 | 例 `MAIN.stItpToPlc.ChnState.bIsEStopRequested` IO=3688808 |
| 输出 `%Q*` | `0xF030` | 字节偏移 | 例 `MAIN.oil_2` IO=460426、`MAIN.oil_pump` IO=460424 |
| Internal | `0x4040` | 字节偏移 | 普通 GVL 变量 |
| Internal 位 | `0x4041` | 位偏移 | GVL 里的 BIT |

- **`0x4040` 是 Internal，不是 `%Q`** —— 别按老表把 `0x4040` 当输出区
- `stItpToPlc` / `stPlcToItp`（NCI 接口结构）也落在 `0xF020` / `0xF030`：
  这两个区是**链接到 NC 的过程映像数据**，不只是物理 I/O
- 不同工程/链接方式可能不同；**以运行时 `IAdsSymbol.IndexGroup/IndexOffset` 为准**
  （`ads-speedtest addr 0xF020` 可只读汇总）
- ⚠ **IO 也不固定**：`AT%I*` / `AT%Q*` 的偏移由编译/链接分配，程序或 I/O 配置变化会移动；
  生产代码不要写死 IG/IO，启动时 `ReadSymbol` 一次并缓存，或直接用符号名/句柄

### ★ 非 PLC 服务器：NCI 501（2026-10-19 实测）

- 501 也有自己的符号表：实测 **1,059 个符号**，例如
  `Channel 2.Group 5.BlockNumber`（IG=0x3105 / IO=13），**句柄读 4.18ms** 成功
- `ReadDeviceInfo` 在 501 返回 `0x701`（服务不支持），但符号上传/句柄正常
- **G 代码行元素没有符号名**：搜 `Line / Text / BlockSearch` = 0；
  `0x2300+通道 / 0x20000001` 只能走原始 IG/IO
- 上位机读 G 代码行：优先读 PLC 副本 `GVL.GVL_sGCodeLine`；
  或直连 501 用原始 IG/IO（通道号可从 `MAIN.stItpToPlc.ChnId` 读）
- 所以：**501 不是“没有句柄的服务器”，而是“符号表只覆盖 NC 自己的数据，部分原始元素没有符号”**

## 一·五、通用 Sum：可以混装任意寻址方式

`SumHandleRead` 只是"每种子命令都填 `IG=0xF005`"的便利封装。要混用不同寻址，用 **`SumRead` / `SumWrite`**：

```csharp
var entities = new List<SumDataEntity>
{
    new IgIoSumReadEntity(0x4021, 位地址, 1),   // 内存寻址
    new IgIoSumReadEntity(0xF005, 句柄, 1),     // 句柄寻址
    new IgIoSumReadEntity(0xF020, 偏移, 4),     // %I 输入区……都行
};

var cmd = new SumRead(client, entities,
                      SumAccessMode.IndexGroupIndexOffset,
                      SumCommandErrorStrategy.Strict);
var res = cmd.ReadRaw();          // res.ReadBlocks[i] : ReadOnlyMemory<byte>
```

**实测（2026-10-18，7.0.172）**：两个实体指向同一个变量 `MAIN.A`，读回都是 1 —— **混装合法**。

**混装 vs 分开的时延**（`arrSum` 20000 字节，拆成内存/句柄各读 10000 字节，每项 100 次）：

| 方式 | 请求数 | 均摊 |
|---|---|---|
| **混合 Sum**（1 个内存实体 + 1 个句柄实体）| **1** | **1.470 ms** |
| 分开两次 `Read`（或两个各 1 子命令的 Sum）| 2 | 2.714 ms |
| 纯内存 Sum（1 个子命令读 20000 字节）| 1 | 1.213 ms |

→ **混合省一次往返（~1.2ms）**；多一个子命令只多 ~0.26ms。混装的价值是"把 N 次请求压成 1 次"，不是让单块更快。

### ★ 三个 API 坑（都编译/运行踩过）

| 坑 | 正确写法 |
|---|---|
| `SumAccessMode` 没有 `Read` | 用 **`IndexGroupIndexOffset`**（枚举值：`IndexGroupIndexOffset` / `ValueByHandle` / `ValueByName` / `AcquireHandleByName` / `ReleaseHandle`）|
| 结果没有 `Data` 属性 | 数据在 **`ReadBlocks`**（`IList<ReadOnlyMemory<byte>>`），另有 `SubErrors` / `SubCommandCount` |
| `List<IgIoSumReadEntity>` 传不进去 | 声明成 **`List<SumDataEntity>`** —— 泛型不协变，`IList<IgIoSumReadEntity>` 转不了 `IList<SumDataEntity>` |

> **PLC 数据区（DataArea）大小**属于 PLC 侧（编译时决定、`AT%M*` 地址从 TMC 查），
> 见 `twincat3-build-verify` 的 §六·五。
