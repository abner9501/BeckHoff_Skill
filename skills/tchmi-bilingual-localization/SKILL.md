---
name: tchmi-bilingual-localization
description: Maintain Chinese and English localization keys when adding or naming visible text in a TwinCAT HMI Plastic project. Use when creating or changing Chinese UI labels in such a project.
---

# TwinCAT HMI bilingual localization

When adding a Chinese UI label, locate the active project's `Packages/Beckhoff.TwinCAT.HMI.Plastic.Localizations.*/runtimes/native1.12-tchmi/Tc3_PlasticLocalizations/Lang/zh-CN.json` and `en.json`. Use project-relative paths, not machine-specific absolute paths.

Reuse an existing suitable key when possible; otherwise add the same meaningful key to both files under `localizedText`, with Chinese and English translations. Reference the key from the project using its existing `%l%Control::TcHmi.Controls.Plastic.Tc3_PlasticLocalizations::Key%/l%` convention instead of embedding Chinese text. Preserve existing translations and verify that both JSON files parse and contain the referenced key. Check the project's actual build and deployment localization paths when relevant.

Keep English labels as short as practical while preserving their meaning and avoiding ambiguous abbreviations. Ensure the full Chinese and English text fits its controls and surrounding layout; adjust the layout rather than sacrificing clarity to shorten a translation.

## 适用环境

- Platform: Windows TwinCAT HMI engineering project.
- Dependencies: The existing project, its installed HMI controls, configuration and applicable TwinCAT HMI version.
- Validation: packaging and static checks dated 2026-11-12; no new device or HMI runtime verification.
- Boundary: preserve the original project assumptions; authorize device writes or configuration activation separately.
