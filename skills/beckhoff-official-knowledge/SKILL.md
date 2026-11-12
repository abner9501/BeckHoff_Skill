---
name: beckhoff-official-knowledge
description: Search and use the local Beckhoff official PDF library at a user-configured location for TwinCAT, EtherCAT, I/O terminals, IPC, Embedded PC, automation, product catalog, and application-note questions. Use when an answer should be grounded in Beckhoff manuals rather than memory.
---

# Beckhoff Official Knowledge

Treat the directory selected by `BECKHOFF_LIBRARY_ROOT` or `--root` as the preferred local source for Beckhoff product and engineering questions. The library remains external to the skill; do not copy or modify its PDFs.

## Retrieval workflow

1. Run `scripts/search.py` with the exact model number and smallest useful technical terms. Prefer identifiers such as `EL3318`, `CX2043`, or `TwinCAT ADS MQTT` over broad descriptions.
2. Inspect the highest-scoring paths. Prefer the user's language when equivalent manuals exist; otherwise prefer English. Distinguish product manuals, quick-start guides, catalogs, and application notes.
3. Open and extract relevant PDF pages with the PDF tooling available in the current environment. For diagrams, wiring, tables, or layout-sensitive claims, render and inspect pages instead of relying only on extracted text.
4. Ground the answer in actual PDF content. Cite every PDF used with an absolute local-file citation. State when the library does not establish the answer.
5. For configuration or code changes, use the manual to verify behavior, but inspect the target project before editing. A manual is evidence, not authorization to change unrelated files.

## Search commands

Python 3 and `pypdf` are required for indexing (`python -m pip install pypdf`). The PDF library is an external dependency; obtain the manuals separately. This package excludes the generated local index. Set the library path and build the index before the first search.

Run from this skill directory, replacing the placeholder with your PDF library:

```powershell
$env:BECKHOFF_LIBRARY_ROOT = "<your-beckhoff-pdf-directory>"
python scripts/build_index.py --root "$env:BECKHOFF_LIBRARY_ROOT"
python scripts/search.py "EL3318 thermocouple" --root "$env:BECKHOFF_LIBRARY_ROOT"
```

The index at `references/index.jsonl` is generated locally. Use `--limit 20` when filenames are ambiguous. Rebuild after PDFs are added, removed, renamed, or replaced. `--resume` preserves existing records and is suitable only for an interrupted build when existing PDF paths and contents have not changed.

## Source priorities

- Exact product manual for hardware limits, wiring, process data, diagnostics, and commissioning.
- TwinCAT product or function manual for software behavior and APIs.
- Application notes for worked examples and integration patterns.
- Catalogs for selection and overview, not as the sole source for detailed implementation claims.
- Marketing/news PDFs only for announcements or product positioning.

Do not claim this library is model training or permanent memory. It is a local, automatically discoverable retrieval source loaded only when relevant.

## 适用环境

- Platform: Windows, macOS or Linux with Python 3.
- Dependencies: Python pypdf for indexing; an external Beckhoff PDF library configured with --root or BECKHOFF_LIBRARY_ROOT.
- Validation: packaging and static checks dated 2026-11-12; no new device or HMI runtime verification.
- Boundary: preserve the original project assumptions; authorize device writes or configuration activation separately.
