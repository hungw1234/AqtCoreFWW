# CCCD Binding Decouple via Cert SubjectType (Provider-Scoped)

**Date:** 2026-07-01
**Plan:** `260701-1354-signer-cert-subject-type-decouple-cccd` (5 phases, all complete)
**Trigger:** `/ck-debug` on a production symptom → root-caused → `/ask` architecture challenge → `/ck-plan red-team` → `/ck-cook`.

## Problem

USB-token cert enrollment hard-rejected any cert whose subject lacked a CCCD:
`"Certificate subject does not carry a national id."` Root-caused via the audit log (`SIGNATURE_VERIFIED`
snapshot) — the failing token cert was `CN=Test RSA3072, O=BAN CƠ YẾU CHÍNH PHỦ …`, a VGCA cert with no
`serialNumber`/`UID` CCCD RDN, so `VgcaCertSubjectParser` returned false and the enroll handler failed closed.

The user challenged the premise: **organization certs never carry a CCCD, and no regulation requires it** — the
CCCD was only ever the Viettel Mysign account key, wrongly promoted to a universal cert-binding invariant.

## Decision

Decouple. The forge-resistant binding is already **possession-proof + admin approval + enrolled
serial/issuer/thumbprint**; CCCD-subject-match added negligible protection. So:
- **CCCD stays provider-scoped:** mandatory for Remote Mysign (functional account key), advisory-nothing for USB.
- **USB accepts** org certs + no-user-CCCD personal certs.
- **Hard-reject retained** for one case (red-team RT-1 #1): a Personal cert whose CCCD is present AND ≠ the user's
  own — the token-delegation / crypto-identity≠system-identity signal. No legitimate use-case; user confirmed.
- Classify every cert by an explicit `CertSubjectType` (Personal | Organization); persist it + a `DisplayName`.
- Org-cert visible stamp shows the org name via `UserSignatureProfile.DisplayName` (user-controlled; the stamp
  path was already cert-agnostic — no PDF-writer change).

## Red-team (2 adversarial reviewers, pre-implementation)

- **RT-1 (security):** premise SOUND — possession-proof binds signature↔cert↔nonce + chain-to-root; Pending never
  binds at sign time; no `SigningNationalId` consumer breaks. Forced the mismatch hard-reject (not advisory) + a
  set-CCCD lockout fix (a pending no-CCCD USB enrollment must not permanently block first-time CCCD set).
- **RT-2 (correctness):** caught 3 real scope gaps that would have compile-broken or left an unwireable AC —
  the full 13→17 `SignerCertEnrollment.Create` caller list, the BE DTO change mis-assigned to the "FE-only" phase,
  and the stamp-name being profile-driven (not auto-derived). All folded in before coding.

## Post-implementation review catch (S1)

The mandatory `code-reviewer` found that an org cert's **MST (tax code, 10/13 digits)** in the `serialNumber`
RDN would be misread as a CCCD by the `\d{9,12}` regex → misclassified Personal → hard-rejected — i.e. the fix
would *still* reject the exact org certs it was built to support. Fixed by tightening the national-id match to
**exactly 9 or 12 digits with word boundaries** (+ an MST-prefix guard) — robust regardless of which RDN carries
the MST. Also capped `DisplayName` at the column length to avoid a truncation `SqlException`.

## Result

- 5 phases (domain+migration → provider-scoped enroll → guard/stamp verify → FE provider-first modal → integration).
- Tests: Domain 173, Application 415, Infrastructure 235, Integration 88 (Signing), FE vitest 16 scoped (1250/1251 full),
  Api host builds clean, `tsc` clean. 3 architecture failures verified **pre-existing** (unmodified files, predate today).
- Zero new schema beyond `SubjectType` + `DisplayName`; migration `20260701073754_AddCertSubjectTypeAndDisplayName`.

## Lessons

- **Audit log is a debugging asset.** The exact failing cert subject was already captured in
  `AuditLogEntries.DataSnapshot` (`certSubject`) — no repro/instrumentation needed to root-cause.
- **A "brittle by design" helper can quietly gate the core use-case.** `VgcaCertSubjectParser`'s own doc admitted
  subject parsing is best-effort — yet a lax digit regex turned it into a hard functional gate that rejected valid
  org certs (MST≈CCCD collision). Length/boundary precision matters when a parser feeds an accept/reject decision.
- **Deferred/parallel builds throw false "pre-existing error" flags.** A subagent reported 64 Api-host build errors
  that vanished on a clean serial build — concurrent obj writes. Always re-verify a build claim before trusting it.
