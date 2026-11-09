# Signature Phase 5 Slice 2: PAdES Visible Stamp Wire (Viettel Mysign Single-Signer Path)

**Date**: 2026-06-30 14:00  
**Severity**: High (design fork + dependency feasibility gate + integration wire)  
**Component**: Identity.Application; PadesTransactionInitiator, signature placement, stamp rasterizer  
**Status**: DONE (1 commit pushed; code-review DONE_WITH_CONCERNS, 0 Critical)

## What Happened

Phase 5 Slice 2 (plan 260623-1950) completed: re-verified a 1-day-old plan, resolved 2 design forks (CA binding vs separate read, flatten feasibility), executed integration wire, shipped placement parser + rasterizer + initiator binding. All tests GREEN: placement 11, rasterizer 2 (real QuestPDF JPEG baseline), 3 wire tests, 43 PAdES regression, arch 149/149, full build.

## The Brutal Truth

Scout caught plan drift hard: `ProfileRepository` and `EnrollmentRepository` never injected into `PadesTransactionInitiator` — had to add resolution before proceeding. QuestPDF spike emits PNG; the PAdES writer expects baseline JPEG — format gap, painful discovery mid-slice. Design fork on flatten (a plan deliverable) escalated to infeasibility: hand-rolled flatten impossible (PDF model is minimal incremental reader, no object-graph rewrite); PDFsharp adds zero value (no Flatten API); iText forbidden (AGPL). Constraint ("no new PDF lib") + hard facts = flatten genuinely infeasible, not just deferred. That stings because the plan promised it, but shipping a degraded "strip" loses visual content. Had to surface that honestly and pivot the contract. Worry spike mid-review: "does the stamp even show?" — clarified that flatten ≠ stamp visibility; stamp ships and displays; flatten only concerns pre-existing forms in input PDF.

## Technical Details

**Scout re-verify findings (1-day-old plan drift):**
- Missing injection: `ProfileRepository`, `EnrollmentRepository` absent from `PadesTransactionInitiator` ctor. Added resolution via DI.
- Format gap: QuestPDF spike outputs PNG; PAdES writer baseline expects JPEG (PDF spec). Confirmed empirically via rasterizer tests.
- `/Annots` guard line moved in plan narrative → re-read OnModelCreating to verify current DbSet order.

**Implementation:**
- **SignaturePlacement (forward-compat parser):** Reads dormant `SigningRequest.Placement` field; parses zone/offset; first consumer. Zero-cost forward-compat gate.
- **SignatureStampRasterizer:** QuestPDF → JPEG baseline via `QuestPdfExporter.EnsureInitialized()` (reused; inline const read does NOT trigger static ctor license-check). 2 tests: QuestPDF invoked directly, JPEG magic verified.
- **IUserSignatureImageStore adapter wire:** `GetAsync` on image store; module isolation preserved; returns byte[], composed + rasterized at initiate time.
- **PadesTransactionInitiator binding:** placement → profile M4-ownership → fetch image → compose → raster → freeze appearance at initiate (no mutation after). 3 wire tests cover happy + missing-image + no-profile-image-yet.

**Verification:**
- Placement logic: 11 unit tests (zone calc, offset bounds, null-safe).
- Rasterizer: 2 tests (QuestPDF call + baseline JPEG detection via magic bytes).
- Wire integration: 3 tests (happy, missing image, profile has no image slot).
- Regression: 43 PAdES (no appearance breakage, all pass).
- Architecture: 149/149.
- Code-review: DONE_WITH_CONCERNS (0 Critical; H1 musl-raster = existing P7 Alpine smoke; M1 typed-error + L1 log + L2 comment fixed before push).

## What We Tried

1. **CA source path (2 design options):**
   - Path A (chosen): Serial/CA read via binding-gated `bcCert` (KISS) — cert already gated to signer's Approved enrollment by binding guard.
   - Path B (rejected): Separate `EnrollmentRepository.GetAsync()` read — redundant, binding guard already enforces invariant.
   - User approved Path A (no separate read).

2. **Flatten feasibility (escalation path):**
   - Option 1 (attempt): Hand-rolled flatten (read stream, parse objects, rewrite references) — PDF model is incremental-reader, no object-graph builder; infeasible.
   - Option 2 (spike): PDFsharp library — spike found: zero Flatten API; Alpine font resolver flaky ("works sometimes").
   - Option 3 (spike): iText — AGPL license banned by org policy.
   - Outcome: Flatten genuinely infeasible under "no new PDF lib" constraint. Deferred to clean-PDF precondition + follow-up. Plan must be amended: flatten removed from deliverables; stamp+placement shipped.

3. **Stamp visibility fear (mid-review):**
   - Question: "Will the stamp show if we don't flatten?"
   - Answer: Yes. Flatten ≠ stamp visibility. Flatten only affects pre-existing interactive form fields in input PDF (makes them non-interactive). Stamp appearance is frozen as `/AP[/N]` XObject; displays independent of form field state. Clarified + fear resolved.

## Root Cause Analysis

**Plan drift (1-day decay):** Injection oversight in plan narrative; re-verified ctor signature empirically before proceeding. Caught early via scout.

**Format gap (QuestPDF → JPEG):** Spike output PNG; standard reader expects baseline JPEG. PAdES spec does not forbid PNG, but writer's `ImageProvider.GetImage()` uses baseline-JPEG only. Verified via test magic-byte check.

**Flatten infeasibility:** Constraint ("no new PDF lib") is hard; feasible paths all blocked (hand-roll = no graph builder, PDFsharp = no API, iText = banned). Not a deferral; a real delivery boundary. Plan claimed flatten as deliverable; must surface that constraint makes it infeasible, not just slow.

## Lessons Learned

1. **Feasibility-verify a dependency BEFORE committing to it.** PDFsharp spike took 1h; if we had shipped "add PDFsharp" in Phase 4 planning without spiking, this slice would have derailed. Cost/benefit: 1h spike saves wasted 4h implementation + integration + rework.

2. **A constraint can make a plan-listed deliverable genuinely infeasible.** Don't ship a degraded "strip" that loses visual content (flatten removing forms). Amend the contract honestly: spell out what's feasible + why. User can then choose to relax the constraint or accept the boundary.

3. **Stamp visibility ≠ flatten.** Appearance frozen as XObject displays independent of form interactivity. Flatten is cosmetic (input form UX); stamp is functional (signature display). Decouple the mental models.

4. **Serial/CA binding-gated fetch (KISS).** Cert already constrained to Approved enrollment by binding guard; separate read is noise. One reader path, one guard point, one trust invariant.

## Next Steps

1. **Commit message:** Cites plan 260623-1950 Slice 2; notes flatten deferred + constraint analysis.
2. **Amend plan 260623-1950:** Remove flatten from Phase 5 deliverables; document constraint analysis; note clean-PDF precondition follow-up.
3. **Follow-up task:** `SIGN-PADES-FLATTEN-PRECONDITION` — accept clean PDFs only (no pre-existing forms) or escalate to separate tool. Track in backlog as "never" unless constraint lifted.

## Unresolved Questions

- **Alpine font resolution in XObject rendering (Slice 3+):** Will QuestPDF composite Vietnamese diacritics + glyphs on Alpine with default sans-serif fallback? (Deferred to Slice 3; plan assumed embedded font path, needs spike.)
- **Clean-PDF precondition scope:** Input validation error or operator manual step? (Product decision; follow-up spike needed.)
