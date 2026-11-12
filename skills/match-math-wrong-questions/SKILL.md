---
name: match-math-wrong-questions
description: Analyze high-school math wrong-question files, summarize each problem's type, knowledge point, method pattern, and error cause, then find similar problems in a user-configured copy of the 2026 赵礼显暑秋一轮讲义 PDF. Use when the user asks to process 错题, classify mistake/problem types, or locate analogous questions in the 2026 赵礼显暑秋一轮讲义 PDF.
---

# 高中数学错题找相似题

## Overview

Use this skill to turn files in `<wrong-question-folder>` into a concise wrong-question diagnosis, then search the fixed source PDF `<source-pdf>` for genuinely similar training problems.

The task is not just keyword search. Match by knowledge point, mathematical structure, method, and trap/error pattern.

## Default Inputs

首次使用时确认错题目录和讲义 PDF 的实际位置。下列占位符必须替换为使用者自己的路径，讲义不随技能分发。

- Wrong-question folder: `<wrong-question-folder>`
- Similar-question source: `<source-pdf>`
- Expected wrong-question formats: images such as `.jpg/.jpeg/.png`, PDFs, Word files, Markdown, or plain text. If the wrong question is an image, visually read it first; transcribe the stem, options, diagrams, and visible student marks when possible.

## Workflow

1. Inventory the wrong-question folder.
   - List all files under `错题`.
   - Group multiple images that are visually part of the same question.
   - Preserve filenames in the final output so the user can trace every conclusion.

2. Read and normalize each wrong question.
   - For images, use visual inspection and OCR if available; do not invent unreadable text.
   - For PDFs, extract page images or text, then inspect the relevant pages.
   - Normalize formulas into readable Chinese math notation or LaTeX-like notation.
   - Capture diagrams qualitatively if exact geometry cannot be represented.

3. Classify the wrong question.
   - Identify the topic: function, derivative, trigonometry, sequence, vector, analytic geometry, solid geometry, probability/statistics, complex numbers, inequality, set/logic, etc.
   - Identify the sub-type: e.g. "导数求参/恒成立", "圆锥曲线定点定值", "三角恒等变换", "数列递推求通项", "概率分布列与期望".
   - Identify the solution method: substitution, monotonicity, discriminant, parameter separation, Vieta, coordinate setup, recurrence transformation, classification discussion, etc.
   - Identify the likely mistake cause: concept confusion, condition omission, computation error, method selection, hidden constraint, diagram reading, case split, endpoint check, or algebra deformation.

4. Search the source PDF for similar problems.
   - Extract or inspect the PDF page by page as needed.
   - Prefer exact section/lesson headings when available.
   - Search first by knowledge-point terms, then by structure and method.
   - If text extraction is poor, render candidate PDF pages to images and inspect visually.
   - Keep page numbers from the PDF viewer/extractor. If PDF page labels differ from printed page numbers, report both when visible.

5. Rank similarity.
   - High: same topic, same sub-type, same core transformation/method, and comparable difficulty.
   - Medium: same topic and method, but changed setting or fewer conditions.
   - Low: same knowledge point only; useful for remediation but not a true analog.
   - Prefer 2-5 matches per wrong question. Avoid padding with weak matches.

## Similarity Signals

Use these signals in order:

- Same mathematical object: quadratic/function/ellipse/hyperbola/sequence/vector/probability model.
- Same target: solve parameter range, prove inequality, find maximum/minimum, compute expectation, determine locus, prove fixed point/value.
- Same constraint pattern: `恒成立`, `存在性`, tangent/secant, monotonic interval, recursive relation, perpendicular/parallel, area/volume, conditional probability.
- Same key move: derivative sign chart, parameter separation, discriminant, Vieta relation, auxiliary angle, telescoping, coordinate method, normal vector, distribution table.
- Same common trap: missing domain, ignoring endpoint, wrong case split, denominator sign, root ordering, overcounting, independence assumption, diagram angle/length relation.

## Output Format

Return the result in Chinese. Use a table for the main mapping:

| 错题文件 | 错题类型 | 核心知识点 | 典型方法 | 易错原因 | 相似题位置 | 相似度 | 为什么相似 | 建议重练顺序 |
|---|---|---|---|---|---|---|---|---|

After the table, add:

- `错题类型总结`: merge repeated types across all wrong questions.
- `优先补救`: list the 1-3 most important weaknesses to fix first.
- `不确定项`: mention unreadable image areas, OCR uncertainty, or PDF pages that need manual confirmation.

## Quality Bar

- Do not call two questions similar only because they share a broad chapter.
- When the target PDF contains no strong match, say so and provide the closest weaker drills separately.
- Cite enough context for the user to find the match: PDF filename, page number, printed page number if visible, section/title if visible, and a short paraphrase of the matched stem.
- Do not provide long verbatim excerpts from the PDF; paraphrase the matched question and quote only short fragments when necessary.

## 适用环境

- Platform: An environment able to inspect PDF, image and document files.
- Dependencies: User-configured wrong-question folder and a separately obtained 2026 赵礼显暑秋一轮讲义 PDF.
- Validation: packaging and static checks dated 2026-11-12; no new device or HMI runtime verification.
- Boundary: preserve the original project assumptions; authorize device writes or configuration activation separately.
