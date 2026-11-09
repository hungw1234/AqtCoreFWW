# Forms Cascade H2 — Vault-Authenticated External DataSource Fetch

**Date**: 2026-08-05 16:00
**Severity**: Medium
**Component**: Forms / BE external DataSource resolver, Vault integration
**Status**: Shipped + merged to main (`8fd6ad4b2`); part of `FORMS-CASCADE-PUBLIC-EXTERNAL-INROW` H2 slice. H2 complete.

## What Happened

External data sources (`config.AuthMode` null = static list; set = fetch URL at serve time) now support Vault-managed API-key authentication. When a form defines a cascading external reference field with authentication enabled, the resolver reads per-source API keys from Vault (`secret/aqt/forms/external-ds/{vaultKeyId}/api-key`, field `value`), caches for 5min (IMemoryCache), attaches as Bearer token or custom header, and fails closed (returns `[]`) if the key is missing. This enables prod-safe external cascades: the external API endpoint credential never lives in form config.

## The Gap (Pre-Existing)

External data sources were either static (encoded in config) or unauthenticated HTTP fetch. Authing an external fetch required the credential to be stored in form config or the request to ship unsigned → phishing risk / credential sprawl. The fix enables authed cascades while keeping secrets out of the form backlog.

## Technical Details

**3 BE files, ~240 lines added:**

1. **`ExternalDataSourceAuthKeyResolver.cs`** — NEW. `IVaultClient?` injected (nullable, optional). Resolves key by `vaultKeyId` (e.g., "ds-99") → path `secret/aqt/forms/external-ds/ds-99/api-key`, field `value`. IMemoryCache 5min TTL + warn-once log on miss. Returns null if Vault not registered or path absent. Mirrors `VaultAuditHmacKeyResolver` but NO legacy fallback — external DS has no legacy source, miss must fail closed.

2. **`ExternalDataSourceResolver.cs`** — ctor injected `ExternalDataSourceAuthKeyResolver`. New config fields: `AuthMode` (null|"Bearer"|custom-header-name), `VaultKeyId` (e.g., "ds-99"), `HeaderName` ("Authorization" default). When `AuthMode` set: resolve key up-front; empty key → return `[]` (fail closed, no unauthenticated fetch). Switched `client.GetStringAsync(url)` → `HttpRequestMessage` + `client.SendAsync` + `EnsureSuccessStatusCode` so per-request auth header is attached WITHOUT polluting the shared named client's default headers. SSRF guards (HTTPS-only, RFC-1918, DNS-rebind) run BEFORE auth resolution → even authed URLs to private ranges are blocked.

3. **`FormsInfrastructureRegistration.cs`** — key resolver registered Scoped. `AddVaultKeyResolver` called only on Api host (has Vault). Hosts lacking Vault (test, secondary) get nullable injections that return null → fetch fails closed (no HTTP call).

**Tests: `ExternalDataSourceAuthResolverTests`, 6/6:**
- static, no Vault (asserts `vault.DidNotReceive()`)
- Bearer header attachment
- custom ApiKeyHeader (e.g., `X-API-Key`)
- Vault path pinned to `secret/aqt/forms/external-ds/ds-99/api-key`
- Vault-unwired fail-closed (no HTTP call issued)
- authed-URL-to-RFC-1918 still SSRF-blocked (HTTPS-guard runs before auth)

Public IP-literal hosts used in tests so DNS-rebind never depends on live DNS (earlier draft using `api.example.com` false-failed offline). Full Forms.Infrastructure 58/59 — the 1 failure (`FormulaSandboxTests.EvaluateAsync_ReferencesAbsentField_SubstitutesZero`) passes in isolation (known wall-clock flake, unrelated). Build 0 warn/0 error.

## Decisions

- **`IVaultClient?` nullable** over adding a stub interface — matches "fail closed on missing Vault" acceptance, avoids double-registration/crash on secondary hosts. KISS.
- **NO legacy fallback** — external DS keys have no legacy source; a Vault miss must fail closed, not fall back to unauthenticated.
- **Per-request header instead of default header** — keeps the shared named client "FormsExternalDataSource" generic; each source can choose auth mode independently.
- **SSRF checks before auth** — even if auth is enabled, private/rebind URLs are rejected before Vault is consulted. Defense-in-depth.

## Carry-Over Concerns

**H-1 (pre-existing, filed backlog `FORMS-EXTERNAL-DS-IPV6-LITERAL-SSRF-GAP`, post-v1.0):** `IsAllowedUrl` only checks IPv4 ranges + string literals; IP-literal check short-circuits. An IPv6-literal like `https://[::ffff:169.254.169.254]/…` (cloud-metadata), `[fc00::1]`, `[fe80::1]` pass both guards today. Not introduced by H2 but authed request path raises stakes. Fix = route IP-literals through existing `IsPrivateIp` helper (handles mapped-IPv6). Deferred (pre-existing, server-side config → low exploitability).

**M-1 (Phase-05 blocker, load-bearing):** the new `AuthMode`/`VaultKeyId`/`HeaderName` config fields have NO save-validator yet. `PublicFormsController` uses the same resolver → today an authed-external config on a PUBLIC form would read Vault + issue authed outbound for an anonymous caller. Phase-05 MUST: reject authed-external-on-public at save/publish time + add FE editor fields for the new config. This is a multi-tenant/privacy hole (public-form credentialed fetch could leak to anon caller). Not a blind-spot (anticipated in Phase-05 acceptance); Phase-05 is the gate.

## Next Steps

H2 complete. Remaining in `FORMS-CASCADE-PUBLIC-EXTERNAL-INROW`:
- **H2-Phase-05** (immediate): save-validator reject authed-external on public forms + FE editor fields for `AuthMode`/`VaultKeyId`/`HeaderName`. Closes M-1 hole.
- **M1** (Phases 06–07): in-row TableField cascade validator + FE per-row cascade renderer.
- **H1** (deferred): Internal Handler (vacuous, no real implementation path).

Vault path documented in `docs/ops/vault-secrets-management-guide.md`. Plan dir: `plans/260805-1409-forms-cascade-public-external-inrow/` (gitignored).

---

**Unresolved questions:** none. H2 shipped and merged.
