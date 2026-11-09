# Microservices Transformation Master Roadmap — Consolidation to Durable SSOT

**Date**: 2026-07-10 15:30
**Severity**: Low (docs-only)
**Component**: Architecture / Documentation
**Status**: Resolved

## What Happened

Promoted microservices-transformation master plan from ephemeral gitignored `plans/reports/` + scattered backlog/memory into committed, durable, self-contained SSOT under `docs/`. Consolidated 4 files; verified all hash citations and architectural cross-links.

## Deliverables

1. **`docs/microservices-transformation-roadmap.md`** (NEW, 160 lines) — Master SSOT. Sections: mandate & context (greenfield compliance-isolation driver, NOT scale), 5-service target map (S1–S5), 8 locked architectural decisions (2026-07-06), 14-capability completeness matrix, wave sequencing (Sóng-0 → S4 → S5+S3 → S2 → S1), status + residual gaps.

2. **`docs/adr/012-activate-microservices-strangler-extraction.md`** (NEW) — Activation record. Frames as decision to activate Phase-2 option reserved in ADR-001, not a repudiation. Driver: compliance-isolation + greenfield window.

3. **`docs/adr/001-modular-monolith-over-microservices.md`** (EDIT, 1 line) — Status field: "Superseded by ADR-012".

4. **`docs/project-roadmap.md`** (EDIT) — Added cross-link pointer to microservices transformation doc.

## Technical Details

- English corpus (matches 11 existing ADRs).
- ADR-012 is the correct next number (009/010/011 taken; validation caught near-miss).
- Roadmap is self-contained (inline load-bearing facts, no external `plans/reports/` references).
- All git hashes verified via `git show -s` before inclusion.

## Discoveries

**Module coverage gap:** `search` module is unmapped in the 5-service target (16 module dirs vs 15 assigned). Flagged in roadmap §2 as a pre-extraction reconciliation point; real blocking is none, just a labeling omission.

**Hash mis-attribution:** Backlog/memory shorthand marks `abd4e079` as "Sóng-0 Phase-1 anchor" (a push-point marker). Verification revealed `abd4e079` is a signing seam commit (`IGuidGenerator`), NOT the outbox arc. Real arc: `e03d2a4e..2d927dce`. Roadmap cites the correct arc; backlog/memory shorthand left unchanged (defensible as push-marker, but now documented here).

## Lesson Learned

Durable SSOT docs demand hash-level precision that ephemeral backlog/memory shorthand does not. Habitual commit citations (e.g., "this hash marks the arc start") become factual mis-attributions when reframed as reference material. **Verify every git hash with `git show -s` before landing in a reference doc.**

## Verification

Code-reviewer APPROVE_WITH_NITS: 7/7 spec-checks pass. 2 nits found + fixed on-the-spot: (1) MEDIUM — corrected mis-cited arc hash; (2) LOW — completeness-matrix row #6 (authz) corrected `❌→✅` to `❌→⚠️` (open hard-cell, not shipped). Zero code touched, zero tests.

## Next Steps

- Backlog/memory maintainers: optional update to cite `e03d2a4e..2d927dce` for outbox arc clarity (low priority, shorthand is defensible).
- Pre-S4 extraction wave: reconcile `search` module assignment.
- Ongoing: ADR-012 becomes reference for strangler-activation decisions.
