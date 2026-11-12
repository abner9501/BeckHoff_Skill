# Codex 本地技能集

包含 13 个本地维护的技能及其脚本、参考资料。打包日期：2026-11-12。

## 技能目录

| Skill |
|---|
| `beckhoff-official-knowledge` |
| `fb-iomap` |
| `iodisplay` |
| `local-skill-governance` |
| `match-math-wrong-questions` |
| `skill-sharing` |
| `tchmi-bilingual-localization` |
| `tchmi-inputbox-grid` |
| `tchmi-parameter-settings` |
| `tchmi-symbol-mapping` |
| `twincat3-ads-csharp` |
| `twincat3-ads-nodejs` |
| `twincat3-build-verify` |

## 安装

将 `skills/` 下需要的整个技能目录复制到本机 Codex 的技能目录（通常是 `~/.codex/skills/`）。如果已存在同名目录，先比较和备份再合并。重新打开会话后检查是否识别。

倍福文档库和数学讲义是外部依赖，不随仓库分发。倍福检索技能第一次使用前必须配置资料目录并运行索引脚本。各脚本的具体用法和依赖见对应 SKILL.md。

## 分享前的处理

个人绝对路径和项目路径已替换为可配置参数或占位符；移除了 Python 缓存及本地生成的 PDF 索引。本机安装的原始技能没有改动。

TwinCAT 标准安装路径、版本记录和本机回环地址保留，便于解释原有技术验证条件。PLC 写入、下载、重启、删除和负载测试脚本仅进行了静态检查；使用这些功能须明确选择目标并获得相应授权。

格式和静态检查不代表跨版本、跨设备或项目运行验证。项目专用布局及 FB_IOMap 规则需要按实际项目复核。

官方与插件附带的技能列在 BUNDLED-SKILLS.md，使用其原安装来源恢复。
