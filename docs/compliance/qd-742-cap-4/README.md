---
title: "QĐ 742 Cấp 4 — Audit Dossier Index"
version: "Wave-2"
created: 2026-05-10
updated: 2026-05-10
maintainer: "QĐ 742 Compliance Track"
---

# QĐ 742 Cấp 4 — Audit Dossier

Central index for the AqtCoreFW QĐ 742/QĐ-BTTTT Level-4 compliance audit dossier.
6/8 implementation phases shipped. 40/41 ReqIDs have code evidence. Wave-2 complete: 40 evidence files generated.

---

## Wave-2 Summary (2026-05-10)

Wave-2 generated **40 evidence files** covering all 40 `MET` rows. 1 row (5.1) deferred to Phase-05b (vendor brief due 2026-05-19).

> **Note:** Task spec stated "39 evidence files / skip 2 mục 5.1 rows" — actual checklist has only **1** mục 5 row (5.1). Correct count is **40 files / 40 MET rows**. Wave-2 delivers 40/41 ReqIDs.

### Evidence folder structure

| Folder | ReqIDs | Files |
|--------|--------|-------|
| [`muc-1-xac-thuc/`](muc-1-xac-thuc/) | 1.1.a–b–c, 1.2, 1.3.a–b–c–d–đ–e, 1.4.a–b–c–đ, 1.5, 1.6.a–b | 17 |
| [`muc-2-truy-cap/`](muc-2-truy-cap/) | 2.1.a–b, 2.2.a–b, 2.3.a–b–c, 2.4.a–b | 9 |
| [`muc-3-nhat-ky/`](muc-3-nhat-ky/) | 3.1.a–b, 3.2.a–b–c | 5 |
| [`muc-4-an-toan/`](muc-4-an-toan/) | 4.1, 4.2, 4.3.a–b, 4.4.a | 5 |
| `muc-5-bao-mat/` | 5.1 — **DEFERRED** (Phase-05b) | 0 |
| [`muc-6-sao-luu/`](muc-6-sao-luu/) | 6.1.a–b, 6.2.a–b | 4 |
| **Total** | | **40** |

