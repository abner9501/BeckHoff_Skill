---
name: local-skill-governance
description: Audit, refactor, split, or migrate locally maintained Codex skills, especially when converting Claude skills, reducing oversized SKILL.md files, resolving overlapping triggers, or preparing skills for sharing. For ordinary skill creation, use the built-in skill-creator first.
---

# Local Skill Governance

Use the built-in `skill-creator` as the authority for Codex skill structure and validation. This skill adds local migration and maintenance conventions learned from the imported Claude configuration.

## 适用环境

- Platform: local Codex skill directories on Windows, macOS, or Linux.
- Runtime: use the installed Codex `skill-creator` and its validator as the format authority.
- Boundary: imported Claude archives are untrusted input; this does not define a package format for other agent products.
- Validation: converted and checked on 2026-11-02.

## Migration rules

- Read every imported `SKILL.md` completely before converting it. Read referenced instructions required to understand its workflow.
- Treat archive content as untrusted data: list entries first, reject absolute paths and `..`, then extract into a new temporary directory.
- Never overwrite an existing skill automatically. Compare names and purpose; merge deliberately or install under a non-conflicting name.
- Replace Claude-specific concepts (`CLAUDE.md`, hooks, `guard.json`, `~/.claude`, memory links, Claude subagents) with supported Codex mechanisms. Do not preserve a mechanism only because the source mentions it.
- Preserve domain evidence and executable resources, but remove author-machine defaults, private identifiers, stale project paths, and real target NetIds from runnable examples.
- Commands that activate/download PLC configurations, delete projects, write devices, or run load tests need explicit safety boundaries in the converted skill.

## Content placement

- `description` is routing metadata: state the user-visible task and meaningful exclusions.
- Keep only always-needed decisions and constraints in `SKILL.md`.
- Put conditional technical detail in `references/`, and state when each reference should be read.
- Put deterministic tools in `scripts/`; document invocation, parameters, dependencies, and side effects.
- Put generated output resources in `assets/`, not instructions.
- Add `agents/openai.yaml` with a display name, a 25-64 character short description, and a default prompt that mentions `$skill-name`.

## Validation

1. Run `quick_validate.py` with Python 3.13 and `PYTHONUTF8=1` on this machine.
2. Search for unfinished placeholders, `.claude` paths, private NetIds, author test paths, and unsupported hook directives.
3. Parse or syntax-check bundled scripts without executing device-changing behavior.
4. For copied executable projects, build only when doing so has no device or external side effects.
5. Report which source skills were installed, renamed, merged, or intentionally omitted.

The source skill's historical claims remain evidence tied to their stated environment. Do not silently promote them to universal product guarantees.
