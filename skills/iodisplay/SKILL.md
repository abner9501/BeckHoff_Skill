---
name: iodisplay
description: IODisplay HMI页面实现规范 - TwinCAT HMI IO监控页面的布局规则、模块排列、绑定格式等终极知识
metadata:
  short-description: TwinCAT HMI IODisplay页面规范
---

# IODisplay 页面实现规范

TwinCAT HMI 的 IO 监控页面全局规则。适用于所有 `IODisplayXX.content` 页面。

---

## 页面布局

### 固定参数（不可改动）

| 参数 | 值 | 说明 |
|------|----|------|
| 总行数 | **36 行**（0-35） | Grid 行数固定，必须参考当前 `IODisplay01.content` 的 row-options |
| 数据行(30px) | **18 行**（偶数索引） | 唯一可放元素的 30px 行 |
| 间隔行(1px/\*factor) | **18 行**（奇数索引） | 仅作为间隔，不放任何元素，row 35 是最后一个底部间隔 |
| 总列数 | **10 列** | Grid 列数固定 |
| 有效列 | **col 1-8** | col 1/3/5/7 = 信号名，col 2/4/6/8 = 值/灯 |
| 边缘列 | **col 0 / col 9 = 5px** | 仅作边缘间隔，不放任何元素 |
| 页面尺寸 | **1010×650 px** | `IODisplayXX` Content 容器固定尺寸 |

### 列配置

```
col 0:  5px    (左边缘间隔，不放内容)
col 1:  1f     (信号名 Textblock)   ← 模块1
col 2:  30px   (InputBox / StateIndicator)
col 3:  1f     (信号名 Textblock)   ← 模块2
col 4:  30px   (InputBox / StateIndicator)
col 5:  1f     (信号名 Textblock)   ← 模块3
col 6:  30px   (InputBox / StateIndicator)
col 7:  1f     (信号名 Textblock)   ← 模块4
col 8:  30px   (InputBox / StateIndicator)
col 9:  5px    (右边缘间隔，不放内容)
```

### 行配置

```
Row 0:     30px   (上段模块标题)       ← 第1段标题
Row 1:     1*f    (间隔)
Row 2:     30px   (通道1)             ← 第1段 8 通道
Row 3:     1*f    (间隔)
Row 4:     30px   (通道2)
...        ...
Row 16:    30px   (通道8)
Row 17:    1*f    (段间隔)
Row 18:    30px   (下段模块标题)       ← 第2段标题
Row 19:    1*f    (间隔)
Row 20:    30px   (通道1)             ← 第2段 8 通道
...        ...
Row 34:    30px   (通道8)
Row 35:    1*f    (底部间隔)
```

`IODisplayXX` 的 Grid 行高必须按当前 `IODisplay01.content` 分配：共 36 行，从 row 0 到 row 35 严格交替 `30px` 与 `1.0factor`，即偶数行 `30px`、奇数行 `1.0factor`。不要恢复成旧的 35 行配置。

### 元素放置规则

- **只放在 30px 行**（偶数行：0,2,4,6,8,10,12,14,16,18,20,22,24,26,28,30,32,34）
- **不放任何元素在间隔行**（奇数行：1,3,5,...,17,19,...,33,35）
- **不放任何元素在 col 9-12**

---

## 模块排列

### 顺序和标题

- `IODisplayXX` 的模块顺序以 PLC `FB_IOMap.TcPOU` 中 `VAR_INPUT` 声明顺序为准，不再按 Excel 行号、端子号或模块号重新排序。
- Excel 只提供通道中文名/备注，不决定模块顺序。
- 模块标题使用型号加同型号序号，不使用 `ELxxxx_tN` 或 Excel 的 `模块NN` 作为显示名：
  - 第 1 个 `EL3078` 显示 `EL3078`
  - 第 1 个 `EL1008` 显示 `EL1008*1`
  - 第 2 个 `EL1008` 显示 `EL1008*2`
  - 第 1 个 `EL2008` 显示 `EL2008*1`
  - 第 2 个 `EL2008` 显示 `EL2008*2`

### 每页容量

