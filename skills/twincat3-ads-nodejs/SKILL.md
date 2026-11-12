---
name: twincat3-ads-nodejs
description: 使用 Node.js 或 TypeScript 的 ads-client 连接倍福 TwinCAT PLC，进行 ADS 变量读写、轮询、通知订阅、Sum 批量操作、功能块成员访问和 RPC 调用，处理路由与重连问题。适用于 Node.js ADS；C# TwinCAT.Ads 使用独立的 C# 技能。
---

# Node.js TwinCAT ADS 知识

本知识依据第三方 `ads-client` npm 包 2.2.0 的发布版 README、类型声明整理，检索日期 2026-11-02。它不是倍福官方 Node.js SDK。参考资料来自上游，不代表本机已经连接 PLC 实测。

## 使用流程

1. 先检查用户项目的 package.json 和锁文件，确定实际 ads-client 版本、CommonJS/ESM 和 Node.js 环境；不要为回答问题自动升级依赖。
2. 连接、按名读写、轮询、通知、功能块：读 [references/usage.md](references/usage.md)。
3. 方法选型、批量、句柄、转换、系统控制：读 [references/api.md](references/api.md)。
4. 版本更新、重连和来源核验：读 [references/sources.md](references/sources.md)。

## 必须保留的区别

- AMS Net ID、IP、ADS Runtime 端口是不同概念；851 是常见的第一个 TC3 PLC Runtime 端口，实际以配置为准。不要把示例地址当现场地址。
- `readValue()` 返回结果对象，实际值在 `.value`；`readRaw*()` 返回原始字节。Node.js 自动类型转换用的是目标 PLC 类型信息，不照搬 C# 编组规则。
- 包没有 `poll()` API；轮询由应用实现，避免异步 setInterval 堆积请求。
- 功能块成员按实例路径读写；执行方法需 `invokeRpcMethod()` 和 PLC 方法上的 `TcRpcEnable`，写成员本身不执行功能块。
- `readWriteRaw()` 是 ADS ReadWrite 服务，不等于写后回读确认；批量请求不保证跨变量原子快照或事务。
- 不把 C# ADS 技能中的性能数字、句柄限制、事件名、失效行为直接外推到 Node.js。
- 修改设备值、执行 RPC、启停/复位控制器及负载测试，应在用户给定目标和授权范围内进行；纯解释和本地代码准备不需设备操作。

## 交付与验证

回答应标明资料版本及第三方来源。写代码时区分本地语法检查、包接口核对和 PLC 联机验证；未联机不得声称通信测试通过。长期运行方案需考虑错误状态、退出清理、重连后数据新鲜度与订阅恢复，不能仅靠 TCP 连接状态宣称 PLC 正常。

## 适用环境

- Platform: Windows TwinCAT HMI engineering project.
- Dependencies: The existing project, its installed HMI controls, configuration and applicable TwinCAT HMI version.
- Validation: packaging and static checks dated 2026-11-12; no new device or HMI runtime verification.
- Boundary: preserve the original project assumptions; authorize device writes or configuration activation separately.
