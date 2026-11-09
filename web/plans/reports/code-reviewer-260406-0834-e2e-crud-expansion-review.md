# E2E CRUD Expansion — Code Review Report

**Date:** 2026-04-06  
**Reviewer:** code-reviewer  
**Scope:** 5 spec files (new + modified), 2 supporting POM/helper files (context only)

---

## File Scores

| File | LOC | Score | Notes |
|------|-----|-------|-------|
| `admin-identity-crud.spec.ts` | 190 | **6/10** | Dead code, no cleanup, missing delete verification |
| `admin-content.spec.ts` | 151 | **8/10** | Unused import, solid otherwise |
| `admin-system.spec.ts` | 139 | **8/10** | Clean, minor observation |
| `admin-integration.spec.ts` | 100 | **9/10** | Well-structured, smart Draft/Active separation |
| `user-pages.spec.ts` | 108 | **7/10** | Create-only tests with no cleanup, orphan data risk |

**Overall: 7.5/10** — Structurally sound, consistent patterns, but several real bugs and data hygiene issues.

---

## Critical Issues

### C1. `createdIds` array is dead code — `afterAll` cleanup does nothing
**File:** `admin-identity-crud.spec.ts:21, 185-189`  
**Severity:** Critical  
**Impact:** The `afterAll` hook iterates over `createdIds`, but nothing ever pushes into it. The API cleanup mechanism is a no-op. If UI-based delete tests fail (skipped, timeout, element not found), test data permanently leaks into the database.

```typescript
// Line 21: declared but never populated
const createdIds: { endpoint: string; id: string }[] = [];

// Line 185-189: afterAll iterates empty array — does nothing
test.afterAll(async ({ apiContext }) => {
  for (const { endpoint, id } of createdIds) { // always empty
    await apiContext.delete(`${API_BASE}${endpoint}/${id}`).catch(() => {});
  }
});
```

**Fix:** Either populate `createdIds` after each create (requires intercepting API response to capture the ID), or remove the dead `afterAll`/`createdIds`/`API_BASE` code to avoid false confidence that cleanup is happening.

```typescript
// Option A: Capture ID from API response after submit
await modal.submit();
const response = await authedPage.waitForResponse(r => r.url().includes('/api/') && r.status() === 200);
const body = await response.json();
createdIds.push({ endpoint: '/api/users', id: body.id });

// Option B: Remove dead code, rely on sequential delete tests + document the risk
```

### C2. `API_BASE` is unused in `admin-content.spec.ts`
**File:** `admin-content.spec.ts:12`  
**Severity:** Medium (dead code, not a runtime bug)  
**Impact:** Import of unused constant. No `afterAll` cleanup exists in this file either, and `API_BASE` is never referenced. Misleads readers into thinking API cleanup is wired up.

**Fix:** Remove line 12.

---

## High Priority

