---
name: twincat3-ads-csharp
description: 用 C# 上位机通过 ADS 读 / 写 TwinCAT 3 PLC 变量，包括连接、符号与句柄读写、批量操作、通知订阅、类型映射、线程模型、错误诊断和性能测量。任务涉及 `TwinCAT.Ads`、`AdsClient`、`ReadValue`、`WriteValue`、`AddDeviceNotification`、通知不刷新、写入未生效或连接阻塞 UI 时使用。建工程、编译和激活不在此技能范围。
---

# TC3 ADS 客户端（C#）—— 读 / 写 PLC 变量

> **范围**：只讲**上位机（C#）用 ADS 读写 PLC 变量**。建工程 / 编译 / 激活 → `twincat3-build-verify`。
> **料**：既有项目 2026-08 ~ 2026-10 实测（跨网线 + 本机回环都量过）；2026-10 又在独立 demo
> 并在独立 ADS demo + 新建 TC3 工程上复现确认；原测试工程未随技能提供。数字有出处，不是估算。
> **库**：`Beckhoff.TwinCAT.Ads`（NuGet；项目用 **7.0.172** / net8.0）。

## Codex 执行边界

- 阅读代码、检查符号和只读 ADS 查询可以直接用于诊断。
- 写变量、注册高频通知、性能/压力测试、断连重连实验以及任何可能改变设备状态的操作，必须先确认目标 NetId、端口、符号、允许写入的值和用户授权。
- 不得把本文历史实测的 NetId、符号名或项目路径当作当前环境。现场性能数字只适用于文中标注的控制器、TwinCAT 和 ADS 库版本，换环境要复测。
- 单次大块传输、批量句柄和高频通知必须从小规模开始，并持续观察 PLC 实时负载；不得在生产控制器上复现事故边界。
- 配套脚本默认可能包含测试符号结构。运行前先读参数与硬编码符号，禁止直接对未知设备运行写入或压力模式。

## 适用环境

- Windows、.NET 8、`Beckhoff.TwinCAT.Ads` 7.0.172，以及可用的本机或远程 ADS 路由。
- 文中的性能和故障边界来自注明日期与环境的历史实测，不是跨控制器、网络和库版本的保证。
- 官方文档路径指向用户指定的本机 Beckhoff 知识库 `<beckhoff-library-root>`。
- Codex 结构与三个随附 C# 项目的无设备编译检查于 2026-11-02 完成。

## 〇、坑速查（先扫这一屏）

- 注册非 Ex 却订阅 Ex 事件 → 收 0 包，**不报错**
- Ex 注册 STRING 漏 `args[0]` → `ArgumentNullException`
- cycleTime 整除信号周期 → **静默失明**（§4.2）
- 休眠 / PLC 下程序 → 订阅作废，**读还通**，零告警（§4.3）
- **重连 / 重注册通知前必须先清本地缓存** —— 不清 → 判重全跳过 = 空转、**零报错**（§4.3）
- `Connect` / `Disconnect` 放 UI 线程 → 卡死（§六）
- 句柄缓存用普通 `Dictionary` + 并发 → `IndexOutOfRangeException`（§六）
- **重连窗口里建句柄会抛 `NullReferenceException`**，且是**从 SDK 内部抛的**，catch `ClientNotConnectedException` 漏掉它（§六）
- **断链后 `TryDeleteVariableHandle` 照样抛**（名字有 Try 不代表安全）→ 必须 catch（§六）
- **`SumCreateHandles` 一次 3000 条本机可行**，没有"200 条上限"（§三·五）
- `Type.GetType("System.int")` → null → unbox 异常（§五）
- **IEC `INT` 是 16 位**（→ C# `short`），当 `int` 读会 `Marshalling size` 报错（§五）
- **`ReadAny<byte[]>` 编组不了数组**（`Cannot marshal dotnet type 'Byte[]'`）→ 用底层 `Read(0xF005,...)` 或 `ReadValue(path, typeof(byte[]))`（§五）
- **`ReadAny(path, typeof(string))` 读 STRING 抛 `MarshalException`**（长度未知）→ 用 `ReadValue(path, typeof(string))` 或按字节读（§五）
- **STRING 写超长 → `0x705 Parameter size is incorrect`**（实测，不截断、不写坏邻居）；
  **WSTRING** 用 `Encoding.Unicode`，大小 `2n+2`；`ReadValue(path, typeof(string))` 会自动按 UTF-16 解码（§五）
- **数组可以整块写**（`WriteValue(path, T[])` / `WriteAny(h, T[])`，2026-10-23 现场实测）；
  读用 `ReadValue(path, typeof(T[]))` 或 `Read(0xF005,h,buf)`；只有 `ReadAny<byte[]>(h)` 读不行（长度未知）（§三）
- **日志打 "OK" ≠ 真写进去了**（§三 三层语义）
- **写握手**：顺序要"先写值、后置使能"，**校验通过才复位使能**；测的时候每轮换值（§三）
- Sum 建句柄**顺序假设**（不自检就张冠李戴）· 读的 `types` **别传 `typeof(object)`** · 写**整批成败**、拿不到逐条原因（§三·五）
- **Sum 创建的句柄要用 `SumReleaseHandles` 释放** —— 逐个 `DeleteVariableHandle` 会抛 `0x710` 且删不干净，后续复用旧句柄导致数据偏乐观（§三·五）
- **变量句柄池是 PLC 全局的：现场 CX5230 = 2000 / 本机 = 10000（实测）** —— 不是每连接、也不是单次 Sum 限制：
  两条连接各请求 2000 个时，第二条有效 **0**；占满后新的 `SumCreateHandles`、逐个 `CreateVariableHandle` 全部失败（返回 `0`），
  且**不报错**；读写这些 0 句柄报 `DeviceSymbolVersionInvalid`（总体仍可能 Succeeded）。
  → **批量建完必须检查 `Count(h => h == 0)`**；不建句柄的读写（按名 / IG-IO）不受此限（§三·五）
