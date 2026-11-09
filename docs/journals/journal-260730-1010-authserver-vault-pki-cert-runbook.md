# Journal — AuthServer Vault-PKI cert provisioning runbook (doc-portion)

**Date:** 2026-07-30 · **Branch:** main · **Plan:** `260730-1002-authserver-vault-pki-cert-runbook`

## What happened

Third `/cook "theo đề xuất"` of the day. Recommended target = wire cert custody to Vault PKI
(Q2 decision). **Scout reversed my own recommendation's weight:** "Vault PKI wiring" is not an
effort-M code task — it is foundation work gated on a Vault PKI cluster that does not exist.
Only the doc-portion is deliverable now. User chose runbook + overlay-comment fix.

## Scout findings that changed scope

- `AuthServer/Program.cs:547-564` `LoadRequiredCertificate` reads a **PFX FILE only**
  (`X509CertificateLoader.LoadPkcs12FromFile`), fail-fast — NOT PEM, not bytes.
- Vault today = secrets-as-config (`AddVaultConfiguration`), injecting KV into `IConfiguration`.
  **No Vault PKI engine, no Agent-Injector, no CSI in `infra/helm/`** (grep = 0). PKI is greenfield.
- Core tension: Vault PKI issues **PEM** (cert + separate key); the app wants a **PFX file**.

## Delivered

- NEW `docs/ops/authserver-prod-cert-provisioning-runbook.md` (134 lines) — PKI engine/role/issuer
  setup, issuance-at-deploy, the delivery decision, rotation, app-side contract, failure modes.
- Recommended **Option A** (init-container packs PKI PEM→PFX on a Memory emptyDir) = **zero app-code
  change**; Option B (app reads PEM) deferred (changes a load-bearing startup contract + 4 tests).
- EDIT `values-prod.yaml` cert comment → Vault PKI + Option A + "file-Secret shape is INTERIM";
  no YAML value changed (`helm template` still 0-error, 7 placeholders intact).
- code-review: 8/8 technical claims verified vs source (PFX file-only loader, exact config keys,
  refresh-token=7d for key-ring overlap, dev/non-dev split). 0 blocking.

## Learned / non-obvious

- **Vault "already in the stack" ≠ Vault PKI available.** The stack uses Vault KV as a config
  source; PKI is a different secrets engine, entirely un-built here. A custody decision (Vault PKI)
  does not imply the mechanism exists — scout the mechanism, not the brand.
- **Option A keeps the app agnostic to cert delivery** — because `LoadRequiredCertificate` is
  file-only, packing to PFX in an init-container avoids touching a load-bearing startup path.
  Cheaper + lower-risk than teaching the app to read PEM.
- Third slice today where scout reversed an aspirational "effort S/M" into "waiting-on-infra +
  a real doc/code portion." Pattern: the launch blockers that look code-shaped are mostly
  environment-gated; the deliverable-now part is the design decision + its documentation.

## Row status

`AUTHSERVER-PROD-CERT-PROVISIONING-RUNBOOK` stays `[blocks-v1.0]` OPEN — doc-portion shipped;
the wiring (PKI engine + init-container + overlay volumes) is gated on a real Vault PKI cluster.

## Launch picture after today

3 of 6 `[blocks-v1.0]` de-escalated/structure-ready this session (VNeID→no, purge-chain→not-a-blocker,
prod-URI + cert both structure/doc-ready pending a cluster). Remaining code-shaped-and-doable-now:
`SIGN-FULL-PDF-CRYPTO-VERIFY`, `CI-DISABLED-SINCE-APRIL`. Open ops decisions: Q4 seed-posture, Q5 Vault seal.
