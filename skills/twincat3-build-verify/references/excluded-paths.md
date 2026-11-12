# 已经排除的路（别再试）

> 从 `SKILL.md` §四 拆出来的完整清单。
> **结论：`msbuild` / CLI 编不了 TC3。** 下面这些全试过、全不通 —— 别再花时间。

| 试过的 | 结果 |
|---|---|
| `MSBuild.exe X.sln` | `X.tsproj.metaproj 未找到`。`.tsproj` 不是 MSBuild 工程，metaproj 要靠 IDE 里的工程系统生成 |
| `MSBuild.exe X.plcproj` | `MSB4057: 项目中不存在目标"Build"`。TwinCAT 的编译目标只从 tsproj 侧导入 |
| `TcXaeShell.exe <sln> /Rebuild "<cfg>"` | 弹窗"**不支持该命令**" |
| `devenv.exe /Out log.log` | 需要完整 VS + TwinCAT 集成；本机没装完整 VS |
| 找 TwinCAT 的 MSBuild targets | `C:\TwinCAT\3.1\sdk\MsBuild\` 里**全是给 C++ 工程的**（`Beckhoff.Cpp.*`）+ 发布/签名，**没有一条给 `.plcproj`** |
| 找独立编译器 CLI | PLC 编译器是 DLL（`TwinCATPlcControl.dll` / `TcPlcMerge.dll`），**没有 exe 入口** |
| Windows 事件日志 / Temp 临时文件 / `_CompileInfo` | 都没有编译报错文本 |
| `Marshal.GetActiveObject('TcXaeShell.DTE.15.0')` | **`MK_E_UNAVAILABLE`** —— 正在跑的交互式实例**不注册到 ROT**，挂不上去 |

**唯一可行的路**：起一个 `TcXaeShell` 的 DTE 实例（Automation Interface）→ 见 `SKILL.md` §一。