- **"连上了" ≠ "通了"**：`Connect` 成功、`IsConnected` 为 true，运行时未必在跑 —— 首次真访问才抛 `0x6`（§七）
- **"读写通" ≠ "PLC 程序在跑"**：变量读写只碰内存，**PLC 程序停着也能读写**；判"在跑"只能看**周期性变量是否在变**（§七）
- 新增的数组变量要**重连 PLC** 才会被符号表扫描到，否则写时「未找到」
- **`e.TimeStamp`（通知自带的时间戳）不要用**：**锚在 PLC 时钟**（实测稳定比本机慢 109 秒）、
  且**一批通知共用一个**（不是每变量一个）。上位机时基的项目用了只会读错。（读命令不带时间戳，只有通知带）
- **结构体实例要递归 `SubSymbols` 找**：`loader.Symbols` 只给顶层容器（GVL / MAIN 本身也是
  `Category=Struct`、`ByteSize=0`），别拿它当结构体（§三·六）
- **C# 结构体接 PLC 结构体**：`LayoutKind.Sequential` + `Pack=8`，字段偏移必须按符号表核；
  `LayoutKind.Explicit` + `byte[]` 字段会 `TypeLoadException`（§三·六）
- **整块写结构体 = 覆盖全部字节**，不是合并；旧快照写回会冲掉期间别人改的成员（§三·六）
- **`ReadAny<T[]>` / `ReadAny<byte[]>` 对结构体数组不行**（长度未知）；整块读 `byte[]` 后按 stride 切（§三·六）
- **通知句柄 ≠ 变量句柄**：两个独立命名空间；但**按符号名注册时库会占用一个变量句柄**
  （注册请求是 `0xF005 / handle`），直接给 IG/IO 注册才不占（§四·五）
- **单次 ADS 传输别 ≥ 2048kB**：AMS 路由器默认缓冲 2048kB。现场用 2,097,152B 连续读写
  **把控制器打到故障（红）/瘫痪，需现场断电重启**。大块必须分段；测试尺寸必须 ≤ 符号 `ByteSize`（§八）
- **大数组/结构体优先整变量句柄**：`CreateVariableHandle(array)` + `Read(0xF005,h,buf)`；
  本机 2448B 数组读 1.25ms/写 1.49ms，与内存同档；`ReadAny<byte[]>` 不行（长度未知）（§二）
- **非 PLC 服务器（NCI 501）也有自己的符号表/句柄**：实测 1,059 个符号，
  `Channel 2.Group 5.BlockNumber` 句柄读 4.18ms；但 G 代码行元素
  （`0x2300+通道 / 0x20000001`）没有符号，只能 IG/IO（§三·七）

## 一、最短路径

```csharp
using TwinCAT.Ads;

using AdsClient plc = new();
plc.Connect(851);                                      // 本机 PLC runtime 端口
bool v = (bool)plc.ReadValue("GVL_Test.bTest", typeof(bool))!;
plc.WriteValue("GVL_Test.bTest", true);
```

- **端口**：`851` = 解决方案里第一个 PLC 项目，`852` = 第二个…（= `.tsproj` 里的 `AmsPort`）
- **AmsNetId**：`Connect(851)` 连的是**本机 AmsNetId**（不同机器不同；本机实测 `<local-net-id>`）。
  `127.0.0.1.1.1` 是本机回环别名，实测也能连；远程用 `Connect(netId, port)`
- **符号名** = 变量在 PLC 里的全名：`GVL名.变量名` / `MAIN.变量名`；数组元素是 `GVL_Gline[1]`
- **底层一句话**：PLC 下载时带符号表 → 上位机用 SymbolLoader 上传 → 按 `InstancePath` 取；
  `CreateVariableHandle` 底层是 `0xF003`（名字→句柄），之后读写走 `0xF005`。
  👉 完整 IndexGroup 表、`%M`/`%MX` 的关系、通用 Sum（混装任意寻址）：`references/底层服务与Sum.md`

## 二、读

| 方式 | 什么时候用 |
|---|---|
| `ReadValue(symbol, type)` | 一次性取值、按钮点击 —— 单次往返 ~1.1ms（本机）/ ~3ms（跨网线）|
| **句柄**：`CreateVariableHandle` → 按 handle 读 | 同一变量反复读，省掉每次解析符号 |
| **批量**：`Sum` 一次打包 N 个 | 要轮询一批变量 —— **绝不要并发**（见 §八、§三·五） |

> ⚠ **裸用 `ReadValue` 每次约 3.4ms**（本机），是句柄的 3 倍；反复读的变量一定要建句柄。
> 大数组/结构体：**整变量建一个句柄 + `Read(0xF005, h, buf)`**；本机 2448B 结构体数组
> 实测读 1.25ms/写 1.49ms，与内存同档。`ReadAny<byte[]>` 不行（长度未知）。
> **原因（反编译核对）**：`ReadValue(path, type)` 默认走 `ValueAccessMode.SymbolicByHandle` —
> **每次 `0xF003` 建句柄 → `0xF005` 读 → 释放**；只有客户端 `_handleCache` 命中时才复用。
> 所以手动建过句柄后再调 `ReadValue` 会降到 ~1.2ms（实测）。
> ⚠ **符号名的物理地址 ≠ 请求地址**：`IAdsSymbol.IndexGroup/Offset`（如 `0x4020/405152`）
> 只是符号元数据；符号名读的请求里是 `0xF003/0xF005`，不是内存地址。
> （`ReadAny(path, type)` 才用 `0xF004` 按名读；只有 `IndexGroupOffset` 模式才直接读 0x4020。）
> **一次性按名读**可以用 `ReadAny(path, type)`（`0xF004`，一次请求、不建句柄，实测 ~1.2ms）；
> `ReadValue` 反而慢（~3.6ms），因为它走“建句柄 + 读”。

## 三、写

```csharp
plc.WriteValue("GVL_Test.bTest", true);
```

**★ 写入成功的三层语义**（最容易骗过自己的地方）：

