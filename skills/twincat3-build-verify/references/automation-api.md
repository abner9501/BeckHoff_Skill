# 建项目 / 建对象 —— API 速查（Automation Interface）

> 从 `SKILL.md` §六 拆出来的。**只在"要建 PLC 项目 / 建对象"时才需要**；
> 日常"改完编译验证"用不到这张表。
> 脚本现成：`<skill-dir>\scripts\ai-add-plc.ps1`（建）、`ai-del-plc.ps1`（删）。

---

| 动作 | 调用 |
|---|---|
| 建 PLC 项目 | `sysManager.LookupTreeItem("TIPC").CreateChild(<名>, 0, "", "Standard PLC Template")` ← **用模板名，不是路径** |
| 建 NC 任务 | `LookupTreeItem("TINC").CreateChild("NC-Task", 1)` ← 实际节点名变成 **`NC-Task SAF`**，后续路径要按实际名 |
| 建 NC 轴（仿真） | `LookupTreeItem("TINC^NC-Task SAF^Axes").CreateChild("Axis 1", 1)` ← **新建即仿真轴**（`EncType=1` / `DrvType=5`），无需任何硬件 |
| 轴参数读写 | `axis.ProduceXml()` 取全量 XML → 改 → `axis.ConsumeXml(xml)` |
| PLC 轴 ↔ NC 轴 | `ITcSysManager::LinkVariables(src, dst)`：<br>NC→PLC `TINC^NC-Task SAF^Axes^Axis 1^Outputs^ToPlc` → `TIPC^<PLC>^<PLC> Instance^PlcTask Inputs^GVL.Axis1.NcToPlc`<br>PLC→NC `…^PlcTask Outputs^GVL.Axis1.PlcToNc` → `…^Axis 1^Inputs^FromPlc`<br>⚠ PLC 侧**必须写全路径**（`TIPC^<项目>^<项目> Instance^…`），短写 `PlcTask Inputs^…` 只在文档示例里成立 |
| 工程目标平台 | tsproj 工程根 `<Project … Target64Bit="true">`；**Automation Interface 无设置 API**，`File.SaveAll` 会按内存状态丢掉手改值（见 SKILL §六·六）|
| 模板文件 | `C:\TwinCAT\3.1\Components\Plc\PlcTemplate\Plc Templates\{Standard,Empty} PLC Template.plcproj` |
| 项目节点 | `TIPC^<名>^<名> Project`（= nested project，下面有 POUs / GVLs / DUTs / References / PlcTask …） |
| 建 GVL | `…Project^GVLs` 下 `CreateChild(<名>, 615, "", "VAR_GLOBAL … END_VAR")` |
| 建 POU | `…Project^POUs` 下 `CreateChild(<名>, 604, "", IECLANGUAGETYPES.IECLANGUAGE_ST)` |
| **对象 subtype** | PRG **602** / FUN **603** / FB **604** / GVL **615** / ITF **618**；**变量 = -1**（vInfo 传类型串，如 `"BOOL"`） |
| 删 PLC 项目 | `TIPC.DeleteChild(<名>)` ← 连 tsproj 的 `<Project>` 节点、sln 的 GUID 条目一起清（**磁盘文件夹不删**） |
| 删 task | `TIRT.DeleteChild(<名>)` |
| 保存 | `SaveConfiguration()` **已废**（"only supported in Automation Legacy Mode"）→ 改用 DTE 的 `File.SaveAll` |

> ⚠ 建 POU 只到"建出空壳 + 指定语言"（`IECLANGUAGE_ST`）—— **往 POU 里写 ST 正文不在本 skill 范围内**。

## 相关

- 手册：`<beckhoff-library-root>\automation\twincat3\TC3_Automation_Interface_EN.pdf`
  - §4.3.5.1 PLC best practice（建项目）· **§5.3.3 Tree item sub types**（那串编号表）·
    §5.3.6 `CreateChild` · §5.3.7 `DeleteChild` · §5.8 `ITcPlcIECProject`
- 原始 PDF：`<beckhoff-library-root>\automation\twincat3\TC3_Automation_Interface_EN.pdf`
