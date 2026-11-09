# AqtCoreFW Documentation Index

> Production-ready .NET 10 modular-monolith backend template for Vietnamese government projects.
> **Last updated:** 2026-06-25

---

## Quick Navigation

| I want to… | Start here |
|---|---|
| Understand the project | [project-overview-pdr.md](./project-overview-pdr.md) → [system-architecture.md](./system-architecture.md) |
| Find a module / see what's built | [codebase-summary.md](./codebase-summary.md) (deep-dive: [codebase-summary/modules.md](./codebase-summary/modules.md)) |
| Write code to the house style | [code-standards.md](./code-standards.md) (backend) + [frontend-code-standards.md](./frontend-code-standards.md) (`web/`) + [api-design-standards.md](./api-design-standards.md) |
| Set up my machine | [developer-onboarding-guide.md](./developer-onboarding-guide.md) |
| Add a new module | [guides/module-creation-guide.md](./guides/module-creation-guide.md) |
| Trace module / FE↔BE contracts | [dependency-map.md](./dependency-map.md) |
| See status & what's next | [project-roadmap.md](./project-roadmap.md) + `../backlogs/backlog.md` |
| Read delivery history | [project-changelog.md](./project-changelog.md) (older: [project-changelog-archive.md](./project-changelog-archive.md)) |
| Deploy / operate | [deployment-guide.md](./deployment-guide.md) + [ops/](./ops/) + [runbooks/](./runbooks/) |
| Verify compliance | [compliance-evidence-checklist.md](./compliance-evidence-checklist.md) + [compliance/](./compliance/) |
| Understand a key decision | [adr/](./adr/) |

---

## Document Map

### Canonical core (the 8 + changelog)
| Document | Purpose |
|---|---|
| [project-overview-pdr.md](./project-overview-pdr.md) | Functional/non-functional requirements, constraints, metrics |
| [system-architecture.md](./system-architecture.md) | Layer model, 15-module topology, CQRS, security, event-driven |
| [code-standards.md](./code-standards.md) | Backend coding conventions, CQRS patterns, invariants, RLS, testing |
| [frontend-code-standards.md](./frontend-code-standards.md) | React/TypeScript conventions for `web/`: error handling, polling, memoization, E2E fixtures |
| [codebase-summary.md](./codebase-summary.md) | Module inventory + stats (deep-dive in `codebase-summary/`) |
| [dependency-map.md](./dependency-map.md) | Module relationships, entity maps, FE↔BE contracts (single source of truth) |
| [design-guidelines.md](./design-guidelines.md) | UI/UX + design-system guidance |
| [deployment-guide.md](./deployment-guide.md) | Docker dev, Kubernetes prod, CI/CD, secrets |
| [project-roadmap.md](./project-roadmap.md) | Current status, strategic direction, recently shipped, next tasks, history |
| [project-changelog.md](./project-changelog.md) | Per-version delivery history (rolling, since 2026-05-01) |

### Standards & reference (top-level)
| Document | Purpose |
|---|---|
| [api-design-standards.md](./api-design-standards.md) | REST conventions, versioning, error codes, pagination |
| [api-reference-vi.md](./api-reference-vi.md) | Vietnamese API endpoint reference |
| [tech-stack.md](./tech-stack.md) | Technology decisions, versions, rationale |
| [developer-onboarding-guide.md](./developer-onboarding-guide.md) | First-day setup, local env, running tests |
| [compliance-evidence-checklist.md](./compliance-evidence-checklist.md) | Pre-deployment compliance checklist (Law 91, NĐ53/59/68, NĐ85/TT12, QĐ742) |

### Subdirectories
| Folder | Contents |
|---|---|
| [adr/](./adr/) | 11 Architectural Decision Records (001–011) |
| [codebase-summary/](./codebase-summary/) | Module-by-module deep-dive appendix (`modules.md`) |
| [guides/](./guides/) | How-to guides: module-creation, orval-migration |
| [reference/](./reference/) | Specs & examples: AsyncAPI schema, RLS migration example, v2.40 patterns, authorization gov-reference, admin blueprint, modules overview (VN) |
| [ops/](./ops/) | Operations: SLOs, performance sizing, Vault secrets management + rotation, RLS-migration login, signing-enablement runbooks |
| [runbooks/](./runbooks/) | Incident/dev runbooks: security-incident, dev-data-reset, cases-workflow backfill/drift |
| [reports/](./reports/) | Point-in-time reports + leadership deliverables (test summary, deployment report `.md`/`.docx`, proposal `.docx`) |
| [signing/](./signing/) | Digital-signature integration guide, spec amendments, usage cookbook |
| [compliance/](./compliance/) | QĐ-742 evidence dossier (57 files) |
| [phases/](./phases/) | Historical phase 1–2 details + changelogs |
| [srs/](./srs/) | Software Requirements Spec (interfaces, NFR, use-cases) |
| [journals/](./journals/) | Daily engineering journals (append-only history) |

> Binary deliverables in [reports/](./reports/): `bao-cao-trien-khai-corebase.docx` (leadership report — Word source of `bao-cao-trien-khai-corebase.md`), `De_Xuat_Core_Fw_v1.docx` (standalone proposal).

---

## Directory Structure

```
docs/
├── README.md                      ← you are here
├── <8 canonical>.md               ← project-overview-pdr, system-architecture, code-standards,
│                                     codebase-summary, dependency-map, design-guidelines,
│                                     deployment-guide, project-roadmap
├── project-changelog.md           ← rolling (since 2026-05-01)
├── project-changelog-archive.md   ← older entries (prior to 2026-05-01)
├── <standards/reference>.md       ← api-design-standards, api-reference-vi, tech-stack,
│                                     developer-onboarding-guide, compliance-evidence-checklist, …
├── adr/                           ← decision records (immutable)
├── codebase-summary/              ← deep-dive appendix
├── guides/                        ← how-to
├── reference/                     ← specs & examples
├── ops/  ·  runbooks/             ← operate & respond
├── reports/  ·  signing/  ·  phases/
├── compliance/                    ← QĐ-742 evidence
├── srs/                           ← requirements spec
└── journals/                      ← append-only engineering log
```

---

## Current Status (2026-06-25)

- **15 backend modules** live; modular monolith over 74 projects; architecture guards 145–149/149 PASS.
- **Latest delivery:** v2.137 line + Signature multi-provider enrollment **Phases 1–3** (self-service enroll, admin approval, binding-guard tightening). See [project-roadmap.md](./project-roadmap.md) → *Recently Shipped (June 2026)*.
- **Strategic direction (2026-06-10):** CI/test-infra parked; focus on new features + polish. Ranked next-tasks in [project-roadmap.md](./project-roadmap.md) and `../backlogs/backlog.md`.
- **Tests:** ~3080 baseline (Architecture, Forms, Workflow, etc.). See `../backlogs/backlog.md` for the live count.
- **Compliance:** Law 91/Decree 356, NĐ53, NĐ59, NĐ68, NĐ85/TT12, QĐ742 — evidence in [compliance/](./compliance/).

---

## Contributing to Documentation

1. Keep files **under 800 LOC** (split into a subdirectory appendix if larger).
2. Prefer **tables and clear headers** over walls of text.
3. **Link to related docs**; verify code references against the actual codebase.
4. Put new docs in the right place: canonical → top-level; how-to → `guides/`; specs/examples → `reference/`; operate/respond → `ops/`+`runbooks/`.
5. **Update this index** when adding, moving, or removing a document.
6. ADRs and journals are **append-only history** — do not rewrite past entries.