1. **没抛异常** = 协议层成功（网络通 / 句柄有效 / 服务器执行了写）
   —— 自己封装的 `bool` 返回值、日志里的 `OK`，**通常只到这一层**
2. **回读同一变量对比** = 端到端（只对"上位机主导写"的变量有效；PLC 也在写的会被覆盖 → 误判）
3. **PLC 侧握手** = 最强（PLC 回读给你确认）

> **写应答回来的那一刻，值已经在 PLC 内存里了**，不用等 PLC 任务周期。
> 任务周期影响的是「**PLC 程序什么时候用它**」，不是「值什么时候到」—— 两件事。

**★ 第 3 层（PLC 侧握手）具体怎么做**：① 先写**数据值** ② 再置**写使能**（**顺序不能反**，反了 PLC 读到旧值）
③ 轮询读 PLC 的**回读变量** ④ 与期望值**按容差**比较（浮点有精度误差）⑤ **校验通过才复位写使能**；超时则告警、使能保持不动。
⚠ 测握手**每轮换一个值** —— 固定值会读到上一轮的旧值，造成"假通过"。

**数组**：**可以整块读写**——按符号名 `WriteValue(path, T[])`、按句柄 `WriteAny(h, T[])` 都实测可行
（2026-10-23 现场，TwinCAT.Ads 7.0.172；读用 `ReadValue(path, typeof(T[]))`，或 `Read(0xF005, h, buf)`）。
`ReadAny<byte[]>(h)` **读**不行（长度未知）；逐元素写当然也可以。

## 三·五、批量（Sum）三件套 —— 一次请求搞定 N 个变量

| 干什么 | API | 结果 |
|---|---|---|
| 批量**建句柄** | `new SumCreateHandles(client, paths)` → `cmd.CreateHandles()` | `res.Handles`（与 `paths` **同序**）|
| 批量**读** | `new SumHandleReadAnyType(client, handles, types, SumFallbackMode.All)` → `cmd.ReadAsync(ct)` | `res.Values` |
| 批量**写** | `new SumHandleWriteAnyType(client, handles, types)` → `cmd.WriteAsync(values, ct)` | 不抛即受理 |

命名空间 `TwinCAT.Ads.SumCommand`。实测：`SumCreateHandles` 300 个真实变量 **21ms**
（逐个 912ms / 均摊 3.04ms 一个）→ **快 43 倍**。

- **★ 变量句柄池是 PLC 全局的：现场 CX5230 = 2000 / 本机 = 10000（实测）**：
  不是每连接、也不是单次 Sum 子命令数限制。实测：两条连接各请求 2000 个 → 第二条有效 **0**；
  先建满且不释放，再逐个 `CreateVariableHandle` → 全部失败。
  `SumCreateHandles` **不抛异常**，失败位置直接填 `0`；读写这些 0 句柄报
  `DeviceSymbolVersionInvalid`，而**总体结果仍可能返回 Succeeded**。
  → 建完必须检查 `Count(h => h == 0)`；超过上限要**先释放再建**；不建句柄的读写（按名 / IG-IO）不受此限。
  （⚠ 官方文档里没找到明文，这是实测值；换控制器/版本请复测。）

- **★ 顺序假设**：官方语义是 `Handles[i]` 对应 `paths[i]`。顺序若不符 → 句柄**张冠李戴** →
  读写错变量（生产里就是"机器乱动"）。**建完要自检**：拿句柄读到的值和按符号名读到的值比对。
  （⚠ 对**实时在变**的变量不能比相等 —— 两次读必然不同会误报；要比"两组值的**区间是否重叠**"。）
- **★ `SumSymbolRead` 默认 `ValueByName` 很慢**：每个子命令按符号名访问。实测现场 2000 个：
  `ValueByName` 18.7ms vs `IndexGroupIndexOffset` 5.2ms（内存法 3.4ms）。
  批量时**显式指定 `SumAccessMode.IndexGroupIndexOffset`**（用符号对象里现成的 IG/IO）；
  **此时本质上是“符号表取地址 + 内存法读写”，不再是按名访问**。
  编组本身成本很小（`Read2` vs `ReadRaw` 几乎一样）。
- **★ 读的类型数组**：`types` 要 `Type[]`，**认不出的类型给 `null`、不要给 `typeof(object)`** ——
  object 编组不了，实测抛 `Cannot marshal dotnet type 'Object' because its marshal size is unknown!`。
  排除掉的那些由调用方逐个补读。
  （实测：**STRING 能走 Sum 读**，不用额外给长度；标量、数组元素也都能走。）
- **★ Sum 符号写的长度/编组全自动**（2026-10-22 现场实测）：LEN 取自 `ISymbol.ByteSize`，
  `Write(object[] values)` 按符号类型自动编组。
  - **字符串数组直接给 `string[]`**（不用逐元素、不用给长度）
  - **结构体套结构体给逐字节匹配的 C# 结构体**（`Sequential` + `Pack=8`）即可整块写；实测 88B 结构体
    （BOOL + 内层 INT/LREAL/STRING(20) + STRING(10)[3] + INT[3]）逐成员读回全对
  - ⚠ **C# 结构体里的字符串数组不能声明成 `string[]`** → 用
    `[MarshalAs(UnmanagedType.ByValArray, SizeConst = …)] byte[]`（`STRING(n)` = n+1 字节/元素）
  - ⚠ `SumSymbolRead` 读结构体返回 **`byte[]`**（原始字节），不会自动解成结构体
  - 测试程序未随技能提供；按本节布局自行建立最小测试时必须使用非生产目标。
- **★ 写与逐个写的语义不同**：Sum 是**一次请求**；`Write()` 不抛即受理，**逐条结果要用
  `TryWrite(out subErrors)` 拿**（实测 5000 个里 3000 个报 `DeviceSymbolVersionInvalid`，
  总体 `TryWrite` 返回仍可能是 `Succeeded`）。→ 批量写后必须看 `subErrors` / 回读验证，
  不能只看总体结果。
