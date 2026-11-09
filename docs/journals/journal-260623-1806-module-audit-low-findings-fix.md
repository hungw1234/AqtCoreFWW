# Journal — MODULE-AUDIT-LOW-FINDINGS slice

**Date:** 2026-06-23 18:06 · **Workflow:** `/cook` plan `260623-1727` · **Branch:** main
**Commits (pushed origin+bitbucket):** `dfadc6c3` code · `7c4c1ffc` docs · code-reviewer DONE (0 concern above Low)

## What

Closed the 6-finding module-audit defer bundle. Empirically re-probed each finding (INVARIANT 260522-A — audit-author labels NOT trusted) and acted per ground-truth, not per nameplate:

| # | Module / target | Verdict | Action |
|---|-----------------|---------|--------|
| (a) | Identity `ClaimsEnrichmentTransformer` | REAL | inject `IHttpContextAccessor`; `RequestAborted` threaded into 2 hot-path EF `.ToListAsync()` |
| (b) | Organization `JwtOrgContext` sync-over-async | DOCUMENT-ONLY | left as-is — cache-backed + `ITenantOrgContext` is a SharedKernel **sync** contract (domain-predicate use); async-all-the-way would break a cross-module public contract |
| (c) | Forms `SubmitForm`/`SubmitPublicForm` ~90 LOC dup | REAL | extracted `FormSignatureIngest` static helper; 2 callers share it (only deltas — TenantId source + signerUserId auth-vs-anon — are params) |
| (d) | Rules `RulesEngineEvaluator` "no TTL" | FALSE-POSITIVE | none — `cache.Set(key,engine,CacheTtl=10min)` HAS absolute-expiration TTL |
| (e) | Collaboration `AddMemoryCache` | REAL (cosmetic) | removed redundant call (host/shared registers it) |
| (f) | AI `DocumentIngestionService` embed-fail | REAL | silent-skip+`Result.Ok` (partial-index-as-success) → **fail-hard** `Result.Fail(embedResult.Errors)` (all-or-nothing, mirrors Qdrant GOV_AI_003). Ollama queue-metric deferred — queue already throws GOV_AI_004 (not silent), metric is YAGNI on-prem |

## Lessons

- **Agent-audit labels decay / false-positive.** 2 of 6 findings were non-issues caught only by reading source — (d)'s "no TTL" was flat wrong, (b)'s "fix it" ignored the SharedKernel sync-contract blast-radius. Third reinforcement today of the verify-before-trust pattern; the 5-min probe per finding is cheaper than implementing a phantom fix.
- **Behavior-preserving refactor of a security path is safe only with a strong pre-existing test net.** The Forms signing path (QĐ-742) had 52 handler tests pinning the exact contract (SignerUserId binding, TenantId binding, Mode-1/2 bytes, tampered-never-persists, required-missing). Extraction = run green → refactor → run green; reviewer also diffed line-by-line. 317/317 unchanged.
- **Kept the extraction a static helper, not an injectable service** — deliberately, so handler constructors didn't change and the existing tests (which build handlers with `IIdentitySigningClient`) stayed green with zero edits. KISS over a "proper" DI service that would have rippled into every test.
- **The `signerUserId` param feeds two different treatments** in one value: RAW into the verify request (anti-impersonation binding), `Guid.Empty→null` normalized into the evidence record. Both callers pass the same value (auth=SubmittedBy, public=null) → exact behavior preserved without branching in the helper.

## Verify

build 0E/0W · Forms.App 317/317 · Ai.Infra 17/17 (+1 new embed-fail teeth test asserting `DidNotReceive().UpsertAsync`) · arch 145/145 · smoke 18/18 (boots host → validates (a) DI + (e) IMemoryCache removal) · net −79 LOC · 0 public-contract change.

## Unresolved

- None blocking. (f) Ollama-queue capacity metric/health-check intentionally deferred — re-open only if saturation is observed in practice.
