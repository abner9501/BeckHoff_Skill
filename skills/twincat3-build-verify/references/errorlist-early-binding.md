# ErrorList 必须用 `EnvDTE80.DTE2` 早绑定

> 从 `SKILL.md` §三.3 拆出来的细节。**结论一句话**：
> `ToolWindows.ErrorList` 只在 **`EnvDTE80.DTE2`** 上；PowerShell 的后期绑定解析到老的 DTE1 →
> **`ErrorItems.Count` 永远返回 0，而且不报错**。
> （`build-plc.ps1` 里已经封好了，一般**不用自己写**。）

---

`ToolWindows.ErrorList` 和 `ToolWindows.OutputWindow` 都在 **`EnvDTE80.DTE2`** 上。
**PowerShell 的 COM 是后期绑定**，`$dte.ToolWindows` 解析到的是老的 DTE1 →

```powershell
# ❌ 这样写不会报错，但永远返回 0 条 —— 最容易骗过自己的一步
$dte.ToolWindows.ErrorList.ErrorItems.Count    # → 0
$dte.ToolWindows.OutputWindow                  # → $null
```

**修法**：`Add-Type -Path` 载入 `EnvDTE.dll` + `EnvDTE80.dll`，再写一段 C# 桥接类里面 `DTE2 _dte2 = (DTE2)raw;`，用早绑定取：

```csharp
ErrorItems items = _dte2.ToolWindows.ErrorList.ErrorItems;   // 1-based
for (int i = 1; i <= items.Count; i++) {
    ErrorItem it = items.Item(i);
    // it.ErrorLevel / it.Description / it.FileName / it.Line / it.Column
}
```

程序集位置：

```
C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\PublicAssemblies\
    EnvDTE.dll      ← 先 Add-Type -Path 载入
    EnvDTE80.dll
```

⚠ 官方手册 §4.3.1.6 最后一句就是警告：
*"Make sure that the ToolWindows property is available in the **EnvDTE80.DTE2** namespace."*
—— 我第一遍读到了、滑过去了，白折腾了一轮。

⚠ **两个附带坑**：
- `it.ErrorLevel` 是枚举 `vsBuildErrorLevel`，赋给 int 要强转 `(int)`
- `Add-Type` 里**别用 `dynamic`**（缺 `Microsoft.CSharp` 程序集）

## 相关

- 现成实现：`<skill-dir>\scripts\build-plc.ps1`
- 手册：`<beckhoff-library-root>\automation\twincat3\TC3_Automation_Interface_EN.pdf` §4.3.1.6 Error List
- 参考实现（社区 PoC，思路来源）：GitHub `benhar-dev/ps-dte-errorlist`
