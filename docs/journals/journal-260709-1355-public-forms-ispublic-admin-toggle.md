# Session: FormTemplate.IsPublic Admin Toggle UI

**Date:** 2026-07-09 ~13:55 ICT  
**Severity:** Medium (feature completion, zero security regression)  
**Component:** Forms.API (PATCH /templates/{id}/public-access), FE (form-settings-panel.tsx)  
**Status:** Resolved

---

## What Happened

T4 backlog row `PUBLIC-FORMS-ISPUBLIC-ADMIN-TOGGLE` surfaced from /aqt-next as highest-coherence follow-up to the RLS carve-out shipped in 0af5e7dc. Without this toggle, new form templates default `IsPublic=0` and are never anonymously reachable—leaving the carve-out stranded, untested by real ops. /cook interactive invoked; scout verified FE admin surface (`form-settings-panel.tsx`) exists before committing to BE-only build.

---

## The Brutal Truth

This felt like a straightforward "flip a flag" follow-up, but the regression trap nearly bit us. The FE test suite mocks the whole `form-api` module—when the settings panel added a new hook import (`useSetTemplatePublicAccess`), that import would silently resolve to `undefined` in the mock context. Running the existing 4 settings-panel tests would have passed (no call to the hook yet), but the first real form-settings-panel instantiation in E2E would have crashed. The catch was proactive: grep the test mock's export list before finalizing the FE PR.

The other near-miss was trivial but instructive: GET single-template projection didn't include `IsPublic`. The toggle couldn't reflect actual state until the handler added it. Audit also surfaced pre-existing gaps (`consent`, `workflow` fields missing)—flagged for future, left out of scope per YAGNI.

---

## Technical Details

**BE Decisions (user-chosen):**
- Dedicated `PATCH /templates/{id}/public-access` endpoint (not folded into PUT Update). Isolates the security-sensitive flag; mirrors the established `/publish` action-endpoint pattern.
- Confirm-dialog on ENABLE only (anonymous access = opt-in ceremony). Disable is silent (no risk).
- Reuse `Forms.Template.Manage` permission (YAGNI, consistent with admin claims).
- Toggle allowed at any Status (flag independent; anon-read still requires Active+IsPublic=1).

**Contract Safety:**
- IsPublic added as trailing optional DTO param #14. Grep verified all 3 positional `new FormTemplateDto(` call sites in src/ safe (stop at arg 10). No blast-radius.

**FE Regression:**
- form-api module mock export list was missing `setTemplatePublicAccess`. Updated mock before FE build.
- Cross-tenant 404 integration test added post-review (acceptance criterion nit).

---

## Lessons Learned

**1. Whole-module test mocks are silent regression traps.** When a component gains a new hook import, the mock must export it or the test passes (unused import) but runtime crashes. Always grep the test mock's export list against new component imports.

**2. Verify FE consumer before BE build works.** Backlog audit found the real admin surface in repo—made this a safe follow-up, not speculative. Saved time vs. building BE-only and guessing at FE needs.

**3. GET single-template projection gaps should be logged, not fixed inline.** Found that consent/workflow missing too; flagged both in a doc comment. One follow-up ticket keeps scope focused.

---

## Quality Gates

- **BE build:** 0 warnings, 0 errors ✓
- **BE integration:** 13/13 (4 new, 9 existing, 0 regression) ✓
- **FE tests:** 18/18 ✓
- **Typecheck:** 0 errors ✓
- **Code review:** DONE (0 Critical/High, mass-assignment boundary verified) ✓

---

**Status:** DONE  
**Summary:** Admin toggle for FormTemplate.IsPublic completed. Caught FE mock regression proactively; GET projection gap audited + flagged. All tests green, review approved.  
**Journal:** `/AqtCoreFW/docs/journals/journal-260709-1355-public-forms-ispublic-admin-toggle.md`