- **★ 一次能建多少条？本机没有条数/字符上限**（2026-10-30 本机实测，`MAIN.arrSum[i]`）：
  200 / 250 / 500 / 1000 / 2000 / **3000 条全部 `Succeeded=True`、零句柄 0 个**；
  3000 条路径合计 **49,893 字符**，耗时 5ms（首批 21ms 含预热）。
  → 真正的约束是**上面那条句柄池上限**，不是报文大小。
  ⚠ 现场链路（跨网线 / AMS 路由）**没测**，别把本机结论外推过去。
- **兜底**：Sum 建句柄失败 / 数量不符时，要能**回退逐个建**；仍有 `Handle == 0` 的也逐个补。

## 三·六、结构体（UDT）

**一句话**：结构体在 ADS 里就是**一段连续字节 + 一张成员地址表**。寻址方式和数组没有区别，
区别只在**偏移怎么算**——数组等距，结构体按成员表 + 对齐填充（见 §八 内存对比）。

**怎么找结构体实例**：`loader.Symbols` 只返回顶层容器；要递归 `SubSymbols`，
按 `Category == Struct && ByteSize > 0` 过滤。

**能做什么**（2026-10-18 本机实测，`MAIN.ST1 : stAxisInput`，816 字节）：

| 操作 | 结果 |
|---|---|
| `CreateVariableHandle("MAIN.ST1")` | ✅ `0x60000010` |
| `Read(0xF005, h, buf)` | ✅ 816 字节 |
| `ReadAny<byte[]>(h)` | ❌ `Cannot marshal dotnet type 'Byte[]'`（长度未知）|
| `ReadValue(path, typeof(byte[]))` | ✅ 816 字节 |
| `IValueSymbol.ReadValue()`（结构体符号） | ✅ `byte[]` 816 |
| `ReadAny<StAxisInput>(h)` | ✅ 需要 C# 布局逐字节匹配 |
| `WriteAny<StAxisInput>(h, v)` | ✅ 整块写 = **覆盖全部字节** |
| `AddDeviceNotificationEx(..., typeof(StAxisInput))` | ✅ 注册即回一条当前值，之后每次变化推整块 |

**C# 布局三件事**：

1. `LayoutKind.Sequential` + `Pack = 8`（和 TwinCAT 自然对齐一致）。**不要 `Pack=1`**。
2. **不能用 `LayoutKind.Explicit` + `byte[]` 字段** → 运行时 `TypeLoadException`。
3. 偏移**按符号表里的成员 `IndexOffset` 核**，不能按成员大小累加推。实测：
   成员合计 **798**，结构体 **816**，中间 18 字节填充（3 + 5 + 7 + 1 + 2）。
   `BOOL` = 1 字节；`STRING(n)` = n+1；`WSTRING(n)` = 2n+2。

> 完整的 C# 结构体定义（含 `alignPad` 那一行）在 `references/结构体.md` 的「四、C# 结构体怎么接」。

**★ 整块写是覆盖**：拿旧快照整块写回，会把期间 PLC（或另一个写者）改过的成员一起冲掉。
→ 结构体里有 PLC 也在写的成员时，**写前立刻重读合并**，或只逐成员写。

**数组套结构体**：步长 = 结构体 `ByteSize`（含填充），元素偏移 = 基址 + (i−1) × `ByteSize`；
元素可以单独建句柄；`ReadAny<T[]>` 不行，按 stride 切或逐元素读。

**时延**：**整块读 / 写的开销和普通标量完全一致** —— 本机实测 8B / 816B / 2448B
的句柄读写都在 **1.1–1.5ms**（差值是噪声），因为时延由**一次往返回路**决定，不是由类型决定。
唯一的例外是**请求次数**：逐成员读 36 个 = **47.1ms/轮**（1.309ms/成员），
整块读 1 次 = **1.29ms** —— 36 倍差在“36 次往返”，换 36 个普通变量也一样。
→ 能整块读就整块读；成员多且要频繁读时用 Sum 批量（§三·五）。

👉 完整实测（36 个成员偏移表、填充位置、三种读法、Ex 订阅、数组套结构体、两套句柄）：
`references/结构体.md`
👉 探针（本 skill 自带）：`scripts/ads-struct-probe/` ——
`dotnet run -- MAIN.ST1 [--notify|--write|--mem|--arr]`

## 三·七、非 PLC 服务器（NCI 501）

- 501 有自己的符号表：实测 **1,059 个符号**，`Channel 2.Group 5.BlockNumber`
  （IG=0x3105 / IO=13）**句柄读 4.18ms** 成功
- `ReadDeviceInfo` 在 501 返回 `0x701`（服务不支持），但符号上传/句柄正常
- G 代码行元素 `0x2300+通道 / 0x20000001` **没有符号名**
  （搜 `Line / Text / BlockSearch` = 0），只能 IG/IO
- 上位机读 G 代码行：优先读 PLC 副本 `GVL.GVL_sGCodeLine`；或直连 501 用原始 IG/IO
  （通道号可从 `MAIN.stItpToPlc.ChnId` 读）

## 四、通知（让 PLC 主动推）

### 4.1 注册方法 ↔ 触发事件 **必须配对**

| 注册用 | 触发的事件 |
|---|---|
| `AddDeviceNotification(...)`（**非 Ex**） | legacy `AdsNotification` |
| `AddDeviceNotificationEx(..., typeof(T))` | `AdsNotificationEx` |

**注册非 Ex、却订阅 Ex 事件 → 6 秒收 0 包，一声不响；反过来也一样。**
**推荐 Ex**：值已编组好（`e.Value` 直接强转），不用自己解码生字节。

**⚠ Ex 注册 STRING 必须走带 `int[] args` 的重载，`args[0]` = 字符数**（`STRING(n)` 的 n）。
漏了抛 `ArgumentNullException(Parameter 'args')`。**标量不需要 args。**

### 4.2 ★ 采样相位律（本 skill 最重要的一条）

**PLC 侧的通知不是事件驱动，是"每 `cycleTime` 检查一次值变没变"** —— 它在**采样**。

