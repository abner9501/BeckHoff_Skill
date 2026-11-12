---
name: fb-iomap
description: TwinCAT PLC FB_IOMap 生成和更新规范。用于根据 EtherCAT 端子顺序和参考 FB_IOMap.TcPOU 创建或修正 Application\Sequence\Subitems\FB_IOMap.TcPOU，要求按端子顺序声明数组、按通道 1..N 顺序写 Cyclic，未绑定通道也要保留但不赋值。
---

# FB_IOMap 生成规范

用于 TwinCAT PLC 项目的 `Application\Sequence\Subitems\FB_IOMap.TcPOU`。

## 核心规则

- 按 EtherCAT 文本中的实际出现顺序生成变量：`EL型号_t端子号`。不要按 `Term` 号或 `Box Id` 重新排序；例如 `EL2008_t15` 如果在文本最后出现，就必须放在最后。
- 跳过电源/耦合等无 IO 映射端子，例如 `EL9410`。
- 数组长度按模块通道数：
  - `EL3078` -> `ARRAY[1..8] OF LREAL`
  - `EL4134` -> `ARRAY[1..4] OF LREAL`
  - `EL1008` -> `ARRAY[1..8] OF BOOL`
  - `EL2008` -> `ARRAY[1..8] OF BOOL`
- 不生成 `EL3318` / `EL3314`，即使参考文件、Excel 或旧 `FB_IOMap` 中存在，也不要出现在声明或 `Cyclic` 中。
- `VAR_INPUT` 中按端子顺序声明所有数组。
- `Cyclic` 中按端子顺序分段，每段标题格式：
  `// ===== EL1008_t5 (Term 5) =====`
- 每个端子内必须按通道 `1..N` 顺序逐行写。
- 有绑定的通道写赋值：`ELxxxx_tN[i] := source;`
- 没有绑定的通道也必须写一行注释占位，不赋值：
  `// ELxxxx_tN[i] 未绑定`
- 模拟量缩放沿用参考项目写法：
  `TO_REAL(source.AnalogValue*100)/32767`
- 不要为了未绑定通道写 `:= 0`、`:= FALSE` 或其他默认赋值。

## 推荐流程

1. 读取 EtherCAT 文本或 `.tsproj` 中的 `<Box><Name>Term N (ELxxxx)</Name>`，得到端子顺序。
2. 读取参考 `FB_IOMap.TcPOU` 中已有绑定，作为通道赋值来源。
3. 生成完整的 `FB_IOMap.TcPOU`：
   - 声明包含全部 IO 端子数组。
   - `Cyclic` 包含每个端子的每个通道。
   - 未绑定通道保留注释占位。
4. 将 `Application\Sequence\Subitems\FB_IOMap.TcPOU` 加入目标 `Tc3_PlasticApplication.plcproj`：
   `<Compile Include="Application\Sequence\Subitems\FB_IOMap.TcPOU">`
5. 验证声明顺序、Cyclic 分段顺序、每段通道覆盖率。

## 应用脚本

优先使用 bundled script：

```powershell
python "<skill-dir>\scripts\apply_fb_iomap.py" `
  --target-root "<target-project-root>" `
  --reference-pou "<reference-project-root>\Application\Sequence\Subitems\FB_IOMap.TcPOU" `
  --ethercat-text "<ethercat-text-file>"
```

脚本会创建/更新 `FB_IOMap.TcPOU`，并补 `.plcproj` 引用。

## 适用环境

- Platform: Windows TwinCAT 3 project files; Python 3.
- Dependencies: An existing target PLC project, reference TcPOU and EtherCAT terminal description; project-specific rules must be checked before reuse.
- Validation: packaging and static checks dated 2026-11-12; no new device or HMI runtime verification.
- Boundary: preserve the original project assumptions; authorize device writes or configuration activation separately.
