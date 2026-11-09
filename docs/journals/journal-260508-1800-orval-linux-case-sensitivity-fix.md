# v2.137-ORVAL-REGEN — Linux Case-Sensitivity Regression Fix

- **Date:** 2026-05-08 18:00 +07
- **Branch:** `main` (hot-fix, no separate PR)
- **Commit:** `f99757c7`
- **Status:** SHIPPED (pushed origin + bitbucket main + bitbucket dev FF)
- **Effort:** ~45min (emergency deploy recovery)

## What

Kaniko Docker build **FAILED on Linux** with 78 × `TS2307 Cannot find module 'aqtCoreFWAPIV1'`. Root cause: OpenAPI spec title changed `"AqtCoreFW Api v1"` → `"AqtCoreFW API v1"` (API capitalization), triggering Orval schema file rename. Windows NTFS is case-insensitive, so git index cached the rename as identical file. Linux (case-sensitive filesystem) rejected imports referencing stale `aqtCoreFWApiV1` (old) vs generated `aqtCoreFWAPIV1` (new).

## Fix

### 1. Orval Regeneration (78 files)
Full `npm run codegen` regen with correct OpenAPI metadata.

**Generated file changes (7 tag files modified):**
- `web/src/api/contracts/contracts.ts`
- `web/src/api/form-submissions/form-submissions.ts`
- `web/src/api/form-templates/form-templates.ts`
- `web/src/api/form-templates-ext/form-templates-ext.ts`
- `web/src/api/message-logs/message-logs.ts`
- `web/src/api/notification-templates-admin/notification-templates-admin.ts`
- `web/src/api/partners/partners.ts`

### 2. Hand-Written Import Fixes
- `web/src/infra/api/api-error.ts` — updated schema import from `aqtCoreFWApiV1` → `aqtCoreFWAPIV1`
- `web/src/infra/api/__tests__/api-error-contract.test.ts` — fixed schema import

### 3. Stale File Cleanup
- **Deleted:** `web/src/api/mfa/mfa.ts` (deprecated post-Phase-07 retier; endpoints relocated to `account-mfa/`, `mfa-emergency-admin/`, `mfa-policies-admin/`)

### 4. TypeScript Type Fixes
- `mfa-policy-page.tsx` — fixed MfaFactor literal union narrowing (vitest strict literal inference)
- `mfa-enrolment-page.tsx` — removed unused imports
- `vitest.config.ts` — added type-union workaround for `poolOptions` (string | string[])

## Verification

```bash
tsc -b clean
# Output: 0 errors (8.24s)

vite build
# Output: success (28.86s)

git push origin main
git push bitbucket main
git push bitbucket dev (FF)
```

## Impact

- **Web:** tsc clean, vite build success, no runtime errors.
- **Backend:** Zero changes (OpenAPI metadata only).
- **Deploy:** Linux/Mac Kaniko builds now pass case-sensitivity checks.

## Root Cause Analysis

**Why NTFS masked the issue:** Windows `git add -A` sees case-insensitive file rename as same entry; git index cached as identity. Commit pushed with NO rename marker in `git diff`. Linux `git checkout` on case-sensitive filesystem = files BOTH exist (old + new), creating shadowing. Imports of old name fail.

**Fix Protocol:** Used 2-step `git mv` force case-change (some `git` versions require this on case-insensitive). Then hand-corrected all import paths.

**Prevention:** Pre-push lint should warn on mixed-case schema imports. Backlog: add `import-case-check` eslint rule (P3).

## Backlog

- **P3:** ESLint rule `import-case-consistency` to catch schema name mismatches vs Orval-generated exports.
