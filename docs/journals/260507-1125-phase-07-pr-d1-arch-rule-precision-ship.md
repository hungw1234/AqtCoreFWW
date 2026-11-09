# Phase-07 PR-D1 — Arch rule R_T2FK precision via suppress directive

**Date:** 2026-05-07
**Branch / PR:** `feature/phase-07-pr-d1-arch-rule-precision` → PR #82 (squash `dd4d565f`)
**Effort:** ~2h elapsed (planning + impl + ship)

## What shipped

- New generic `// arch:suppress <RuleCode> <Entity>: <reason>` directive in `SourceCodeScanner` (mandatory entity + ≥3-char reason for forge-resistance)
- New 4-arg overload `ScanForPattern(pattern, ruleCode, entityExtractor, extraWhitelistFragments?)` — backwards-compat preserved
- 7 unit tests covering same-line / previous-line / wrong-rule / wrong-entity / missing-reason / distance>1 / valid happy path
- 8 `arch:suppress R_T2FK <Entity>: <reason>` annotations on deliberate-design lines (Files / Organization / Ai / Forms)
- `T2FkDriftBacklog` HashSet trimmed 7 → 3 (un-suppressed: FileRecord, OrgUnit, Message, FormTemplate; remaining: WorkflowDefinition, WorkflowInstance, Rule)

## Key decision: Re-scope from refactor → precision upgrade

Initial scope was "convert 8 long-FK leaks to Guid PublicId" (mirror PR-B Partner pattern). After planner verified each leak from source, **0 of 8 were real T2 external-surface drift** — all 8 mapped to 4 distinct false-positive patterns:

1. **T3 cross-aggregate FK by design** (Files DTOs, Phase-4b B3b)
2. **T1 child holding long FK to T2 parent** (Organization DTOs)
3. **Regex name collision** (`MessageId` resolves to T1 `AiChatMessage`, not T2 `Message`)
4. **Private inner Dapper materializer** (Forms private Row classes)

Pursuing the original scope would have unwound deliberate design + hit an impossibility wall on AiChatMessage (T1, no `PublicId`).

PR-C commit message had explicitly flagged this precision gap: *"line-based regex cannot distinguish ... fix via per-file suppress comment OR Roslyn AST upgrade."* Re-scoped to that exact precision fix.

## Lessons

- **Verify before refactor.** "22 leaks" backlog from PR-C was treated as uniform real drift. ~36% verified false-positive on first audit. Future cleanup waves (PR-D2/D3) must audit each leak before assuming refactor scope.
- **Plan-time verification > implementation-time discovery.** Planner spent ~3min verifying 4 entities at source — caught the assumption that would have blown up Phase 02-03. Cost: 3min planner time. Saved: hours of unwinding misguided refactors.
- **Generic grammar with one wired caller is the YAGNI sweet spot.** Directive parser accepts any `<RULE>` token, but only Rule_T2FK is wired. Future rules opt in by passing their own `ruleCode` — no parser change needed.
- **Forge-resistance via mandatory reason works.** Code-reviewer confirmed entity collision (`MessageThreadId` vs `MessageId`) impossible due to `\b` word boundary. Mandatory reason text + audit log create review-time visibility on each suppression.

## Follow-ups

- **PR-D2 (audit Workflow + Rule entities):** 14 pending leaks. Re-audit each: real drift vs deliberate design → either retier or annotate.
- **Code-reviewer M1:** `Console.WriteLine` audit log silently swallowed by xUnit default. Either weaken claim to "diagnostic trace" or wire `ITestOutputHelper`.
- **Code-reviewer M2:** `///` doc-comment lines also activate directive (regex matches `//` at pos 1). Document or tighten with `(?<!/)//`.
- **Rule6 retier wave (out of scope, pre-existing):** `ai.AiModelProfile`, `ai.AiPromptTemplate`, `identity.MfaPolicy` — T1 in CSV but `IPublicIdentified` at source.

## Validation evidence

- Architecture suite: 23/23 rules pass (1 pre-existing Rule6 fail on main HEAD)
- New tests: 7/7 pass
- Build: 0 warnings 0 errors
- Code review: 9.5/10 auto-approved (0 Critical, 0 High, 2 Medium → follow-ups)
- Reports: `plans/reports/tester-260507-1147-pr-d1-arch-validation.md`, `plans/reports/code-reviewer-260507-1125-pr-d1.md`