### H1. No delete verification assertions after `clickRowDeleteAndConfirm`
**Files:** All 5 spec files  
**Severity:** High  
**Impact:** After deleting a row, tests only `waitForTimeout(1500)` — no assertion that the row actually disappeared. Delete could fail silently (backend rejects, popconfirm doesn't fire, row persists) and the test still passes.

**Fix:** Add verification after each delete:
```typescript
await clickRowDeleteAndConfirm(authedPage, rowIdx);
await authedPage.waitForTimeout(1_500);
// Verify row removed
const rowAfter = await findRowByText(authedPage, `E2E GrpEdited ${ts}`);
expect(rowAfter).toBe(-1);
```

### H2. `waitForTimeout` used as synchronization (flaky test risk)
**Files:** All 5 spec files — 15+ occurrences of `waitForTimeout(1500-2000)`  
**Severity:** High  
**Impact:** Hardcoded waits cause two problems: (1) tests are slower than necessary in fast environments, (2) tests flake in slow CI/Docker environments where 2s is insufficient. This is the #1 cause of E2E flakiness.

**Fix:** Replace with event-based waits:
```typescript
// Instead of: await authedPage.waitForTimeout(2_000);
// Use response-based:
await authedPage.waitForResponse(r => r.status() < 400);
// Or DOM-based:
await expect(modal.container).not.toBeVisible({ timeout: 5_000 });
// Or network idle:
await authedPage.waitForLoadState('networkidle');
```

### H3. Duplicate Groups CRUD test — `admin-identity.spec.ts` also creates Groups
**Files:** `admin-identity.spec.ts:55-75` (existing) vs `admin-identity-crud.spec.ts:54-89` (new)  
**Severity:** High  
**Impact:** Both files create a Group via modal. The original `admin-identity.spec.ts` creates `E2E Group ${Date.now()}` and never deletes it — guaranteed data leak. The new CRUD file creates, edits, and deletes, which is correct. But running both files produces two groups: one cleaned, one orphaned.

**Fix:** Remove the Groups CRUD test from `admin-identity.spec.ts` (keep only the render tests there) since the new CRUD file has complete create+edit+delete coverage.

### H4. Sessions — revoking the first available session could kill the test session
**File:** `admin-identity-crud.spec.ts:166-181`  
**Severity:** High  
**Impact:** The test revokes "the first session if available." The `authedPage` fixture is worker-scoped, meaning the test's own admin session appears in the sessions list. Revoking it would invalidate the auth token for all subsequent tests in the serial suite. The test has no guard to skip its own session.

**Fix:** Add a guard that checks the session doesn't belong to the current user, or skip sessions that match the test's IP/user-agent, or revoke from the end of the list (E2E-created sessions are typically newest).

---

## Medium Priority

### M1. `user-pages.spec.ts` — creates Forms and Partners with no cleanup
**File:** `user-pages.spec.ts:77-107`  
**Severity:** Medium  
**Impact:** Both `Forms — create` and `Integration Partners — create` leave orphan records in the database. No corresponding delete tests, no `afterAll` cleanup. Each test run adds 2 permanent records.

**Fix:** Add delete tests after each create, or add an `afterAll` API cleanup.

### M2. Delegations create test has no assertion or cleanup
**File:** `admin-identity-crud.spec.ts:147-162`  
**Severity:** Medium  
**Impact:** The Delegations create test doesn't assert modal closure or success, and there's no corresponding delete/revoke test. The delegation with a 30-day range persists.

**Fix:** Add `expect(modalClosed).toBe(true)` assertion (like other create tests), and add a delete test.

### M3. `submit()` regex may match the "Add" button behind the modal
**File:** `form-modal-page.ts:72`  
**Severity:** Medium  
**Impact:** The submit regex `/lưu|save|submit|tạo|ok|xác nhận|thêm/i` matches "Thêm" (Add) and "Tạo" (Create). These same words appear in the "Add" button that opens the modal. If the modal's submit button has a different label and the regex doesn't match it, the locator falls through to the background page's Add button, potentially opening a second modal instead of submitting.

**Mitigation:** This is scoped to `this.container` (the modal locator), so it only searches within the visible modal. Risk is low but worth noting — if modal uses a drawer that doesn't fully contain the background, the locator could leak.

### M4. `findRowByText` linear scan is O(n) per call
**File:** `table-helper.ts:55-63`  
**Severity:** Medium (perf, not correctness)  
**Impact:** For tables with many rows, this loops through every row sequentially calling `textContent()` on each. Paginated tables cap this, but admin tables with hundreds of rows could slow down.

**Observation:** Acceptable for E2E tests. Not blocking.

---

## Low Priority

### L1. `DataTablePage` import unused in `admin-identity-crud.spec.ts`
**File:** Not present — confirmed this file does NOT import DataTablePage. Clean.

### L2. Magic timeout numbers
**Files:** All — `5_000`, `3_000`, `8_000`, `1_500`, `2_000`  
**Impact:** Different timeout values scattered without constants. Not a bug but reduces maintainability.

**Suggestion:** Extract to a shared config: `const WAIT_SHORT = 1_500; const WAIT_MODAL = 5_000;`

### L3. Vietnamese label strings are duplicated across files
**Files:** Multiple files use `'Tên'`, `/thêm|tạo|create/i`, etc.  
**Impact:** If the app changes label text, every file needs updating.

**Suggestion:** Future improvement — extract label constants to a shared `labels.ts`.

---

## Positive Observations

1. **Consistent POM usage** — All CRUD tests correctly use `FormModalPage` for modal interactions. No raw CSS selectors for form filling.
2. **Graceful `test.skip`** — Every create test checks button visibility first and skips gracefully. Prevents cascading failures in environments where features are disabled.
3. **Serial mode correctly configured** — All describe blocks use `{ mode: 'serial' }` since later tests depend on earlier test data.
4. **Timestamp-based isolation** — Using `Date.now()` for unique names prevents collision between parallel test runs.
5. **LOC compliance** — All files under 200 lines as required.
6. **No hardcoded credentials** — Auth delegated to fixtures. No passwords, tokens, or API keys in test files.
7. **Rules activate/delete separation** — Smart pattern in `admin-integration.spec.ts` where activated rules can't be deleted, so a separate draft is created for the delete test.

---

## Recommended Actions (Priority Order)

1. **[Critical]** Fix or remove `createdIds` dead code + unused `API_BASE` in identity-crud and content files
2. **[High]** Add delete verification assertions (check row disappeared after delete)
3. **[High]** Guard session revoke test against self-revocation
4. **[High]** Remove duplicate Groups create test from `admin-identity.spec.ts`
5. **[Medium]** Add cleanup for Forms and Partners creates in `user-pages.spec.ts`
6. **[Medium]** Add assertion + cleanup for Delegations create
7. **[Low]** Replace `waitForTimeout` calls with event-based waits (can be done incrementally)

---

## Unresolved Questions

1. Does the Delegations form require additional fields beyond the date range? If required fields are missing, `submit()` may silently fail with validation errors that aren't checked.
2. Is the `admin-identity.spec.ts` Groups test intentionally kept as a simpler "smoke" test, or should it be removed now that full CRUD coverage exists in the new file?
3. What is the expected behavior when revoking one's own session in the Sessions admin page — does the backend prevent it, or will it actually log out the admin?
