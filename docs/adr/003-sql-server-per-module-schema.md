# ADR-003: SQL Server with Per-Module Schema Isolation

**Date:** 2026-03-04
**Status:** Accepted
**Deciders:** AqtCoreFW Architecture Team

## Context

Vietnamese Government procurement standards strongly prefer SQL Server (native Windows/Windows Server licensing bundled with system licenses). EF Core + Dapper (CQRS read side) pattern works reliably on SQL Server 2022+.

Monolithic architecture with multiple modules requires schema organization strategy:
- **Single schema per monolith:** Poor module isolation; difficult to extract modules later; schema-level GOV audit/security policies cannot be applied per-module
- **Multiple databases:** Defeats monolithic advantage (single transaction boundary); increases infrastructure cost and operational complexity
- **Per-module schemas in single database:** Optimal balance—module isolation with shared database connection, transaction capabilities across modules when needed

SQL Server's schema construct enables role-based access control (RBAC) at the schema level, meeting NĐ53 (Cybersecurity) and NĐ13 (Data Protection) requirements without microservice operational overhead.

## Decision

Implement **per-module schema isolation within single SQL Server 2022 database**:
- Each module owns named schemas: `Identity`, `Cases`, `Files`, `Audit`, `Notifications`, `MasterData`
- Tables/views/SPs owned by respective schemas
- EF Core migrations organized per module (`modules/*/Data/Migrations/`)
- Cross-module queries via stored procedures or application-level joins (explicit dependencies documented)

This design preserves monolithic ACID guarantees while enabling schema-level security policies and future extraction to separate databases if scale requires.

## Consequences

### Positive
- **Module Isolation:** Schema boundaries prevent accidental cross-module data access; easier to extract as separate DBs/services later
- **GOV Compliance:** Schema-level role permissions enable NĐ53 security policy enforcement (e.g., only Audit.* tables accessible to compliance user role)
- **Single Transaction:** ACID transactions span multiple modules if needed; no eventual consistency complexity in Phase 1
- **Cost Efficiency:** Single SQL Server license covers all modules; easier procurement justification
- **Audit Trail:** Audit schema isolated; HMAC chain verification endpoint can verify Audit.AuditLog integrity without cross-schema complexity

### Negative
- **Schema Explosion:** 7+ schemas visible in SQL Management Studio; developers must be disciplined about ownership
- **Cross-Schema Queries:** Explicit dependencies; if Cases needs MasterData lookups, coupling is visible (harder to hide poor design)
- **Migration Ordering:** Database migration order matters; Audit/MasterData schemas typically migrate before dependent modules
- **Cross-DB Porting (out of scope for this repo):** SQL Server schema semantics differ from Oracle (schema ≈ user) and PostgreSQL (schema ≈ namespace). Oracle 19c port lives in [AqtCoreFW_Oracle](https://github.com/thiennd212/AqtCoreFW_Oracle); PostgreSQL/MySQL not on roadmap.

## Alternatives Considered

| Option | Why Rejected |
|--------|-------------|
| **Single schema for all modules** | Poor isolation; difficult to apply per-module security policies; schema-level RBAC unusable |
| **Multiple SQL Server databases** | Per-database connection string management; cross-database transactions complex; higher licensing/infrastructure cost |
| **Separate microservice databases** | Defeats monolith advantage; dual-write problem; eventual consistency adds operational burden for GOV projects |
| **NoSQL per module** | Procurement reluctance; difficult to enforce compliance audit trails; transaction guarantees required for GOV workflows |

## Implementation Details

### Schema Naming Convention
```
[dbo]           # Shared (rare)
[Identity]      # Identity module tables
[Cases]         # Cases DDD aggregates
[Files]         # File storage metadata
[Audit]         # Audit logs (append-only)
[Notifications] # Notification queue/history
[MasterData]    # Reference data (cached)
```

### Migration Execution Order
1. `Audit` (must exist first for audit trigger stubs)
2. `MasterData` (seed data for lookups)
3. `Identity` (users/roles/claims)
4. `Cases` (domain aggregates)
5. `Files`, `Notifications`, `Integration`

### Cross-Module Query Pattern
```csharp
// Explicit dependency: Cases module depends on MasterData lookups
using (var conn = await _readDbConnection.GetConnectionAsync())
{
    return await conn.QueryAsync<CaseTypeDto>(
        "SELECT * FROM [MasterData].[CaseType] WHERE Id = @Id",
        new { Id = caseTypeId }
    );
}
```

## Mitigation Strategy

- **Cross-DB Strategy:** Oracle 19c maintained as a parallel implementation in [AqtCoreFW_Oracle](https://github.com/thiennd212/AqtCoreFW_Oracle) (post 2026-04-27 split); no runtime `IDbProvider` abstraction. Multi-provider unification was evaluated 2026-04-27 and **cancelled** — 594h vs ~80h dual-repo, no Postgres/MySQL customer demand.
- **Documentation:** Clear ownership matrix in module-creation-guide.md; each module README documents schema structure
- **Testing:** Integration tests verify schema isolation (users with only Identity schema access cannot query Cases tables)