设信号全周期 T：

| 关系 | 结果 |
|---|---|
| `cycleTime ≡ 0 (mod T)` | 采样永远同相位 → **一个包都不发**（静默失明） |
| `cycleTime ≡ T/2` | 每次检查都翻相 → 每个检查周期都发（最大量） |
| 其他 | 相位漂移 → **慢拍**（发送周期 = 跨越周期数 × cycleTime） |

**实测**（信号 = 100ms 周期方波，3 秒内收到几个包）：

| cycleTime | 收到 | 实测间隔 |
|---|---|---|
| **200 / 100** | **1** | —（失明）|
| 250 | 12 | 250ms |
| **210** | 4 | **1050ms**（慢拍）|
| 55 | 50 | 56ms |
| 33 | 64 | 35ms |
| **0** | **65** | **50ms（每个跳变都收到）** |

- **与 PLC 任务周期无关**（任务 2ms 或 10ms 复测，上表基本不变）—— 采样间隔是**时间基准**
- **`cycleTime = 0` = 每个跳变都发** → 要即时反馈的关键信号用它
  - 实测（2026-10-21，本机 AdsTest，BOOL 每任务周期翻转）：**2 / 1 / 0.5 / 0.25ms 任务**
    3 秒分别收 **1501 / 3006 / 6007 / 12009 条**，平均 **2.00 / 0.998 / 0.499 / 0.250ms**
    —— 四档都是**全部值变化，一条不丢**（0.5ms 需 BaseTime ≤0.5ms；0.25ms 需 **BaseTime 250µs + Cycle ticks=1**）
  - **★ 平均间隔 ≠ 交付延迟**：回调是**成批**到的 —— 攒批节拍约 **1ms**，不随任务周期变：
    0.5ms 任务每批约 2 条、0.25ms 任务每批约 4 条；到达间隔 p50 ≈0.01ms、p99 ≈1.7ms。
    **每条通知自带的 PLC 时间戳仍严格按任务周期递增，值不缺不错。**
    换原生 `TcAdsDll` 复测，分布一模一样 → 攒批在**客户端 API 以下**的 ADS 链路
    （PLC 通知发送 / AMS 路由，具体哪一级未定位）；**不是 .NET 库、也不是应用代码**。
    → 要**节拍可控**（不攒批）用 Sum 轮询，但**它并不更快**：每轮至少一次 RTT
    （现场 ~3ms、本机 ~1.1ms）。通知给的是“值新鲜”，不是“准点到手”。
    明细见 `references/性能实测.md`（§8.7 通知交付节奏）
  - **★ 没有“一变就单独发一个包”的开关**（2026-10-21 查文档 + 实测）：
    能开的只有 `OnChange` + `cycleTime=0` + `maxDelay=0`（= `NotificationSettings.ImmediatelyOnChange`）；
    `OnChangeInContext` 要传 ContextMask（PLC = TASKINFOARRAY 索引−1），**变量所在任务就是 0 时与默认无差别**（实测分布一致）；
    `ClientOnChange` / `ClientCycle` 被 PLC 拒绝（`0x701 Service is not supported`）；
    客户端 `timeBeginPeriod(15)` 也不改变分布 → 不是客户端调度。
    ADS 协议的 Device Notification 本身就是「一个流包带多个 StampHeader」的结构（每 Stamp 一个时间戳），**没有一条一包的选项**。
  - 现场“~3ms”是请求-响应往返；通知是 PLC 主动推，**不受这个往返限制**
- **★ `cycleTime=0` ≠ “每次赋值都通知”**：PLC 在**任务周期结束时**比较一次值。
  实测（现场 CX5230，0.5ms 任务）：同一周期内让变量 `+ → − → +`（净变化为零），
  3 秒收到 **0 条**（注册时那 1 条除外），而中间值确实出现过（另一变量捕获到 True）。
  → **脉冲窄于任务周期的变化对 ADS 完全不可见**；要抓短脉冲只能在 PLC 侧锁存成 flag。
  且**通知时间戳是任务周期时间，不是变量写入时刻**：同周期内程序首、尾两行各翻一次的两个变量，
  时间戳完全一致（现场实测 6005/6015 精确匹配）；PLC 侧 `F_GetSystemTime` 在一周期内也不刷新（首尾差恒 0）。
  **外部 I/O 同理**：输入映像每个任务周期只有一次快照，一个周期内变两次中间的也丢。
  要抓窄脉冲/边沿时刻，只能靠硬件锁存/时间戳端子（EL125x、EL51xx Latch）或过采样，
  把边沿时间带进 PLC 变量，再通过 ADS 读——**通知只能搬运 PLC 变量，不能恢复物理边沿时刻**。
  → **事件计数用单调递增变量（counter），不要用翻转变量**：实测每 60ms `K:=K+1e-9`
  通知 51 条/3s、值严格递增、一条不丢；而“翻出去再翻回来”的变量一条都没有。
- **⚠ 推论**：任何"变化周期能整除你 cycleTime"的**周期性**信号，你会**系统性看不到**。
  **轮询一样中招**（轮询同样在采样）—— 区别只是轮询周期由你挑、通知周期注册时定死。
  **→ 判链路死活的方波（心跳），必须主动读，别用通知。**

### 4.3 通知会「半死」：读 / 写全通、订阅死

**电脑休眠、或 PLC 侧重新下载程序** → ADS Server 让通知句柄失效 → 发 **0 长度通知** + 触发
**`AdsClient.AdsNotificationsInvalidated`**。

- **连接可能没断** → 读、写、心跳全正常，**只有通知永远不来**，**零告警**
- **判据**：日志里一条 PLC→上位机的记录都没有（只剩自己写出去的）+ **PLC CPU 掉几个点**（通知那点固定开销没了）
- **修法**：订阅 `AdsNotificationsInvalidated` → **先清掉本地注册缓存** → 重新注册
  （⚠ 不清缓存会被"已注册"判重全跳过 = 白做）
  **⚠ 断连重连那条路径同样要先清** —— 重连后重注册若不清缓存，也是**全跳过、也是零报错**（实测踩到）
