# Phase 6 Slice D — Multi-Page Placement Picker Contract Defect at Code Review

**Date**: 2026-06-30 14:00
**Severity**: Critical
**Component**: Signature PAdES visible-stamp placement (FE + BE boundary)
**Status**: Resolved

## What Happened

Shipped Phase 6 Slice D: FE placement picker (coordinate geometry, react-pdf overlay, page nav), Files modal wired to Mysign remote PAdES signing, placement JSON → signing hook. Code reviewer (scouting BE source, not just FE diff) caught a silent-wrong-output defect at the contract boundary.

## The Brutal Truth

This hurt because the code looked clean on both sides. FE tests passed. BE tests passed. All gates green. But the defect lived in the gap between them: FE shipped a multi-page placement picker (page dropdown, emits `page: 2` in JSON), BE's `PdfIncrementalSignatureWriter` **always targeted first page** (`FirstPageObjectNumber`), and the parsed `SignaturePlacement.Page` was **silently dropped**. On a 2-page PDF, a stamp drawn on page 2 would land on page 1 — wrong output on a legal document, and neither side knew it. This is exactly the kind of silent fail that kills trust in an e-signature system.

## Technical Details

**FE placement JSON** (react-pdf overlay + Files modal):
```typescript
{ page: 2, x: 100, y: 150, width: 80, height: 40 }  // page 2 intended
```

**BE writer (before fix)**:
```csharp
var appearance = new SignatureAppearance { X = ..., Y = ..., Width = ..., Height = ... };
var pageNum = docModel.FirstPageObjectNumber;  // ALWAYS page 1
```

**Parser dropped the field**:
`SignaturePlacement.Page` existed, was parsed from JSON, then never passed to `SignatureAppearance`. Silent ignore = wrong output.

**Root cause**: Accept-and-ignore fields are a fail-open trap. FE shipped a capability (multi-page picker). BE accepted the payload but honoured only the first-page case. No compile-time contract, no runtime error. Silent wrong-output.

## What We Tried

Code reviewer (smart pick: reviewed BE source, not just FE diff) found it. User chose to fix BE rather than constrain FE UI (better UX). Added `SignatureAppearance.Page`, a fail-closed `PdfDocumentModel.PageObjectNumber(page)` (resolves `/Kids` flat tree, delegates page 1 to existing golden-byte path, rejects out-of-range / nested / non-leaf trees). Wired `placement.Page` through initiator. 3 new tests pinned behaviour (page 2 → obj 4, page 1 → obj 3, out-of-range fails closed). Full BE regression suite green.

## Root Cause Analysis

Two independent facts, both locally correct:
1. FE shipped a UI that the requirements and UX demanded.
2. BE had no explicit contract saying "Page must be honoured; if missing, fail-closed."

The gap: neither side validated the full contract. A "parse-and-ignore" approach looked safe (backward-compat) but silently broke a known field. Silent wrong-output is worse than an error.

## Lessons Learned

1. **Reviewers across boundaries catch what same-side review misses.** FE reviewer would never see the BE writer. Cross-boundary review (reviewer scouts BOTH sides) is non-optional for public contracts.

2. **Accept-and-ignore fields on known-but-unhonored properties is a silent-wrong-output trap.** Forward-compat tolerance of unknown fields is defensible. Tolerating KNOWN fields that aren't implemented is a defect. Answer: fail-closed parsing or explicit contract.

3. **Lazy-load heavy viewer deps.** react-pdf/pdfjs import in eager graph broke 3 unrelated jsdom tests (DOMMatrix undefined at collection time). Defer viewer deps so they never block feature test loading.

## Next Steps

- BE: `PdfDocumentModel.PageObjectNumber(page)` now supports any page in range; golden-byte path (page 1) reuses existing code.
- FE: placement picker pages now bind to actual PDF structure (no regression).
- Add to contract checklist: any FE field emitted to remote signing must have explicit BE handler + test.

---

**Status**: DONE

**Summary**: Code reviewer cross-boundary audit caught critical silent-wrong-output defect (FE multi-page placement picker vs BE first-page-only writer); fixed BE to honour page targeting with fail-closed validation.

**Journal**: C:\AqtCoreFW\docs\journals\journal-260630-1400-phase6-slice-d-placement-contract-defect.md
