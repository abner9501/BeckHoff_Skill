# 文件映射与运行验证

## 三种名称

| 用途 | 示例 |
|---|---|
| PLC 变量路径 | `Application.bEnableTempHeatTime` |
| ADS 扩展内部映射来源 | `PLC1::Application::bEnableTempHeatTime` |
| HMI 服务器符号别名 | `PLC1.Application.bEnableTempHeatTime` |

`PLC1` 必须取自当前工程的 ADS runtime 配置。示例不指定 NetId，不改变现有连接。

## 持久映射

在实际使用的 HMI 项目中，检查 `Server/TcHmiSrv/TcHmiSrv.Config.default.json` 的顶层 `SYMBOLS`。若工程启用了其他配置，先确认运行配置、覆盖规则和配置存储位置；不能默认任意 default 文件都会被当前服务加载。

普通 BOOL 的在线 ADS 映射示例：

```json
{
  "SYMBOLS": {
    "PLC1.Application.bEnableTempHeatTime": {
      "ACCESS": 3,
      "DOMAIN": "ADS",
      "DYNAMIC": true,
      "MAPPING": "PLC1::Application::bEnableTempHeatTime",
      "SCHEMA": {
        "$ref": "tchmi:general#/definitions/BOOL"
      },
      "USEMAPPING": true
    }
  }
}
```

这是需要合并的单个条目，不是替换整个配置文件的模板。编辑前备份，保留其他 `SYMBOLS`、schema 定义、权限和扩展配置。条目存在时核对后更新，禁止产生重复 JSON 键。类型须与实际 PLC 符号一致；结构体、数组、枚举或属性应使用项目实际导出的 schema，不能一律写成 BOOL。

`ACCESS: 3` 是该条目的读写访问能力；用户/用户组权限还需单独检查，不能据此判断所有用户均可访问。沿用当前项目同类控制的授权方式，仅对需要的用户组增加该符号权限，不授予无关权限。某些工程的 `USERGROUPS/<现有组名>/SYMBOLS` 也有同名条目；不要从示例复制特殊系统组来替代实际操作员组。

控件引用示例：

```html
data-tchmi-state-symbol="%s%PLC1.Application.bEnableTempHeatTime%/s%"
```

Map 不需要改配方配置。也不要用 `DEFAULT` 常量、普通内部 symbol 或 TMC 离线值掩盖在线 PLC 符号不存在的问题。

## 使配置在实际工程生效

1. 确认文件所在工程与 XAE、服务进程 `--storageDir` 一致，并检查设计器有无未保存更改。
2. 完成映射配置和必要的权限后，保存、构建 HMI。构建只验证并生成对应产物，不证明当前 HMI Server 已读取新配置，也不会更新 PLC 中的符号。
3. 使用当前版本支持的配置编辑/应用流程让运行服务载入配置；随后重新读取服务当前 `SYMBOLS`。如果文件已更新而服务仍旧，不应反复新增条目；检查工程、活动配置和刷新状态。生产环境所需发布或服务重启必须留在用户授权范围内。
4. 若变量尚未进入在线 PLC，可导入最新 TMC 作离线 map，报告在线读取未完成。不要自行激活 PLC 来消除验证缺口。

## 经 HMI Server 只读核对

优先在 **Mapped Symbols** 使用实时值显示。已有可用 HMI 页面/诊断连接时，也可通过框架 API 发起只读查询：

```javascript
TcHmi.Server.readSymbol([
    'TcHmiSrv.Config::SYMBOLS::PLC1.Application.bEnableTempHeatTime',
    'PLC1.Application.bEnableTempHeatTime'
], function (data) {
    // 查看整体 error，并逐项检查 results 或 response.commands 的错误。
    console.log(data);
});
```

第一项核对当前服务已加载的来源和类型，第二项核对经映射别名能否读取值。配置读取通常需要配置查看权限；权限不足不能直接认定 map 不存在，应检查允许的诊断入口或当前用户组。

- 检查 `data.error` **以及每条结果**；整体成功不能排除单条命令失败。
- 正常读取 `false` 或 `0` 仍是成功，不能用真值判断替代错误检查。
- 核对返回的 `DOMAIN`、`MAPPING`、`SCHEMA`、`USEMAPPING`，并确认该项并非离线状态。
- 已有别名原来是内部变量时，尤其检查来源是否真正切到 ADS。例如 `AccumulatorInUse` 即使返回 TRUE，若服务当前配置仍是 `DOMAIN: TcHmiSrv`、空 `MAPPING`、`USEMAPPING: false`，只能证明内部值可读，不能证明读取了 PLC。文件中已改成 ADS 而服务仍返回这些旧字段时，判为运行配置尚未生效。
- 无映射条目：核对活动配置是否已加载、服务器别名拼写以及 map 操作。
- 有映射而读不到：继续检查 runtime/NetId/端口、在线 PLC 的符号版本、权限及连接状态，不能直接归因于未 map。
- 能读到值只证明当前读取链路，不能宣称已验证写入。测试页面和 LiveView 可能带有启动时的配方激活或写 PLC 动作，做只读检查时应避免触发这些动作。

本机于 2026-11-09 对普通 BOOL 在线映射和上述只读 API 核对成功。不要将当时的值或进程端口记录为以后任务的默认目标。
