---
name: tchmi-parameter-settings
description: TwinCAT HMI 参数页面统一设置规范。用于将加载、保存、single-page/multi-page 控件集中到共享 Parameters_Settings.content，补齐各参数页引用，并修复页面切换后设置栏消失或单页只显示一条参数的问题。
---

# TwinCAT HMI 参数设置统一规范

用于包含 `TcHmi.Controls.Plastic.Table` 的参数页面。先确认用户指定的实际 HMI 工程目录；同级备份、副本和其他 HMI 工程不应一并修改。

## 共享设置栏

- 以 `Contents/Slider/Parameters/Parameters_Settings.content` 作为唯一共享设置内容，保留其中加载、保存和 single-page/multi-page 控件的绑定。
- 扫描 `Contents/Navigation/Parameters/Parameters_*.content`。每个含 Plastic Table 的参数页都应包含一个 `TcHmiRegion`，其 `TargetContent` 指向共享设置内容。
- 新增 Region 时沿用已正常工作的壁厚参数页布局，包括位置、尺寸和 `zindex`；同时统一 Table 的 `HeaderHeight`。只复制设置栏布局，不覆盖页面自己的 TableList、符号、触发器或导航。
- 共享内容内部使用固定控件 ID，因此 `Properties/tchmiconfig.json` 中该内容必须设置 `keepAlive: false`。否则离开第一个页面后，缓存实例会占用 ID，后续页面的设置栏可能不显示。

## Single-page 初始化

`ParametersTableMultiPage=false` 表示 single-page，`true` 表示 multi-page。

Beckhoff `FillParamTable` 初始化时会先执行以下临时步骤：

1. `setCreateMultipleTables(false)`。
2. `setReftable([TableFillingInProgress])`，此时数组长度为 1。
3. 异步读取 Schema 并生成完整参数数组。
4. 最后调用 `setReftable(table)`。

不要在第 1 或第 2 步根据 `refsubtable.length` 设置 `TableRowCount`，否则重新进入页面时只会显示一条，并出现 `Page 1|N`。

单页行数必须在完整数组写入且 `CreateMultipleTables` 最终状态设置完成后更新：

```javascript
TableControl.setReftable(table);
TableControl.setCreateMultipleTables(HasMultipleTables);
if (!(await asyncRead("%s%ParametersTableMultiPage%/s%"))) {
    TableControl.setTableRowCount(table.length);
    TableControl.setActSubPage(0);
}
```

使用现有 `asyncRead`/`readEx2` 读取服务器符号。初始化期间不要依赖同步 `readEx`，它可能返回空值。

## TwinCAT 函数注册

- 不要仅靠普通 JavaScript 中的 `registerFunctionEx` 新增 TriggerManager 函数。TwinCAT HMI 还要求 `tchmiconfig.json#userFunctions`、Manifest 或有效的 `*.function.json` 元数据。
- 若日志出现 `Missing metadata for functions` 或 `E_MODULE_MISSING`，立即恢复原触发器函数；参数表不显示通常是填表函数根本没有执行。
- 若项目没有规范的用户函数结构，优先在原 `FillParamTable` 完成位置做最小修复，不要把无元数据函数写入页面触发器。

## 源文件与运行输出

- 修改工程源文件后，检查 `bin` 是否仍为旧内容。用户未重新生成而直接运行时，`bin` 中缺失 Region 或仍为旧 JS 会导致修复看似无效。
- 同步检查：
  - `Properties/tchmiconfig.json` 与 `bin/Properties/tchmiconfig.json`
  - 新增或修改的参数 `.content`
  - 修改的运行 JavaScript
- 不修改无关备份工程。构建工具能够可靠更新 `bin` 时，以源文件为准；需要立即验证现有输出时再同步 `bin`。

## 验证

1. 确认所有含 Plastic Table 的参数页都引用共享设置内容。
2. 解析所有修改过的 JSON 和 `<script type="application/json">` 块。
3. 对修改后的 JavaScript运行 `node --check`。
4. 检查页面触发器仍调用存在且有元数据的 `TcHmi.Functions.Plastic.FillParamTable`。
5. 测试：壁厚参数切到 single-page -> 射料参数 -> 返回壁厚参数；两页设置栏均显示，参数完整且不是 `Page 1|N`。
6. 发布后重启 HMI Server并强制刷新浏览器，排除旧内容缓存。

## 适用环境

- Platform: Windows TwinCAT HMI engineering project.
- Dependencies: The existing project, its installed HMI controls, configuration and applicable TwinCAT HMI version.
- Validation: packaging and static checks dated 2026-11-12; no new device or HMI runtime verification.
- Boundary: preserve the original project assumptions; authorize device writes or configuration activation separately.
