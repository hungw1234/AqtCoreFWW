# Journal — FE Dependency Security: axios Bump + Scoped Transitive Overrides

**Date:** 2026-06-11 09:34 ICT
**Commits:** `aec38144` (deps) · `f3084908` (test/component hygiene) · `d5bcab3a` (yesterday's journals, bundled) — all pushed origin + bitbucket, ahead=0.

## Session goal

Run `/security-scan`, then (user-directed) research thoroughly which dependency versions to bump before touching anything, apply the bump + scoped overrides, and gate on a green full FE suite before committing.

## What worked (verified)

- **Scan triage:** code/secret hygiene clean — Vault-gated prod secrets, parameterized Dapper SQL everywhere (`{where}` from constant clause literals, user values `@`-bound, `SearchTerm` `EscapeSqlLike`-escaped), zero FE XSS sinks (no `dangerouslySetInnerHTML`/`eval`/`v-html`). Only real surface = FE deps.
- **Dependency-tree triage (the key move):** of 28 advisories, only **axios** is a runtime/browser dep. fast-uri/ws/brace-expansion all trace to dev tooling (orval, eslint, vitest/jsdom) → no deployed attack surface. This reframed "13 HIGH" panic into a one-package fix.
- **axios `1.13.6 → 1.16.1`** (exact pin, project convention). Verified the 1.14–1.17 breaking changes (URL basic-auth decode, parseProtocol strictness, null-proto headers) don't touch this codebase's `axios.create`/interceptor/`isAxiosError` usage. api-client interceptor-coupling tests (the only internal-`handlers[]` consumer) 44/44 post-bump.
- **Scoped `pnpm.overrides`** — range selectors bump only vulnerable instances; verified resolved tree: ws@7.5.10 (signalr) + brace-expansion@1.1.14 (minimatch) **untouched**, only the 8.x/5.x/3.1.x vulnerable copies moved. Audit **28 → 1**.
- **Full FE suite 1005/1005**, tsc clean.

## What didn't / lessons

- **First full suite run failed 1004/1005** — `credential-policies-page.test.tsx`. Looked like the documented contention flake. Isolation run passed the 6 tests but still emitted 2 unhandled-rejection errors → **real bug masked as flake**: `handleSave` awaited `form.validateFields()` outside its try/catch → invalid-form save escaped as an unhandled rejection (a **prod defect**, not test noise). Fixed: catch + early-return.
- **Second residual error** = test mock of `notification-bridge` missing `appMessage` export → success-path threw. Stale-incomplete mock.
- **Second full run failed again — different file** (`smoke-user-pages.test.tsx`). The rotating victim looked like contention roulette, but isolation showed a **deterministic** failure: its `delegation-api` mock missed `useApproveDelegation` (stale since yesterday's `bbbcdcd8` Approve-action slice — the component gained the hook, the smoke mock didn't).
- **Lesson:** a full-suite "flake" with a rotating victim is often **N deterministic stale-mock/unhandled-rejection bugs**, one surfacing per run by ordering — not environmental contention. Raise verification (isolation re-run + capture the actual error head, not just pass/fail) to peel real bugs out of the noise. Same class as prior `appMessage`/`hasRole` stale-mock instances in memory.
- **Lesson:** adding a hook/export to a component creates a silent debt in every test that mocks that module — the mock must gain the export or the consuming test throws. Type-grep won't catch it (mocks are untyped factory objects).
- **BE NuGet audit inconclusive** — `dotnet list slnx --vulnerable` prints "restore up-to-date" then no per-project vulnerability table (slnx enumeration quirk). BE **not** asserted clean.

## Decisions made

- **Target `1.16.1`, not `1.17.0`** (user choice, research-backed): 1.16.1 clears the ≥1.16.0 floor, is more mature than just-released 1.17.0, keeps deps=3 (1.17 adds a 4th), respects exact-pin convention. YAGNI over "latest".
- **Scoped overrides, not global** — a global `ws`/`brace-expansion` override would force signalr's ws@7→8 and minimatch's brace-expansion@1→5 (both breaking). Range selectors target only the vulnerable copies.
- **Fix the surfaced bugs in-session** (user chose this over file-and-defer) — both the component defect and the two stale mocks, since they block a green gate and are the same trivial class.
- **quill CVE → no action** — no upstream patch; verified mitigated by existing DOMPurify-on-input + zero raw-render + app never calls Quill export. Watch upstream.

## Test status

- Full FE suite: **1005/1005** (106 files). tsc app: clean. api-client coupling: 44/44.
- pnpm audit: 28 → 1 (quill LOW, mitigated, no patch).
- BE NuGet: inconclusive (tooling quirk) — not asserted.

## Next session / carry-over

- **`WEB-AXIOS-SEC-BUMP`** — closed this session (filed in backlog as closed for traceability).
- **`BE-NUGET-AUDIT-INCONCLUSIVE`** (T3, parked-infra) — re-run BE vulnerable-package audit per-project or with `--format json`; BE dep security currently unverified.
- **`WEB-QUILL-XSS-EXPORT-WATCH`** (T4) — track CVE-2025-15056 for an upstream quill/react-quill-new patch; currently mitigated, no action.
- Backlog/changelog updated; full backlog-archive optimization deferred (marginally over threshold but reorganized <24h ago — avoid churn).