- **兜底**：光靠这个事件不够（PLC 也可能**静默**作废、不给事件）→ 加**探针**：
  把一个已知周期的信号注册成 `cycleTime=0` 的通知，**读还通、但超阈值收不到包** → 判订阅死 → 重注册
  （⚠ 重注册会连探针一起清掉，**记得补挂**，否则自锁）

### 4.4 回调里的限制

官方原文：**别在回调里做耗时操作或发 ADS 命令**。通知数量也有上限。

### 4.5 两套句柄：变量句柄 ≠ 通知句柄

| | 变量句柄 | 通知句柄 |
|---|---|---|
| 实测值 | `0x60000010`（结构体）、`0x61800027`（数组元素） | `0x2` / `0x3` / `0x4` |
| 用途 | `0xF005` 读写 | 标识订阅；回调 `e.Handle`、删除时用 |
| 删除 | `DeleteVariableHandle` | `DeleteDeviceNotification` |
| 失效 | 跟连接绑定 | 休眠 / PLC 重下程序**单独作废**（§4.3）|

- **通知注册的请求 = IG/IO + 长度 + 采样设置**，不是“通知句柄”本身。
  实测直接 `AddDeviceNotificationEx(0x4020, 405152, settings, ..., typeof(T))` 也能订阅。
- ⚠ **按符号名注册时，库会先建/复用变量句柄，再以 `0xF005 / handle` 注册**（反编译确认）：
  `TryAddDeviceNotificationEx(path,...)` → `_handleCache.CreateVariableHandle(path)`（`0xF003`）
  → `TryAddDeviceNotificationEx(0xF005, handle, ...)`。
  所以这条路径下**通知与那个变量句柄同寿**；PLC 重下程序让句柄失效时，通知也会一起失效。
  （直接给 IG/IO 注册则不占变量句柄。）
- 回调里的 `e.Handle` 是**通知句柄**，不要拿去当变量句柄用。

## 五、类型与长度

- **★ IEC 类型 ≠ C# 类型**：PLC 的 `INT` 是 **16 位** → C# `short`；`DINT` 才是 32 位 → C# `int`。
  拿 C# `int` 去读 PLC `INT` 会抛 **`Marshalling size is '4' bytes, but read data is '2' bytes`**（实测踩到）。
  同类：`REAL`→`float`、`LREAL`→`double`、`WORD`→`ushort`、`DWORD`→`uint`。
- **C# 关键字 ≠ CLR 类型名**：`Type.GetType("System.int")` **返回 null**（正确是 `System.Int32`）。
  凡按类型名转 CLR 类型，**必须把关键字映射到 CLR 名**（`short→Int16` / `int→Int32` / `bool→Boolean` / …），
  否则回退成原类型 → 写的时候 unbox 强转抛 `InvalidCastException`。
- **STRING 的长度** = `BitSize/8 - 1`（**字符数**）。读、写、注册通知**共用同一把尺子**最省事。
- **★ 数组不能用 `ReadAny<T[]>`**：`ReadAny<byte[]>(handle)` 抛
  **`Cannot marshal dotnet type 'Byte[]' because its marshal size is unknown!`**（库不知道长度）。
  读数组两条路：① `client.Read(0xF005, handle, buf)` —— 自己给缓冲区；
  ② `client.ReadValue(path, typeof(byte[]))` —— 按符号名读时库能从符号信息拿到长度。
- **★ `ReadAny(path, typeof(string))` 读 STRING 也不行**（2026-10-19 现场实测）：
  抛 `Cannot marshal dotnet type 'String' because its marshal size is unknown!`。
  用 `ReadValue(path, typeof(string))`（库从符号信息取长度），或 `ReadAsResult` 给长度后按字节解码。

## 六、线程模型

- **★ 碰 ADS 资源的，一律走 UI 线程** —— 踩出来的架构约定（后台那些全包进一个 `InvokeOnUi`）
- **`Connect` / `Disconnect` / `DeleteDeviceNotification` / `DeleteVariableHandle` 都是同步阻塞** ——
  **别放 UI 线程**（PLC 下线时 `Connect` 超时数秒 → 界面卡死）。包进 `Task.Run`
- **⚠ 从后台回 UI 要用 `Dispatcher.InvokeAsync`（异步），不要 `Invoke`（同步）** ——
  后台 `Invoke` 等 UI、UI 又在 await 那个 Task → **死锁**
- **句柄缓存必须线程安全**：`CreateVariableHandle` 并发调会**破坏普通 `Dictionary`**（内部 bucket），
  症状是**通知大量注册失败 + `IndexOutOfRangeException`**，且报错点（通知注册）离真正的破坏点（建句柄）很远。
  → 换成 `ConcurrentDictionary` 即可，**并发建句柄本身不用串行化**（下条实测）。
- **跨线程共享的容器用"整体替换"**（建好新的 → 一把换引用），比换 `ConcurrentDictionary` 更彻底：
  后者照样会露出 `Clear()` 之后的空窗口
- **★ 别想着"支持并发"，要想着"收敛成一个入口"**。我们的工程本来就**不需要**并发建句柄：
  原来 `Parallel` 建 1024 个已经把字典搞坏过一次，后来改成连接时一次 Sum、单线程，问题就没了。
  **批量建（`SumCreateHandles`）已经把"建得多"解决了**；真正的并发来源是散在各处的**单个懒建**
  （写变量 / 读变量 / 通知注册 / 写 G 代码都会 `Handle == 0` 就现建），它们跑在不同线程上。
  → **能一次 Sum 建完的，就别留懒建**；留下的那些兜底要么删、要么统一进同一把锁。
  同日实测：8 线程 × 200 次并发建 **0 异常**（比串行还快，267ms vs 1642ms）——
  说明 `IndexOutOfRangeException` 来自**应用自己的普通 `Dictionary`**，不是库；但这不等于"可以随便并发"，
  因为真正会炸的是下面那条的重连窗口。
