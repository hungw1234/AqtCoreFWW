# Priority Reversal → Stable-Monolith-First + P1 Readiness Audit

**Date**: 2026-07-16 18:15
**Severity**: High (strategic priority reversal — reverses the 2026-07-10 standing directive) + one dev-infra fix + a readiness audit
**Component**: strategy/priority; dev-DB (forms); FU-A auditor doc; operational-readiness audit
**Status**: All committed + pushed (tree clean); audit report written; wrap complete

## Session goal

Opened as "return to microservices work". Empirical check showed the microservices build-now CODE track was already EXHAUSTED (07-16) with the sole remaining action being FU-A (non-code). Session pivoted through four honest steps instead of fabricating microservices scope: (1) triage+fix the dev-DB boot blocker, (2) prep the FU-A auditor doc, (3) a user-decided **priority reversal** to stable-monolith-first, (4) a P1 operational-readiness audit to make "stable" a finite checklist.

## What worked (commits, all pushed)

- **`b8361dca`** — filed 07-16 db-instance direction row + triaged the dev-DB blocker (housekeeping wrap of prior session).
- **`99fc5cb0`** — resolved `FORMS-DEV-DB-DUPLICATE-PUBLIC-CODE-MIGRATION-BLOCK`. Source-proven it is NOT a seeder bug: `DemoFormTemplateSeeder` inserts `demo-intake-form` for both tenants as Active/`IsPublic=0`; the duplicate-public state came from the behavior-preserving backfill migration `20260709033410_AddFormTemplateIsPublic` (`SET IsPublic=1 WHERE Status='Active'`) acting on pre-existing demo rows in the long-lived dev DB. Fix = non-destructive demote (`UPDATE forms.FormTemplates SET IsPublic=0 WHERE Code='demo-intake-form'`, both tenant rows) — verified 0 dup public Codes, migration still pending → next boot applies the filtered unique index clean. No source change.
- **`f13190c7`** — tightened `docs/microservices-compliance-isolation-auditor-questions.md` (FU-A) to send-ready: added a compliance-vs-scale scope fence, a separate-DB-vs-separate-instance precision axis, and a governing-instrument anchor prompt.
- **`a0572c48`** — **PRIORITY REVERSAL** across backlog + project-roadmap + roadmap header + changelog. Memory updated in parallel (new `project-stable-monolith-first.md`, MEMORY.md STANDING PRIORITY flip, `project-microservices-top-priority.md` demoted with a SUPERSEDED banner).
- **P1 readiness audit** → `plans/reports/audit-260716-1805-stable-monolith-p1-operational-readiness-report.md` (gitignored). 2 parallel read-only Explore agents + controller spot-checks. 13 finite gaps, 2 genuine launch-blockers (automated backup+restore; integration-test CI gate).

## What didn't / lessons

- **Verify-subagent discipline paid off.** The observability agent claimed alerting notification is "dormant → prod silence". Spot-check of `AlertEvaluationJob.cs:150-192` disproved it: LOG + WEBHOOK channels are functional (webhook = SSRF-validated POST); only EMAIL is a stub. The gap narrowed from HIGH to LOW. [[feedback-verify-subagent-reports]]
- **"Return to microservices" ≠ there is microservices work.** The honest move when a top-priority track is code-exhausted + externally-gated is to say so and route to the real next step (decision/plan/ops), not manufacture a cook target. YAGNI + INVARIANT 260522-A.
- **Severity calibration is the controller's job.** Both audit agents rated some gaps HIGH that are lower for a *fresh-prod launch* (the migration guard bit dev on legacy data; fresh prod has none). Recalibrated in the report rather than parroting.

## Decisions made

- **PRIORITY REVERSAL (user 2026-07-16):** ship a **stable modular monolith for launch = #1**; microservices transformation **demoted to deferred/gated** (NOT cancelled), sequenced after the stable-monolith launch, gated on (a) launch + (b) FU-A auditor answer + (c) production load-evidence. Rationale: strangler-fig needs a stable base; reaching prod generates the two signals (FU-A answerability + load-evidence) that the extraction decision was already stuck on. Recorded to memory `project-stable-monolith-first.md` + all priority-bearing docs.
- **Dev-DB fix = demote both rows to private** (restore seeder-intended state, matches a fresh reseed) rather than reseed (which nukes all dev data) or demote-one.
- **P1 audit excludes functional-completeness** (P2) — the launch feature-set is user-TBD and cannot be sized from the codebase.

## Test status

N/A — no source-code changes this session (docs + memory + a dev-DB data fix + a read-only audit). Empirical verifications: dev-DB post-demote 0 dup public Codes; audit spot-checks (backup/RabbitMQ-health/alerting) via grep+read.

## Next session

1. **Define the launch feature-set (P2)** — the gating unknown for "stable"; blocks accurate P1 sizing.
2. **The 2 P1 launch-blockers** — automated backup + restore runbook; re-enable integration tests as a CI gate. Both extraction-independent, `/ck:plan` candidates.
3. **Answer the 5 §4 decisions** in the audit report (deploy target, prod seed posture, alerting bar, audit-iso-at-launch, CI-enforcement appetite) — each materially changes P1 effort.
4. **Residual:** confirm API boots after the dev-DB demote (`/aqt-runapp`); FU-A §7 prereqs (auditor identity + legal instrument) are user-owned.
