# Journal — Arch R1 fix: route signing/files id-gen through IGuidGenerator seam (2026-07-07)

**Row:** `ARCH-R1-RAW-GUIDNEWGUID-SIGNING-FILES` (T3). **Trigger:** Sóng-0 Wave-0 release-gate verification.

## What
`SourceLevelRulesTests.Rule1_GuidNewGuid` RED on `main` — 6 signing/files source sites raw `Guid.NewGuid()` (not `IGuidGenerator` seam). Allowlist grew to 14 modules; CI parked since April → red lọt. Routed all 6 qua seam. Behavior-preserving.

## Discovery path (INVARIANT 260522-A saved 3 phantom rows)
Tester full-repo run báo 18 fail, categorization mơ hồ. Verify isolated:
- **1 red THẬT:** `Rule1_GuidNewGuid` (arch, deterministic, no-DB, 520ms) — 6 concrete violations.
- **3 phantom/false-red KHÔNG file:** WorkflowTaxonomies-dupkey (MasterData 144/144 green isolated) · datetime2-precision (0 test tồn tại — hallucinate) · Hangfire-smoke (infra false-red). MASSTRANSIT-FAULT đã tracked.
Lần 5 tester over-report session này → verify từng claimed-red isolated là ground-truth.

## Fix decisions
- **V4-vs-V7 (tests KHÔNG guard):** 3 domain id (`SignerCertEnrollment.PublicId`, `UserSignatureProfile.PublicId`, `SignerEnrollmentNonce.Id`) **cố ý V4-random** (comment: no timing-leak via v7 timestamp) → `gen.NewIdRandom()`, KHÔNG monotonic. Swap nhầm = security regression im lặng. Handler docId → `NewIdMonotonic()`; blob-suffix → `NewIdRandom()`.
- **Fork thread-vs-suppress:** chọn thread `IGuidGenerator` (design-đúng: `NewIdRandom()` sinh ra cho case V4-entity-id; whitelist chỉ dành non-entity random per precedent). Cost ~36 caller edit, mechanical.
- **NSubstitute footgun** (subagent phát hiện): nested factory-call trong `.Returns(...)` corrupt NSubstitute per-thread last-call state → `CouldNotSetReturnDueToTypeMismatchException` (4 test fail). Fix = hand-rolled `IGuidGenerator` double thay mock, 16 file.

## Scope + gates
35 file (10 prod + 25 test). 3 factory +param, 6 handler +ctor-dep (DI singleton `AddSharedKernelGuidGenerator()`), ~25 test call-site. 0 logic change, dependency-map no-change.
- Build 0W/0E · `SourceLevelRulesTests` 12/12 · Identity.Integration signing 174/174 · unit Files.App 144/Forms.App 320/Identity.Domain 173/Identity.App 426/Identity.Infra 339 green.
- code-review APPROVE_WITH_NITS (3 dead-using + 1 stale-comment fixed).

## Verify độc lập (trust-but-verify)
Build + arch-oracle + behavior-grep (NewIdRandom) + DB-integration + riskiest suite tự chạy lại — không chỉ trust subagent report.