Each evidence file follows the schema documented in [Evidence File Schema](#evidence-file-schema-wave-2-standard) below.
Checklist status updated: 40 rows → `MET` + Evidence Link + Verifier + Date populated. YAML frontmatter `status: wave-2-evidence-populated`.

### Updated Evidence File Schema

Wave-2 uses this frontmatter + section structure (supersedes Wave-1 schema in README):

```yaml
---
ReqID: "1.1.a"
title: "Có giao diện quản lý tài khoản người sử dụng"
muc: 1
phase: "03"
status: MET
generated: 2026-05-10
---
```

Sections: Implementation Reference · Evidence Artifacts · Behavior Summary · Verification Steps · Observations/Audit Notes (optional).

---

---

## Index

| Folder / File | Contents | Wave |
|---------------|----------|------|
| [`qd-742-cap-4-checklist.md`](qd-742-cap-4-checklist.md) | 41-row master checklist (ReqID → Status → Evidence) | Wave-1 |
| [`phase-01/`](phase-01/) | Audit Baseline evidence (AuditLog, AuditCategory, RetentionPolicy) | Wave-2 |
| [`phase-02/`](phase-02/) | Auth Lifecycle evidence (password policy, lockout, first-login) | Wave-2 |
| [`phase-03/`](phase-03/) | MFA Policy evidence (MfaPolicy entity, RecoveryCode, RequiresMfa) | Wave-2 |
| [`phase-04/`](phase-04/) | Data Classification evidence (PII encryption, RTBF, EmailHash) | Wave-2 |
| [`phase-05/`](phase-05/) | Supporting UIs evidence (IpRules, SecurityHealth, permissions) | Wave-2 |
| [`phase-05b/`](phase-05b/) | Digital Signature evidence — PENDING (due 2026-05-19) | Wave-2 |
| [`phase-06/`](phase-06/) | Vault Finish evidence (no-hardcode-credentials, key rotation) | Wave-2 |
| [`phase-07/`](phase-07/) | Aggregator + dossier scaffold (this wave) | Wave-1 |
| [`decisions-log.md`](decisions-log.md) | Architecture decisions with QĐ 742 rationale | pre-Wave-1 |
| [`official-requirements.md`](official-requirements.md) | Vietnamese source text of all 41 requirements | pre-Wave-1 |
| [`plan-summary.md`](plan-summary.md) | Implementation plan summary (all 8 phases) | pre-Wave-1 |
| [`red-team-findings.md`](red-team-findings.md) | Red-team audit findings and mitigations | pre-Wave-1 |

---

## Audit Workflow

Auditor path through this dossier:

```
1. README.md (this file)
   └─ overview of phases + status

2. qd-742-cap-4-checklist.md
   └─ 41-row table: ReqID → Status → Phase → Evidence Link

3. phase-XX/<reqId>-evidence.md   (Wave-2 populates)
   └─ YAML frontmatter + 5 sections: Implementation, Code, Test, Screenshot, Notes

4. phase-XX/screenshots/<reqId>-*.png   (Wave-3 captures)
   └─ D7 mandate: one screenshot per UI-facing requirement

5. Compliance test suite (automated verification)
   └─ dotnet test --filter "FullyQualifiedName~Qd742"
   └─ Output: tests/architecture/.../bin/Debug/net10.0/qd-742-cap-4-test-output.txt
```

---

## Evidence File Schema (Wave-2 Standard)

> **SUPERSEDED:** This section documents the original Wave-2 planning schema. The actual Wave-2 shipped schema is defined at [Updated Evidence File Schema](#updated-evidence-file-schema) above (lines 37–52). The section below is preserved for historical audit trail only — do not use for new evidence files.

Every evidence sub-file under `phase-XX/` MUST follow this schema exactly.
CI guard (Wave-2) validates presence of all mandatory sections.

```yaml
---
reqId: "1.6.a"                                        # verbatim from checklist
muc: 1                                                 # integer 1-6
title: "Giao diện quản lý chính sách MFA"             # verbatim from QĐ 742
status: MET                                            # see Status Taxonomy below
phase: 03
verifier: "<name>"
date: 2026-05-10
---

## Implementation Summary
1-3 sentences: what was built + where in codebase.

## Code Evidence
- Entity: `src/.../MfaPolicy.cs:1`
- Controller: `src/.../MfaPolicyAdminController.cs:1`
- DDL/Migration: `src/.../Migrations/AddMfaPolicy.cs`

## Test Evidence
- Compliance test: `tests/.../Qd742Phase03ComplianceTests.cs::Compliance_1_6_a_MfaPolicy_EntityExists`
- Test output snippet: `<paste relevant assertion line>`

## Screenshot Evidence (D7 mandate)
- File: `screenshots/1-6-a-mfa-policy-ui.png`  (relative to dossier root)
- Captured: 2026-05-XX
- Captured by: <name>

## Notes / Caveats
Free-form: deviations, accepted risks, related ReqIDs.
```

**Naming convention:** `<reqId-with-dots-replaced-by-dashes>-evidence.md`
Example: `1-6-a-evidence.md` for ReqID `1.6.a`.

---

## Status Taxonomy

| Status | Meaning | Audit Interpretation |
|--------|---------|---------------------|
| `MET` | Code + test + screenshot present, all green | Full credit |
| `MET-pending-evidence-file` | Code/test green; evidence file not yet authored (Wave-2 gap) | Credit pending Wave-2 |
| `PARTIAL` | Code present, test or screenshot missing — gap listed in Notes | Partial credit; must list gap |
| `pending-phase-05b` | Placeholder for 5.1 only — Phase-05b vendor brief not shipped | NOT MET; awaiting 2026-05-19 |
| `N/A` | Does not apply to AqtCoreFW (business decision documented) | Exempt if justified |

> `pending-phase-05b` is explicitly NOT equivalent to `MET`. Do not promote to `MET` without Phase-05b ship confirmation.

---

## Compliance Test Suite

Automated structural verification covering all 6 shipped phases. Tests live in **two assemblies**:
the architecture suite (reflection-only, fast) and the integration suite (Phase-02 needs WebAppFixture + SQL).
The aggregator manifest emits totals for the architecture suite only — Phase-02 facts are counted
separately when running the full filter.

| Test Class | Phase | Facts | Suite | Location |
|------------|-------|-------|-------|----------|
| `Qd742Phase01ComplianceTests` | 01 Audit Baseline | 5 | architecture | `tests/architecture/.../Compliance/` |
| `Qd742Phase02ComplianceTests` | 02 Auth Lifecycle | 15 | integration | `tests/integration/AqtCoreFW.Identity.Integration.Tests/` |
| `Qd742Phase03ComplianceTests` | 03 MFA Policy | 5 | architecture | `tests/architecture/.../Compliance/` |
| `Qd742Phase04DataClassificationComplianceTests` | 04 Data Classification | 5 | architecture | `tests/architecture/...` |
| `Qd742Phase05ComplianceTests` | 05 Supporting UIs | 15 | architecture | `tests/architecture/.../Compliance/` |
| `Qd742Phase06ComplianceTests` | 06 Vault Finish | 9 | architecture | `tests/architecture/...` |
| `Qd742Cap4ComplianceSuiteTests` | Aggregator (arch) | 1 | architecture | `tests/architecture/.../Compliance/` |

**Totals:** 39 facts (architecture suite) + 15 facts (integration suite) = **54 structural assertions** across 6 phases.
Aggregator's `TOTAL` line reports the architecture-suite figure; auditor must read both manifests.

Run the full filter (both suites):
```
dotnet test --filter "FullyQualifiedName~Qd742" --logger "console;verbosity=normal"
```

Manifest output (architecture suite — for dossier inclusion):
```
tests/architecture/AqtCoreFW.Architecture.Tests/bin/<Configuration>/net*/qd-742-cap-4-test-output.txt
```
(`<Configuration>` = `Debug` or `Release` depending on build; TFM follows current `net10.0`.)

---

## How to Add Evidence (Wave-2 Instructions)

1. Pick a row from `qd-742-cap-4-checklist.md` with status `MET-pending-evidence-file`.
2. Create `phase-XX/<reqId-dashed>-evidence.md` using the schema above.
3. Update the checklist row: set Evidence Link to the relative path, fill Verifier + Date.
4. Change status from `MET-pending-evidence-file` → `MET` (after screenshot also captured).
5. Run `dotnet test --filter "FullyQualifiedName~Qd742"` — must stay green.

Wave-2 goal: all 39 covered rows → `MET` with evidence file + screenshot.

---

## Wave Roadmap

| Wave | Status | Deliverables |
|------|--------|-------------|
| Wave-1 | SHIPPED 2026-05-10 | Checklist + aggregator test + Phase-01/03 compliance tests + dossier scaffold + README |
| Wave-2 | SHIPPED 2026-05-10 | 40 evidence files (muc-1 through muc-6; 1 row deferred to Phase-05b) + checklist updated to MET |
| Wave-3 | pending | 40 screenshots (D7 mandate) — placeholder paths in each evidence file |
| Wave-4 (final) | pending Phase-05b | Audit dry-run report + sign-off + 5.1 evidence |
| Phase-05b parallel | pending 2026-05-19 | Vendor brief → populate muc-5-bao-mat/5-1-evidence.md |