- **★ 真正致命的竞态在「重连窗口」，抛的是 `NullReferenceException`**（同日实测）：
  一线程不停 `CreateVariableHandle`、另一线程每 200ms 连断交替，30 秒三跑分别抛 **7 / 23 / 3 次**；
  栈顶固定是 `AdsClient.TryCreateVariableHandle` → `tryReadEncodings`
  → `SymbolLoaderFactory.TryReadSymbolUploadInfo` → `ReadAsResult`（**SDK 内部抛，不是你代码抛**）。
  两个常见写法都挡不住：**只 catch `ClientNotConnectedException` 会漏**；
  **"先查 `IsConnected` 再调用"也挡不住**（检查与发包之间连接照样可能被断）。
  → 把 `Disconnect` 和 `CreateVariableHandle` 放进**同一把锁**里互斥，是唯一能关掉这个窗口的做法
  （本机只验证了问题存在，**修复方案未实测**）。
- **★ 断链后删句柄，两个方法都抛**（同日实测）：
  `TryDeleteVariableHandle` 与 `DeleteVariableHandle` **都会抛 `ClientNotConnectedException`**（不是 NRE）；
  `Dispose` 之后抛 `ObjectDisposedException`。
  → 「连接还在才逐个删、删不掉也要清本地表」的写法是对的，但**必须 catch**——`Try` 开头不保证不抛。

## 七、错误码与失败语义

| 码 | 含义 |
|---|---|
| `0x745` | **Timeout** —— PLC 没开机 |
| `0x6` | **Port not found** —— TwinCAT 没跑 |
| `0x710` | **Symbol not found** —— PLC 里没这个变量 |
| `0x12` | **Port is disabled** —— TwinCAT 服务在，但 PLC 端口没起（程序没下载 / 没 Run）|

- **★ "连上了" ≠ "通了"**：**TwinCAT 运行时没跑**时，`Connect(851)` **照样成功**
  （`ConnectionStateChanged` 事件照发、`IsConnected` 为 true），**直到第一次真正访问变量**才抛 `0x6`。
- **★ "读写通" ≠ "PLC 程序在跑"**：变量读写只碰内存，**PLC 程序停着也照样读得到、写得进**。
  判"PLC 真在跑"只能靠**周期性变量是否在变**。
- **"静默失败"是常见病**：初始化时预设 `IsConnected = true`，任何"失败路径忘了改它"都会变成
  **"系统说一切正常、实际有变量不通"**。**查这类问题先问：失败路径有没有把状态改对？**
- 官方有**不抛异常的变体**：`TryReadValue` → `AdsErrorCode`、`ReadAnyAsResult` / `WriteAnyAsResult` → `ResultValue<T>`。
  比全程 try/catch 干净，值得优先用。

## 八、性能数字（选型用）

- **单次往返**：本机 ~1.1ms / 跨网线 ~3ms（1B–1KB 内与数据量无关）
- **裸符号名 ~3.4ms vs 句柄 ~1.2ms**（本机）—— 解析成本 ~2.4ms/次，句柄只付一次
- **读 / 写都有同样的差**（现场，2026-10-21，200 次平均）：
  `ReadValue` **~9.6ms/次**、`WriteValue` **~9.5ms/次**（每次 `0xF003` 建句柄 + `0xF005`）；
  `ReadAny(path)` ~3.4ms、句柄读 ~2.5ms、内存读 ~2.8ms；句柄写 ~2.8ms、内存写 ~3.0ms。
  反复读写用句柄 / 内存，别每次 `ReadValue` / `WriteValue`
- **轮询要用批量（Sum），绝不要并发**：并发读 2500 请求/秒、上位机 CPU 33.5%；
  批量读 19 请求/秒、2.7%；**通知最省**（0.3 请求/秒、0.2%）
- **数据量**：≤100KB 读写都平稳；>100KB **写**开始波动上升（读基本不变）
- **内存寻址 vs 句柄：没有可测差异** —— "内存最快"只对比裸符号名时成立
- **本地连接也走 TCP 环回**（`127.0.0.1:48898`），所以本机 1.2ms 含完整协议栈开销
- **失败路径每次都要付一次完整往返**（2026-10-30 本机实测）：建不存在的变量
  **1.115ms/次**（错误码 `0x710`）；换成 200 个**各不相同**的失败路径 **1.015ms/次**；
  同样路径**已缓存**时 **0.000ms/次**。
  → 符号可能缺失、页面又高频刷新时，重试成本**线性叠加**（现场按 ~3ms 往返算是三倍）。
  本机没测过"失败冷却"这类优化，别照抄别人的数字。
- **结构体整块读写与标量同档**（2026-10-18 本机实测）：8B / 816B / 2448B 的句柄读
  1.25–1.35ms、写 1.32–1.49ms，都在噪声内；**逐成员读 = N 次往返**
  （36 个成员 47.1ms/轮）—— 差异来自请求次数，不是类型
- **现场跨网线（2026-10-19）**：单次句柄读 **2.8ms**；**单连接顺序轮询上限 ~320/s**
  （= `1 / RTT`，再提高目标速率只会空等）；**Sum 批量受句柄上限约束**（现场 2000 / 本机 10000）；
  N≤2000 时变量吞吐随 N 增长，**旧 N=5000/7500 的吞吐数字因超上限已作废**（§三·五、references）；
  **大块单变量读写不对称**：读 ~24 MB/s（≈190 Mbit/s）、写 ~48–50 MB/s（≈400 Mbit/s），
  2MB 读 ~89ms、写 ~42ms；**2,000,000B 实测读 ~105ms / 写 ~55ms**（首次写 147ms 是冷启动抖动），远低于本机回环的 ~1 GB/s。
  **边界**：2,097,152B（正好 2MB）会打停控制器（现场踩过，需断电重启）；2,000,000B 可过但已贴边
  → 生产单次 ≤1MB、大块分段。详见 `references/性能实测.md` 第八节
