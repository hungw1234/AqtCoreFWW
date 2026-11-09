# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Role & Responsibilities

Your role is to analyze user requirements, delegate tasks to appropriate sub-agents, and ensure cohesive delivery of features that meet specifications and architectural standards.

## Workflows

- Primary workflow: `~/.claude/rules/primary-workflow.md`
- Development rules: `~/.claude/rules/development-rules.md`
- Orchestration protocols: `~/.claude/rules/orchestration-protocol.md`
- Documentation management: `~/.claude/rules/documentation-management.md`
- And other workflows: `~/.claude/rules/*`

**IMPORTANT:** Analyze the skills catalog and activate the skills that are needed for the task during the process.
**IMPORTANT:** You must follow strictly the development rules in `~/.claude/rules/development-rules.md` file.
**IMPORTANT:** Before you plan or proceed any implementation, always read the `./README.md` file first to get context.
**IMPORTANT:** Sacrifice grammar for the sake of concision when writing reports.
**IMPORTANT:** In reports, list any unresolved questions at the end, if any.

## Hook Response Protocol

### Privacy Block Hook (`@@PRIVACY_PROMPT@@`)

When a tool call is blocked by the privacy-block hook, the output contains a JSON marker between `@@PRIVACY_PROMPT_START@@` and `@@PRIVACY_PROMPT_END@@`. **You MUST use the `AskUserQuestion` tool** to get proper user approval.

**Required Flow:**

1. Parse the JSON from the hook output
2. Use `AskUserQuestion` with the question data from the JSON
3. Based on user's selection:
   - **"Yes, approve access"** → Use `bash cat "filepath"` to read the file (bash is auto-approved)
   - **"No, skip this file"** → Continue without accessing the file

**Example AskUserQuestion call:**
```json
{
  "questions": [{
    "question": "I need to read \".env\" which may contain sensitive data. Do you approve?",
    "header": "File Access",
    "options": [
      { "label": "Yes, approve access", "description": "Allow reading .env this time" },
      { "label": "No, skip this file", "description": "Continue without accessing this file" }
    ],
    "multiSelect": false
  }]
}
```

**IMPORTANT:** Always ask the user via `AskUserQuestion` first. Never try to work around the privacy block without explicit user approval.

## Python Scripts (Skills)

When running Python scripts from `.claude/skills/`, use the venv Python interpreter:
- **Linux/macOS:** `.claude/skills/.venv/bin/python3 scripts/xxx.py`
- **Windows:** `.claude\skills\.venv\Scripts\python.exe scripts\xxx.py`

This ensures packages installed by `install.sh` (google-genai, pypdf, etc.) are available.

**IMPORTANT:** When scripts of skills failed, don't stop, try to fix them directly.

## [IMPORTANT] Consider Modularization
- If a code file exceeds 200 lines of code, consider modularizing it
- Check existing modules before creating new
- Analyze logical separation boundaries (functions, classes, concerns)
- Use kebab-case naming with long descriptive names, it's fine if the file name is long because this ensures file names are self-documenting for LLM tools (Grep, Glob, Search)
- Write descriptive code comments
- After modularization, continue with main task
- When not to modularize: Markdown files, plain text files, bash scripts, configuration files, environment variables files, etc.

## Dependency Map (Impact Analysis)

**`./docs/dependency-map.md`** is the single source of truth for module relationships, entity maps, and FE↔BE contracts.

**MUST READ** before:
- Adding/modifying entities, DbContexts, or migrations
- Adding/modifying cross-module client interfaces
- Adding/modifying controllers or API endpoints
- Adding/modifying FE features or API hooks

**MUST UPDATE** after any task that:
- Adds/removes/renames entities or DbContext DbSets
- Adds/removes cross-module dependencies (I*ModuleClient)
- Adds/removes controllers, endpoints, or FE features
- Changes background jobs or event handlers

## Backlog Row Audit-at-Slice-Start (INVARIANT 260522-A)

**Before opening any backlog row claimed `pending` / `ready` (>5d old), MUST audit ground-truth empirically — backlog snapshots decay faster than ship cadence.**

**Why:** 5× in week of 2026-05-23, a backlog row's claimed status was contradicted by the actual repo state:
- FE-FU 4-A-A (PR #114 carry-fwd) — closed-audit-clean (zero FE consumers per `audit-260522-1530-fe-instance-manage-callers.md`)
- P6-DEFER-2 (4C-1 EF model-cache) — WONTFIX-YAGNI per scout (0 fixture substitutes for `ITenantContext`)
- T3-BP-FORMMAP-BACKFILL — BE DTO + handler + FE consumer all shipped 2026-05-18 BUG2 fix (close-note commit `025f477f`)
- WFE-AUTH-FAIL — 2 originally-named tests silently fixed; 2 unnamed siblings in same file still failing (real-fix commit `155282b9`)
- T5-16-FOLLOWUP — Playwright `@smoke` spec shipped silently in API-only mode (partial-close commit `0e9b41a4`)

**Checklist (canonical, MUST execute in order):**
1. **Read the row verbatim** — note claimed file paths, identifiers, status field, age (row-date vs today)
2. **Empirical probe** — `grep` / file-exists / filtered test-run on the SPECIFIC identifier the row names; do NOT trust the row's claim
3. **Categorize** — (a) stale (close-note + `docs(backlog)` commit) / (b) partial-stale (update row + carry remainder) / (c) real-blocked (re-affirm block reason with evidence) / (d) real-ready (proceed to plan)
4. **Document-then-impl** — write the audit verdict in the commit message regardless of outcome; never silent-close, never silent-fix

**When this kicks in:** EVERY `/aqt-next` recommendation that surfaces a backlog row + EVERY `/cook <row-id>` invocation. The 5-min audit is cheaper than a wasted scope-opening.

**Reference:** memory anchor `[[phase-04a-a-invariant-closure-260522]]` 260522-A (origin) + 5 reinforcement instances all in week of 2026-05-23.

## Documentation Management

We keep all important docs in `./docs` folder and keep updating them, structure like below:

```
./docs
├── project-overview-pdr.md
├── code-standards.md
├── codebase-summary.md
├── dependency-map.md          # Module relationships, entity maps, FE↔BE contracts
├── design-guidelines.md
├── deployment-guide.md
├── system-architecture.md
└── project-roadmap.md
```

**IMPORTANT:** *MUST READ* and *MUST COMPLY* all *INSTRUCTIONS* in project `./CLAUDE.md`, especially *WORKFLOWS* section is *CRITICALLY IMPORTANT*, this rule is *MANDATORY. NON-NEGOTIABLE. NO EXCEPTIONS. MUST REMEMBER AT ALL TIMES!!!*