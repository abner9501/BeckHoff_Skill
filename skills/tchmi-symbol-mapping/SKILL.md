---
name: tchmi-symbol-mapping
description: 创建或修复 TwinCAT HMI（TE2000/TF2000）的 PLC/ADS 符号映射（Map Symbol），核对 Mapped Symbols、TMC 离线符号及运行读取。新增或改绑 PLC 变量、将 HMI 内部变量改为 PLC 联动，或出现变量未 map、映射未生效时使用。不负责 EtherCAT I/O 链接或普通上位机 ADS 编程。
---

# TwinCAT HMI PLC 符号映射

新增 PLC 变量并在控件中写 `%s%...%/s%` 之后，还要完成 HMI Server 的 Map Symbol。PLC 编译成功、控件可显示、配置文件里存在 `MAPPING`，分别只证明对应层次，不能据此宣称运行中的映射可用。

## TE2000 / TF2000 必须完成 Map

TwinCAT HMI 的 PLC 联动必须通过有效的服务器符号映射。新增 PLC 变量、改绑变量或将原有 HMI 内部变量改为 PLC 联动时，都要将 Map Symbol 及运行核对作为任务的一部分。普通 HMI 内部变量本身不需要 ADS map，但不能直接接收 PLC 赋值。

沿用已有服务器别名时，也要核实它的真实来源。例如 `AccumulatorInUse` 可以继续作为控件绑定名，但只有映射到实际 PLC BOOL 后才会与 PLC 联动。运行服务若仍显示 `DOMAIN=TcHmiSrv`、`MAPPING` 为空且 `USEMAPPING=false`，说明它仍是内部变量；即使读到 TRUE，也不能证明 PLC 联动成功。

文件配置完成后继续核对运行服务已加载的映射和经别名的读取。尚未发布、在线 PLC 缺少符号或权限不足时，明确报告对应缺口；不能仅以“工程已 map，发布后生效”作为已完成在线联动的结论。具体持久配置及核对方式见下方参考。

## 确认工程和目标

- 先确认当前打开的 HMI 解决方案和 PLC 工程；同一目录的日期副本可能与正在使用的工程不同。必要时只读查看 XAE 的 `Solution.FullName` 和 HMI Server 进程的 `--storageDir`，不要只按文件夹日期猜测。
- 确认 HMI 的 ADS runtime 名称、NetId、PLC 端口及当前配置。示例的 `PLC1` 是 runtime 别名，不能直接当作目标 NetId。保留既有路由、用户组和配方。
- 核实变量已声明、编译且出现在最新 TMC。在线 PLC 还须实际包含该符号；本地编译不会更新在线 PLC。未下载新符号时可完成离线映射，并明确报告尚未完成在线验证，不能因此自行下载或重启设备。

## 在 XAE 中执行 Map Symbol

1. 打开 **TwinCAT HMI Configuration**，进入 **Server Symbols → All Symbols**。
2. 展开 **ADS → 实际 runtime（例如 PLC1）→ GVL（例如 Application）**，找到新增变量。若看不到，先检查在线 PLC 是否包含新符号、连接目标、符号过滤及必要的符号刷新；不要创建一个同名常量来代替 PLC 符号。
3. 选择变量，右键 **Map Symbol** 或点击同名工具栏按钮。
4. 在 **Map new Symbol** 对话框中指定服务器符号名，沿用当前项目命名方式。例如 PLC 路径 `Application.bEnableTempHeatTime` 对应服务器符号 `PLC1.Application.bEnableTempHeatTime`。同名映射已存在时先比较来源和类型，不要再建第二个别名。
5. 在 **Mapped Symbols** 中确认新增项的来源、类型、在线/离线状态和适当的读写权限；开启 **Show live values for mapped symbols**，核对读取结果。
6. 控件绑定服务器符号名。例如 Checkbox 的 `StateSymbol` 为 `%s%PLC1.Application.bEnableTempHeatTime%/s%`。不要把 ADS 内部路径 `PLC1::Application::bEnableTempHeatTime` 原样用作这个服务器别名。
7. 保存当前工程及服务器配置。保留已有设计器未保存的编辑；外部修改文件后，应核对设计器和运行服务是否已刷新，避免旧缓冲区覆盖修改。

**Map 不等于加入配方。** 是否加入配方由用户要求和参数用途决定。用户要求“不绑定配方”的开关只做符号映射和控件绑定。

## 无在线 PLC 时

在 HMI Configuration 添加最新编译的 **Offline symbols (TMC)**：指定有意义的 PLC 名称并选择正确 TMC，然后在 **All Symbols → TMC** 下映射所需符号。离线源与 ADS 在线源的内部路径不同，不要把离线演示值当作在线 PLC 值。

目标 PLC 有该符号后，在 **Mapped Symbols** 选择映射，右键 **Switch Mapping to PLC**，选择经确认的 runtime，核对保存后的来源和实时读取。需要新的变量才能在线可用时，应先完成用户授权范围内的 PLC 发布流程。

## 文件方式和运行验证

需要用文件工具补映射，或核对“配置里有、运行里没有”时，读取 [references/config-and-verification.md](references/config-and-verification.md)。该参考包含已核对的配置结构和只读验证示例。

交付时分清已完成的层次：PLC 声明/TMC、项目中的持久映射、HMI Server 已加载的映射、经该别名读取 PLC、经授权的实际写入。至少核对持久配置和控件引用；有运行环境时还须核对服务当前映射及单项读取结果。运行环境不可用时如实报告缺口，不把静态核对或构建成功写成“已验证读写”。

对加热、运动、输出等机器命令，验证 map 优先只读，不以切换命令来试通。写入验证只在已授权、目标已确认且现场条件允许时执行。

## 官方依据

TE2000 TwinCAT 3 HMI Engineering，版本 2.18.0：§5.7.1.1–§5.7.1.4（PDF 第 81–89 页），§13.1.1.5.3（第 595 页）。本机参考文件为 `<beckhoff-library-root>/automation/twincat3/TE2000_TC3_HMI_EN.pdf`；文件不随 skill 分发，其他机器可查同版本官方手册。

2026-11-09 在 HMI 1.12.762.46 工程核对过上述配置格式，并通过运行中的 HMI Server 读取映射及 BOOL 值；界面步骤以官方手册为依据。此记录不代表所有版本或目标均已实测。

## 适用环境

- Platform: Windows TwinCAT HMI engineering project.
- Dependencies: The existing project, its installed HMI controls, configuration and applicable TwinCAT HMI version.
- Validation: packaging and static checks dated 2026-11-12; no new device or HMI runtime verification.
- Boundary: preserve the original project assumptions; authorize device writes or configuration activation separately.