- 每段（9 数据行）= **4 个模块**（使用 col 1-8）
- 每页（上下 2 段）= **8 个模块**
- 8 通道模块占 9 数据行（1 标题 + 8 通道）
- 4 通道模块也占 9 行（空余通道留空）
- 16 通道模块则独占一列 18 行

### 行公式

```
通道行 = start_row + channel_index × 2
```

其中 channel_index 是通道编号（1-based）。

上段 start_row = **0**：标题 row 0，通道 row 2/4/6/8/10/12/14/16
下段 start_row = **18**：标题 row 18，通道 row 20/22/24/26/28/30/32/34

### 列公式

```
模块列 = 1 + 模块序号 × 2
```

模块序号 0→col 1（信号名）+ col 2（值）
模块序号 1→col 3（信号名）+ col 4（值）
模块序号 2→col 5（信号名）+ col 6（值）
模块序号 3→col 7（信号名）+ col 8（值）

---

## 元素类型

### 模块标题 (Textblock)

```html
<div id="H_{FB_Var}" data-tchmi-type="TcHmi.Controls.Beckhoff.TcHmiTextblock"
data-tchmi-grid-column-index="{col}" data-tchmi-grid-row-index="{start_row}"
data-tchmi-height="100" data-tchmi-height-unit="%"
data-tchmi-left="0" data-tchmi-left-unit="px"
data-tchmi-text="{模块名}" data-tchmi-top="0" data-tchmi-top-unit="px"
data-tchmi-width="100" data-tchmi-width-unit="%"
data-tchmi-word-wrap="true"
data-tchmi-text-horizontal-alignment="Center"
data-tchmi-text-vertical-alignment="Center"
data-tchmi-text-font-size="15">
<script data-tchmi-target-attribute="data-tchmi-background-color" type="application/json">
{"color": "rgba(194, 216, 191, 1)"}
</script>
</div>
```

### 信号名 (Textblock)

```html
<div id="T_{FB_Var}_{Row}" data-tchmi-type="TcHmi.Controls.Beckhoff.TcHmiTextblock"
data-tchmi-grid-column-index="{col}" data-tchmi-grid-row-index="{row}"
data-tchmi-height="100" data-tchmi-height-unit="%"
data-tchmi-left="0" data-tchmi-left-unit="px"
data-tchmi-text="{信号名}" data-tchmi-top="0" data-tchmi-top-unit="px"
data-tchmi-width="100" data-tchmi-width-unit="%"
data-tchmi-word-wrap="true"
data-tchmi-text-horizontal-alignment="Center"
data-tchmi-text-vertical-alignment="Center"
data-tchmi-text-font-size="15">
</div>
```

### 模拟量值 (InputBox) — 用于 AI/AO 模块

```html
<div id="IB_{FB_Var}_{Row}" data-tchmi-type="TcHmi.Controls.Plastic.InputBox"
data-tchmi-digitsafterdecimal="2" data-tchmi-displayunittext="false"
data-tchmi-fbvalue_datatype="FloatDataType"
data-tchmi-grid-column-index="{col+1}" data-tchmi-grid-row-index="{row}"
data-tchmi-height="30" data-tchmi-height-unit="px"
data-tchmi-isreadonly="true" data-tchmi-left-unit="px"
data-tchmi-logchangesbyuser="false"
data-tchmi-maxval="99999" data-tchmi-minval="-99999"
data-tchmi-readbackgroup="0" data-tchmi-readbackonwrite="false"
data-tchmi-recipetype="None" data-tchmi-stepsize="1"
data-tchmi-top-unit="px" data-tchmi-width="60" data-tchmi-width-unit="px"
data-tchmi-textval="%s%IOMap::{FB_Var}[{Offset}]%/s%"
data-tchmi-right="0" data-tchmi-top="0">
<script data-tchmi-target-attribute="data-tchmi-unittext" type="application/json"></script>
</div>
```

### 数字量指示灯 (StateIndicator) — 用于 DI/DO 模块

