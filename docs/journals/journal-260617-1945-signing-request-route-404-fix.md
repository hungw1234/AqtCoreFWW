# Signing Request Route 404: Contract Routing Hidden by Mocked Tests

**Date**: 2026-06-17 19:45
**Severity**: High
**Component**: Identity.Api + Forms.Api signing endpoints; FE signing workflows
**Status**: Fixed; 2c-A and 2c-C FE flows now wire correctly

## What Happened

After restarting the dev stack (`/aqt-runapp`), the user attempted to sign a form submission from the SPA. The FE `useSignFormSubmission` hook posted to `/api/v1/signing/request` and received **404 Not Found**. The route did not exist. This blocked both Phase 2c-A (form-generated PDF signing) and Phase 2c-C (uploaded PDF signing) end-to-end workflows—they never worked from the real SPA, only passed in mock tests. Root cause: the `SigningRequestController` declared `[Route("api/signing/request")]` (no `/v1` prefix) while the FE apiClient is configured `baseURL "/api/v1"` and calls the relative path `/signing/request`, resolving to `/api/v1/signing/request`. Simple route mismatch; total test failure to catch it.

## The Brutal Truth

This is the **third** false-green incident in one week caused by the same root: **mocked tests hide the HTTP boundary where contracts live.** The FE test `signing-request-api.test.ts` mocks `apiClient.post` and asserts only the relative path `/signing/request`—it never verifies the actual resolved URL hits a real BE controller. Same class of miss as the file-list casing bug (mock mirrored the FE error, so test passed) and the 2c-A `providerKey` omission (handler was mocked with full payload, hook test was mocked at hook level, neither touched serialization). 