- **通知负载（现场 CX5230，测试程序空载，2026-10-21）**：到 **6% CPU** 的通知数：
  **100ms 采样 ≈ 4600、50ms ≈ 4200、10ms ≈ 3200**（10ms 任务周期）；100ms 下 10000 个 ≈ 10%、
  12000 个 ≈ 25%（非线性）。**采样频率降 10 倍只让拐点提前 ~30%**。
  **任务周期影响更大**：同样 10ms 采样，**2ms 任务 → 仅 1000 个就到 6%**（任务快 5 倍，拐点约 1/3）。
  **`cycleTime=0`（每个变化都发）更重**：2ms 任务下约 **500~600 个就到 6~7%**，
  ≈ 10ms 采样的 1.5~2 倍。→ 通知开销主要在**每个任务周期的处理**，不只是 cycleTime 采样。
  弯管任务周期 2ms、163 个通知（含不少 `cycleTime=0`）≈ 1.5~2%，仍在安全区
- **Sum 轮询负载（现场 CX5230，2ms 任务，2026-10-21）**：**100 个变量、10ms 周期**
  （实际 ~91 次/s），CPU 从 2% → **3%（+1%）** —— 轮询比大量通知省得多；
  同样规模的 1000 个通知（10ms 采样、2ms 任务）已经到 6%
- **生产项目样本（163 变量 / 50ms）**：逐变量轮询 = **3,260 请求/s，会撞 ~320/s 上限**；
  **Sum 批量 = 20 请求/s，余量 ~16 倍**；正常数据 KB 级，远离 2MB 路由器上限。
  → 峰值数字远超正常需求，测试价值在“确认余量 + 找失败边界”（§八末尾）

**分档建议**：大量低频信号 → 通知；少量关键反馈 → cycleTime 降到 0/20；
**真要 ms 级新鲜度 → 通知 + `cycleTime=0`**（采样精度由任务周期决定，交付另有 ~1ms 攒批）；
**轮询受 RTT 限制（现场 ~3ms），只适合周期快照，不适合追低延迟**。

👉 全部实测表（跨网线/本机、156 变量轮询对比、1KB–2MB 曲线、跨网线估算、测量陷阱）：
`references/性能实测.md`

**监控 PLC 实时核 CPU（安全测试必备）**：

- PLC 程序里加 `TC_CpuUsage`（`Tc2_Utilities` 库）→ `USAGE : UDINT`（%），映射到 GVL，上位机 ADS 读
- **这就是 TwinCAT 实时设置里显示的那个 CPU 使用率**
- 采样周期建议 **100ms**（1s 太慢，做安全墙拦不住）
- MDP 的 `FB_MDP_CPU_Read`（`Tc2_MDP`）读的是**非实时核**（实测 `iCPUusage=0`，频率能读）
- 安全测试：分批注册 + 每批读 CPU + 阈值墙（如 6%），到墙立即停止并注销

## 九、相关

- **官方手册**：`<beckhoff-library-root>\automation\twincat3\TC3_ADS_NET_V7_EN.pdf`
- 本技能保留的可运行探针位于 `scripts/`；作者的其他独立 demo 和 bench 工程未随技能提供。
- **句柄生命周期 / 并发 / Sum 规模探针**（本 skill 自带）：`scripts\ads-handle-test\`
  ```
  dotnet run -c Release -- <t1|t2|t3|t4|t6|all> (--local | --netId=x.x.x.x.x.x) [--port=851]
    t1  断链后删句柄（TryDelete / Delete / Dispose 后各抛什么）
    t2  并发建句柄 vs 串行（各 1600 次），建完再注册 50 个通知
    t3  连断重建 30 秒窗口（狂建句柄 + 每 200ms 连断），抓 NRE 栈
    t4  SumCreateHandles 规模：200/250/500/1000/2000/3000 条
    t6  失败路径 vs 缓存命中的单次成本
  ```
  必须显式选择本机路由 `--local` 或指定 `--netId`；依赖目标 PLC 的 `MAIN.arrSum[1..3000]`，跑完会自动释放句柄。

- **现场体检工具**（本 skill 自带）：`scripts\ads-speedtest\`
  ```
  dotnet run -- <mode> [参数] [--netId=x.x.x.x.x.x] [--port=851]

    info                                   设备信息（名称 / 版本 / ADS 状态）
    read    <symbol> [type]                只读一次（绝不写）
    write   <symbol> <value>               写一次并回读
    check   <symbol> [type]                连通性 + 读 + 写 + 回读 + 还原
    speed   <symbol> [type] [n]            符号名 vs 句柄（默认 n=200；先符号后句柄）
    lat     <symbol> [n] [--members] [--write]
                                           句柄 / 内存 / 符号名 时延；--members 对比逐成员 vs 整块
    pollload <symbol> [秒/档] [速率档...]   按速率档逐步加载（默认 10,20,50,100,200 /s）
                                           每档后读 PLC 任务统计；一有下降就停
    bench   <symbol> [size] [n] [--write]  数据量（默认只读；写要 --write）
    sum     <basePath> [n]                 批量 Sum（basePath[1..n]）
    mixed   <symbol> <ig> <io> <len>       混装内存 + 句柄（同一块数据对比）
    notify  <symbol> [seconds]             通知（Ex；结构体自动 byte[] + 长度）
    struct  <symbol> [--notify|--write|--mem|--arr]  结构体探针
    symbols [filter]                       列符号表（含子符号）
  ```
  结构体逻辑与 `scripts\ads-struct-probe\` 共用 `scripts\ads-shared\StructProbe.cs`；
  里面硬编码了 `MAIN.ST1 : stAxisInput` 的 C# 布局，换工程要改。
  `--arr` 默认找 `MAIN.arrSt`，可用 `--arr-symbol=` 覆盖；`--mem` 的数组对照默认 `GVL_AdsDemo.aInts`，
  可用 `--array-symbol=` 覆盖。
> **本 skill 的配套脚本都在 `scripts/`** —— 为 ADS 写的测试/工具放这儿，别散在项目目录。
