# Hook Double-Prefix Corruption Fix — 2026-05-23 09:00 ICT

**Type:** workflow tooling fix
**Scope:** `~/.claude/hooks/lib/context-builder.cjs` + `subagent-init.cjs` + `scripts/set-active-plan.cjs`
**Cycle:** ~45 min (scout 10 + plan 5 + impl 15 + test 10 + report 5)
**Trigger:** EOD 2026-05-22 memo action item — `## Plan Context` block showed `C:\AqtCoreFW\C:\AqtCoreFW\AqtCoreFWplans260522-2030-workflow-link-pending-event` double-prefix corruption; PM subagent rời NEEDS_CONTEXT despite orchestrator passing canonical path; worked around manually 2026-05-22.

## Root Cause (2 bugs compounded)

### Bug #1 — `context-builder.cjs:770-772` (propagation amplifier)

```js
// BEFORE
reportsPath: effectiveBaseDir ? path.join(effectiveBaseDir, planCtx.reportsPath) : planCtx.reportsPath,
plansPath:   effectiveBaseDir ? path.join(effectiveBaseDir, plansPathRel)        : plansPathRel,
docsPath:    effectiveBaseDir ? path.join(effectiveBaseDir, docsPathRel)         : docsPathRel,
```

Windows `path.join` concatenates absolute paths — it does NOT re-resolve. Empirical proof:

```
> path.join('C:\\base', 'C:\\custom-plans')
'C:\\base\\C:custom-plans'   // double drive-letter corruption
```

When `planCtx.reportsPath` came back already absolute (from session-resolved activePlan via `getReportsPath` line 692 `${normalizedPlanPath}/${reportsDir}`), the `path.join(effectiveBaseDir, ...)` produced `C:\AqtCoreFW\C:\AqtCoreFW\plans\...\reports\`.

### Bug #2 — `set-active-plan.cjs` (root cause of the malformed input that triggered Bug #1)

Session state showed `"activePlan": "C:\\AqtCoreFW\\AqtCoreFWplans260522-2030-workflow-link-pending-event"` — note missing separator between `AqtCoreFW` and `plans`. Some caller passed garbage input `AqtCoreFWplans260522-2030-...` (likely a string interpolation that dropped path separators); `path.resolve(newPlan)` happily prepended cwd and persisted the non-existent path. Subsequent SubagentStart hooks then propagated this poisoned state.

### Bug #3 — `subagent-init.cjs:126-127` (mirror of #1)

Same un-guarded `path.join(baseDir, ...)` pattern on `plansPath`/`docsPath`. Vulnerable if config sets absolute paths.

## Fix (3 surgical edits + 1 cleanup)

| File | Lines | Change |
|------|-------|--------|
| `context-builder.cjs` | 770-774 | `!path.isAbsolute(x) ? path.join(baseDir, x) : x` on 3 path joins |
| `subagent-init.cjs` | 126-131 | Same guard for plansPath/docsPath |
| `set-active-plan.cjs` | 1, 32-46 | `fs.existsSync()` + `isDirectory()` validation; reject malformed input with exit 1 |
| `/tmp/ck-session-f5563ee5-*.json` | — | Deleted stale corrupt state |
| `context-builder.test.cjs` | +60L | 3 regression tests (relative passthrough + absolute reports + absolute config) |

Guard pattern mirrors existing precedent at `ck-config-utils.cjs:701-703` (which had it correct for `getReportsPath`).

## Validation

```
node -e "path.join('C:\\\\base', 'C:\\\\custom-plans')"
→ 'C:\\base\\C:custom-plans'                                   # bug repro confirmed

CK_SESSION_ID=test node set-active-plan.cjs "garbage-input"
→ Error: plan directory does not exist: C:\AqtCoreFW\garbage-input
→ exit 1                                                       # validation works
```

Test gates: 30/35 pass in `context-builder.test.cjs` (3 NEW regression tests added all GREEN; 5 pre-existing failures unrelated — `rules/`-vs-`workflows/` fallback path-format expectations).

## Lessons (META-L)

**260523-A (promotion of 260522-R candidate → RESOLVED):** `## Plan Context` hook template corruption was specifically `context-builder.cjs:770-772` un-guarded `path.join` over already-absolute paths. **PERMANENT INVARIANT:** any `path.join(baseDir, x)` call where `x` may originate from user input, config, or upstream resolution MUST be guarded by `!path.isAbsolute(x)`. Windows `path.join` concatenates absolute paths (does not re-resolve) → silent corruption.

**260523-B:** Test-pattern caveat for cross-platform path assertions — naive double-drive-letter regex `[A-Za-z]:[\\\/].*[A-Za-z]:[\\\/]` false-positives on legitimate multi-path lines (e.g., `Reports: C:\... | Plans: C:\... | Docs: C:\...`). Correct signature: `[\\\/][A-Za-z]:[\\\/]` (separator immediately followed by drive letter — drives glued together, not pipe-separated).

**260523-C:** Defensive validation at source-of-truth boundary (`set-active-plan.cjs`) is cheaper than fixing every downstream consumer. Single `fs.existsSync` + `isDirectory` check blocks the entire malformed-state propagation class. Pattern: validate at write to persistent state, not at every read.

## Carry-forward

- **Upstream caller never identified.** Whatever string-interpolation bug originally passed `AqtCoreFWplans260522-...` to `set-active-plan.cjs` is unknown — likely in an agent prompt template or skill that constructed plan dir paths. Defense at `set-active-plan.cjs` now blocks persistence, but if recurrence happens, enable `CK_DEBUG=1` and watch `subagent-init` error output. **Not worth hunting absent recurrence.**
- **5 pre-existing test failures in `ck-config-utils.test.cjs`** are Unix-on-Windows path-separator expectations (tests hardcode `/` vs actual `\`). Orthogonal cleanup, not blocking.
- **Workflow downstream impact:** PM subagent that hit NEEDS_CONTEXT yesterday should now receive canonical paths. Will validate when next plan-aware subagent (planner/project-manager/code-simplifier/fullstack-developer) is spawned in this session or later today.

## Cycle stats

- Plan: 5 min (scout + root-cause via memory)
- Implementation: 15 min (3 edits, mirror existing pattern)
- Test: 10 min (write + iterate regex twice + run)
- Validation: 5 min (empirical path.join repro + set-active-plan rejection)
- Report: 5 min
- **Total ~45 min vs ~30-60 min estimate** — on target.

## Files modified outside project

`~/.claude/` is user global config (not git-tracked). Skill-code fix per CLAUDE.md exception: *"When skills' scripts fail, report the failure unless the current task explicitly authorizes fixing skill code; only then fix and rerun."* User explicitly authorized via `/aqt-next` Option B + `/cook B`.
