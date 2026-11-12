# 连接、读写、轮询与通知

以下 API 来自 ads-client 2.2.0 发布资料；代码是整理示例，未连接设备运行。安装：`npm install ads-client`。CommonJS 使用 `.cjs`；ESM 使用 `import { Client } from 'ads-client'`。

## 连接与路由

本机或有本机 TwinCAT Router：

```js
const { Client } = require('ads-client');
const client = new Client({
  targetAmsNetId: 'localhost',
  targetAdsPort: 851
});
```

远程已有本机 Router 时，targetAmsNetId 改为实际 PLC Net ID，并建立相应路由。没有本机 Router（如 Linux/树莓派）可直连 PLC Router：

```js
const client = new Client({
  localAmsNetId: '<local-net-id>',
  localAdsPort: 32750,
  targetAmsNetId: '<target-net-id>',
  targetAdsPort: 851,
  routerAddress: '<target-ip>',
  routerTcpPort: 48898
});
```

这些地址仅为示例。PLC 路由中客户端 Net ID 必须匹配 localAmsNetId，IP 必须指向实际 Node.js 主机。传统 AMS/TCP 使用 48898；不要将 ADS 端口 851 当成 TCP 端口。容器/NAT 还需检查实际网络可达性。

## 按名读写

```js
async function readOnce() {
  await client.connect();
  try {
    const result = await client.readValue('GVL_Ads.nCount');
    console.log(result.value);
  } finally {
    await client.disconnect();
  }
}
readOnce().catch(console.error);
```

写入：`await client.writeValue('GVL_Ads.rSpeedSet', 100.0)`。
读数组/结构体同样使用 readValue，结果为按 PLC 类型解码的 JS 值。64 位整数、时间和字符串编码有专门规则，需要按发布版本文档核对；不要把超过 Number 安全整数范围的值强转成 Number。

`writeValue(path, partialObject, true)` 的 autoFill 会先读取当前对象并补齐再写回。它不是服务器端原子成员更新，可能覆盖并发变化；只改一项时优先写完整成员路径。读取或整体写入结构体，也不自动保证 PLC 业务一致性。

## 轮询

包无 poll()。以下循环每次读取完成后等待，实际周期=读取耗时+等待时间；信号由调用方停止，正在进行的 ADS 读仍需完成或超时。

```js
const { setTimeout: delay } = require('node:timers/promises');

async function poll(client, path, intervalMs, signal, onValue) {
  if (!Number.isFinite(intervalMs) || intervalMs <= 0) {
    throw new Error('intervalMs must be positive');
  }
  while (!signal.aborted) {
    try {
      const result = await client.readValue(path);
      if (!signal.aborted) await onValue(result.value);
    } catch (error) {
      if (!signal.aborted) console.error('ADS read failed:', error);
    }
    try {
      await delay(intervalMs, undefined, { signal });
    } catch (error) {
      if (error.name !== 'AbortError') throw error;
    }
  }
}
```

避免 setInterval(async ...) 无互斥启动下一轮。变量多时评估专用状态结构体或 Sum；Promise.all 多读不是 Sum。

## 通知

```js
const subscription = await client.subscribeValue(
  'GVL_Ads.bAlarm',
  data => console.log(data.value),
  100,
  true
);
// 使用结束：
await subscription.unsubscribe();
// 或 await client.unsubscribe(subscription);
// 应用退出时 await client.unsubscribeAll(); 然后 disconnect()
```

签名：`subscribeValue(path, callback, cycleTime?, sendOnChange?, maxDelay?, targetOpts?)`。
cycleTime 与 maxDelay 参数单位为 ms。显式传 true 表示变化通知，false 表示周期推送（值不变也发送）。回调参数 data.value 是解码后的值，第二参数为订阅对象。

通知是采样后的变化，不保证捕获采样间隔中的所有短脉冲；事件需要 PLC 锁存、序号或计数。回调避免同步耗时工作；如果启动异步工作，应自行捕获 Promise 错误和控制背压。

重连时库有订阅备份恢复机制，不能照搬 C# 的重新注册策略而叠加重复订阅。应检查恢复失败和实际数据是否恢复，具体行为见 sources.md。

## PLC 功能块

成员读写：`readValue('MAIN.fbMotor.bRunning')`、`writeValue('MAIN.fbMotor.rSpeedSet', 100)`。PLC 仍需在周期程序内调用实例处理输入。命令建议请求/确认握手，避免 Node.js 快速置位复位漏采。

RPC 方法需 PLC 声明 `{attribute 'TcRpcEnable'}`：

```js
const result = await client.invokeRpcMethod(
  'GVL_RPC.RpcBlock', 'Calculator', { Value1: 1, Value2: 123 }
);
console.log(result.returnValue, result.outputs);
```

上游作者的经验提醒：改变 RPC 参数或返回类型后避免依赖 online change；参数/返回类型的 pack-mode 和直接 ARRAY 参数可能有兼容问题，建议数组放 STRUCT 中。此类属于作者经验，非普遍的官方保证，使用前核对当前版本和 PLC 布局。