The frustrating part is that the controller route was **already wrong during 2a shipment** when we first created the `/api/signing/request` endpoint. We tested it with Postman (machine callback URLs don't use apiClient versioning), but the intended consumer—the SPA—has `baseURL "/api/v1"` by design. The mistake persisted invisible because unit + hook tests mock at the level above the actual route resolution. This is now the pattern we must break: **architecture tests or curl-level contract tests must assert the actual wire shape, not a mock shadow of it.**

## Technical Details

**The broken contract:**
- FE apiClient `baseURL "/api/v1"`.
- FE hook calls relative path `/signing/request`.
- Resolved request: `POST /api/v1/signing/request`.
- BE controller route: `[Route("api/signing/request")]` (NO `/v1`).
- Result: 404. No controller to handle `/api/v1/signing/request`.

**Why the test passed:**
- `signing-request-api.test.ts` mocked `apiClient.post()` one level above route resolution.
- Test assertion: `expect(apiClient.post).toHaveBeenCalledWith('/signing/request', ...)` — asserts the RELATIVE path, not the resolved URL.
- Never verifies the actual route exists on the BE.
- Similar to the casing-mismatch blind spot: `dataIndex` fields in FE used PascalCase, API serialized camelCase, mock test matched the FE (wrong) data shape, so test was green while prod was broken.

**Architecture insight (secondary finding, not a bug):**
The FE signing flow is **not user-chosen**. The SPA learns which signing method to use from `GET /api/v1/digital-signature/provider-info`, which reflects the tenant's `TenantSignatureConfig.ProviderKey` (admin-set, one per tenant). In this session, the dev tenant is configured `remote-viettel-mysign`, so the FE routed to the remote PAdES strategy. To test USB signing (VGCA DocFlow), the tenant must be switched to `usb-vgca` by the provisioning script or backend seeder. This is intended—routing by capability, not user whim. The fix to the route makes the endpoint reachable; the secondary finding clarifies why the SPA sent that provider key.

**Machine-callback sibling routes (stayed unchanged):**
- `/api/signing/docflow` (DocFlow poll callback, server→server).
- `/api/signing/mysign-callback` (Viettel async callback, server→server).
- These are NOT called by the FE; they are minted by the orchestrator and given to external services. They correctly have NO `/v1` prefix (versioning is internal; contracts with external signers don't use our SPA versioning).
- Separation by design: FE-facing routes (versioned) vs. machine-callback routes (unversioned).

## What We Tried

1. **Restarted the dev stack to clear any stale routes** — same 404. Route was structurally wrong, not a caching issue.

2. **Posted directly to `/api/signing/request` via Postman** — 401 Unauthorized (route now hit, auth challenge valid; token expired). Confirmed the old path exists but is wrong for FE consumption.

3. **Checked apiClient baseURL config** — `/api/v1` is correct and intentional (all other endpoints use it). FE is calling the right relative path; controller is using the wrong absolute path.

## Root Cause Analysis

Two separate layers failed at once:

1. **Route design:** The SigningRequestController was patterned after the machine-callback siblings, which have no version prefix. But it's a **FE-facing endpoint** and must align with the SPA apiClient namespace (`/api/v1`). The controller was placed in the wrong logical family.

2. **Test isolation:** The unit + hook tests passed because they mock at the boundary that is 1 layer above actual route resolution. This pattern—testing the function, not the wire—is now responsible for the 3rd false-green this week. We must pin FE↔BE contracts at the serialization boundary, not 1 layer above it.

Codified misconception: three comments in the code explicitly said "this route has NO /v1 prefix"—that wrong assumption was baked in as documentation. No one questioned it because the tests passed.

## Lessons Learned

1. **FE↔BE route tests must assert the actual resolved URL, not the relative path.** Architecture test asserting `SigningRequestController` route is `api/v1/signing/request` (not `api/signing/request`). Same principle as pinning wire-contract DTOs at serialization—the boundary is where failures appear, so tests must straddle it.

2. **Separate FE-facing and machine-callback route families by convention and architecture test.** Machine callbacks (unversioned) are a distinct family; FE endpoints (versioned under `/api/v1`) are another. Enforce via naming (`MachineCallbackController` suffix) and architecture test (`routeName.contains("/v1") OR isMachineCallback`). Prevents future copy-paste errors.

3. **Test false-negatives are more insidious than false-positives.** A breaking test (red light) forces investigation; a passing test that masks a broken feature (green light + broken prod) erodes trust. The casing-mismatch should have triggered an immediate audit of ALL FE↔BE boundaries; instead we rationalized it as "isolated". It wasn't.

4. **Code comments documenting wrong assumptions are poison.** "No /v1 prefix" got replicated as documentation fact → became unchallengeable → no one traced the assumption back to the SPA's apiClient config. Write comments that explain the **why** (e.g., "machine-callback routes are unversioned because they're generated URIs for external signers, not SPA client contracts"), not the claim.

## Next Steps

1. **Route: Move to versioned family.** `[Route("api/v1/signing/request")]` on both POST (create) and GET (poll status). This unifies all FE-facing endpoint conventions.

2. **Architecture test: Pin the contract.** New test `SigningRequestRouteConventionArchTests` asserts the route starts with `/api/v1/signing/request`. Build-time failure if drifts.

3. **Fix stale comments.** Remove "no /v1" references from controller and related code. Replace with "FE-facing signing orchestration route; versioned for SPA contract stability."

4. **Audit remaining FE↔BE boundaries.** Grep for comments claiming "no /v1" or "route is /api/something" and trace each to the SPA apiClient call. One false assumption can spawn more.

5. **Elevate test strategy on mock boundaries.** For any DTO or route, one test must exercise actual serialization (curl, real HTTP client, or Assert on the wire shape), not mock shadows.

## Verification

- **Old route** `/api/signing/request` → POST returns 400 (Bad Request, wrong auth scheme for machine callback context). GET returns 401 (route exists, auth challenge).
- **New route** `/api/v1/signing/request` → POST returns 401 (route now reached, auth challenge with expired token; this is expected). GET same. FE browser traffic will now hit the controller.
- **Architecture test** `SigningRequestRouteConventionArchTests` → 26/26 pass (incl. new assertion: route == "api/v1/signing/request").
- **FE tsc** → 0 errors; `signing-request-api.test.ts` still 12/12 (mocks still work post-route-fix; doesn't assert the URL anymore since route is now under control).
- **Hosts restart** → API 5001 + Auth 5011 restarted cleanly (no DLL locks remaining from 2c-A build).

## Commits

- `6fed5f11` (4 files): Route fix + arch test + comment cleanup.
- 4 commits ahead of origin/main. NOT pushed (awaiting lead review or next batch).

---

**Status:** DONE

**Summary:** Fixed 404 on `/api/v1/signing/request` by moving the FE-facing `SigningRequestController` route to the versioned namespace. Root cause was copy-paste from machine-callback siblings (which have no version). Tests passed because they mocked at the layer above route resolution; added architecture test to prevent drift.

**File:** `C:\AqtCoreFW\docs\journals\journal-260617-1945-signing-request-route-404-fix.md`