```html
<div id="SI_{FB_Var}_{Row}" data-tchmi-type="TcHmi.Controls.Plastic.StateIndicator"
data-tchmi-errorstate="false"
data-tchmi-grid-column-index="{col+1}" data-tchmi-grid-row-index="{row}"
data-tchmi-height="100" data-tchmi-height-unit="%"
data-tchmi-left="0" data-tchmi-left-unit="px"
data-tchmi-normalstate="%s%IOMap::{FB_Var}[{Offset}]%/s%"
data-tchmi-top="0" data-tchmi-top-unit="px"
data-tchmi-warningstate="false"
data-tchmi-width="100" data-tchmi-width-unit="%">
<script data-tchmi-target-attribute="data-tchmi-background-color" type="application/json">
{"color": "rgba(225, 231, 230, 1)"}
</script>
</div>
```

---

## 绑定规则

### PLC 变量路径

HMI 绑定格式使用 `IOMap::` 前缀，对应 PLC 中的 `FB_IOMap` 实例：

```
%符号%IOMap::{模块名}[{从0偏移}]%/符号%
```

例如：
- `%s%IOMap::EL1008_t5[0]%/s%` → 第1通道
- `%s%IOMap::EL2008_t23[3]%/s%` → 第4通道
- `%s%IOMap::EL3078_t2[7]%/s%` → 第8通道

**注意**：HMI 绑定使用**从0偏移**（0 对应物理通道 1），而 FB_IOMap 数组声明为 `ARRAY[1..N]` 时，HMI 侧偏移 = 物理通道 - 1。

### 模拟量缩放

AI/AO 模块的 LREAL 值需要缩放公式：
```
TO_REAL(PLC变量*100)/32767
```

---

## 页面分配

### 20 个模块按机架顺序分配

| 页面 | 上段(4模块) | 下段(4模块) | 标签名 |
|------|------------|------------|--------|
| IODisplay01 | EL3078\*1, EL4134\*1, EL1008\*1~2 | EL1008\*3~6 | 模拟量和数字量输入 |
| IODisplay02 | EL1008\*7~9, EL2008\*1 | EL2008\*2~5 | 数字量输出\*1 |
| IODisplay03 | EL2008\*6~9 | — | 数字量输出\*2 |

---

## 本地化引用

### HMI 侧格式
```
%f%%l%Control::TcHmi.Controls.Plastic.Tc3_PlasticLocalizations::KEY%/l%+' Address'%/f%
```

- `%f%...%/f%` 包裹整个表达式
- `%l%...%/l%` 引用本地化 key
- `+' Address'` 拼接地址后缀（Y1/x1 等）

### zh-CN.json 侧格式

```json
"KeyName": "中文翻译",
```

**注意**：翻译中不含地址（Y1/x1），地址通过 HMI 侧的 `+' Y1'` 拼接。

---

## 生成脚本通用模式

Python 生成脚本的关键函数：

```python
# 模块定义结构
(hdr_name, fb_var, module_type, [(ch_idx, signal_name, fb_offset), ...])

# module_type: "ai" | "ao" | "di" | "do"
# ai/ao → InputBox，di/do → StateIndicator

# 行公式
row = start_row + ch_idx * 2    # ch_idx 从1开始

# 列公式
col = 1 + module_index * 2      # module_index 从0开始

# 页面分组
pages = [(page_id, top_modules_list, bottom_modules_list), ...]
```

---

## 重要注意事项

1. **列约束** - 只使用 col 1-8，col 9-12 不放内容
2. **行约束** - 元素只放在偶数 30px 行，奇数间隔行不放任何内容
3. **通道公式** - `row = start_row + ch_idx * 2`，不是 `start_row + 1 + ch_idx * 2`
4. **偏移从0** - HMI 绑定偏移从 0 开始，物理通道从 1 开始
5. **36行固定** - Grid 总行数 36（0-35），不可变；行高按当前 `IODisplay01.content`：偶数行 30px、奇数行 1.0factor
6. **底色统一** - 标题淡绿 `rgba(194,216,191,1)`，指示灯淡灰 `rgba(225,231,230,1)`
7. **引用优先** - 信号名通过 `%l%` 引用 zh-CN.json，地址通过 `+' Y1'` 拼接

## 适用环境

- Platform: Windows TwinCAT HMI engineering project.
- Dependencies: The existing Plastic HMI controls and IODisplay01.content layout; these dimensions are project conventions.
- Validation: packaging and static checks dated 2026-11-12; no new device or HMI runtime verification.
- Boundary: preserve the original project assumptions; authorize device writes or configuration activation separately.
