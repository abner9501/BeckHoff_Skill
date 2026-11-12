# Client 公开方法选型（2.2.0）

从 npm 发布的 dist/ads-client.d.ts 核对。以下为方法族与用途；不包含 private 方法。精确重载与字段以锁定版本的类型声明为准。

| 方法 | 用途 |
|---|---|
| connect / disconnect / reconnect | 建连、断连、重连 |
| setDebugLevel | 调试输出级别 |
| readValue / writeValue | 按变量路径读写 JS 值，读取值在 result.value |
| readValueBySymbol / writeValueBySymbol | 按 AdsSymbol 对象读写 JS 值 |
| subscribeValue / subscribeRaw / subscribe | 按名解码通知、原始通知、配置对象订阅 |
| unsubscribe / unsubscribeAll | 取消一个/全部通知 |
| getSymbol / getSymbols / cacheSymbols | 获取单个/全部符号，缓存符号 |
| getDataType / getDataTypes / cacheDataTypes | 获取单个/全部类型，缓存类型 |
| readPlcUploadInfo / readPlcSymbolVersion | 符号上传信息、符号版本 |
| readRaw / writeRaw | IndexGroup + IndexOffset 原始字节读写 |
| readRawByPath / writeRawByPath | 按路径原始字节读写 |
| readRawBySymbol / writeRawBySymbol | 按符号原始字节读写 |
| readRawByHandle / writeRawByHandle | 按句柄原始字节读写 |
| readRawMulti / writeRawMulti | Sum 批量原始读/写 |
| readWriteRaw / readWriteRawMulti | 单次/批量 ADS ReadWrite 服务 |
| createVariableHandle / createVariableHandleMulti | 创建单个/多个句柄 |
| deleteVariableHandle / deleteVariableHandleMulti | 释放单个/多个句柄 |
| convertFromRaw / convertToRaw | Buffer 与 JS 值按 PLC 类型转换 |
| getDefaultPlcObject | 按 PLC 类型生成默认 JS 对象 |
| readDeviceInfo | 设备名称和版本 |
| readState / readPlcRuntimeState | ADS 目标/PLC Runtime 状态 |
| readTcSystemState / readTcSystemExtendedState | TwinCAT 系统/扩展状态（依目标支持） |
| startPlc / stopPlc / resetPlc / restartPlc | PLC 启动、停止、cold reset、复位后启动 |
| setTcSystemToRun / setTcSystemToConfig / restartTcSystem | TwinCAT 系统状态切换、重启 |
| writeControl | 底层 ADS 状态控制 |
| invokeRpcMethod | 调用 PLC 公开的 FB RPC 方法 |
| sendAdsCommand / sendAdsCommandWithFallback | 底层命令和特定错误时的备用命令 |

## 重要签名

```ts
readValue<T>(path, targetOpts?): Promise<ReadValueResult<T>>
writeValue<T>(path, value, autoFill?, targetOpts?): Promise<WriteValueResult<T>>
subscribeValue<T>(path, callback, cycleTime?, sendOnChange?, maxDelay?, targetOpts?): Promise<ActiveSubscription<T>>
readRaw(indexGroup, indexOffset, size, targetOpts?): Promise<Buffer>
writeRaw(indexGroup, indexOffset, value, targetOpts?): Promise<void>
invokeRpcMethod(path, method, parameters?, targetOpts?): Promise<RpcMethodCallResult>
```

此处简写参数类型用于选型，不能直接作为 TypeScript 声明文件。

## Sum 和生命周期

先获取符号地址/类型，使用 readRawMulti 聚合读取，再通过 convertFromRaw 解码。批量命令字段和子结果错误字段需查当前发布类型，不能凭 C# Sum API 拼接。检查每条结果；请求成功不保证每个子命令成功，也不保证多个变量来自同一 PLC 周期。

句柄属于目标资源，创建后应释放，批量创建需核对每项结果。符号变化/PLC 下载后不要继续使用自行缓存的旧地址、类型或句柄。不要直接沿用其他机器测试的句柄数上限和传输性能。

写成功表示 ADS 服务已受理，不保证 PLC 程序已经执行命令。关键业务用 PLC 确认序号/状态；不能仅靠写回读相等证明动作完成。轮询和通知各自都要评估频率、负载及消费速度。
