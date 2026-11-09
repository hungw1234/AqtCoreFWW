# ADR-001: Modular Monolith over Microservices

**Date:** 2026-03-04
**Status:** Accepted — **Superseded by [ADR-012](./012-activate-microservices-strangler-extraction.md)** (2026-07-10, activates the Phase-2 strangler extraction reserved below)
**Deciders:** AqtCoreFW Architecture Team

## Context

Vietnamese Government backend projects operate under resource-constrained conditions: typical development teams consist of 2–5 developers, limited DevOps infrastructure, and minimal budget for operational complexity. Traditional microservice architectures introduce significant overhead:
- Service discovery, load balancing, distributed tracing
- Separate deployment pipelines and infrastructure per service
- Network latency and inter-service communication failures
- Operational expertise requirements (Kubernetes, service mesh)

GOV projects prioritize reliability and maintainability over horizontal scaling. A single, cohesively deployable unit reduces cognitive load and deployment risk while maintaining architectural flexibility for future evolution.

## Decision

We adopt a **modular monolith** architecture:
- Single .NET 10 web application deployed as one unit
- Modules (Identity, Cases, Files, Audit, Notifications, MasterData) organized as independent namespaces with clear boundaries
- Each module owns its own SQL Server schema, enabling future extraction as microservices via the strangler pattern
- Shared abstractions (IReadDbConnection, IMessageBus, ICacheService) decouple modules from infrastructure

This design preserves monolithic simplicity while maintaining the option to extract modules into separate deployables (Phase 2) if scale demands justify the operational cost.

## Consequences

### Positive
- **Operational Simplicity:** Single Docker image, single deployment pipeline, single process to monitor
- **Developer Velocity:** New team members onboard faster; shared codebase, single IDE session
- **Transaction Boundaries:** Local ACID transactions across module boundaries (no eventual consistency complexity)
- **Strangler-Ready:** Per-schema isolation enables clean extraction of modules as microservices later

### Negative
- **Horizontal Scaling:** Individual modules cannot be scaled independently; entire monolith must scale
- **Deployment Frequency:** Any module change triggers full application redeployment
- **Large Codebase:** Over time, the single codebase may exceed 500k LOC without disciplined modularization
- **Shared Infrastructure Bottleneck:** All modules contend for shared CPU, memory, database connections

## Alternatives Considered

| Option | Why Rejected |
|--------|-------------|
| **Microservices** | GOV teams lack DevOps bandwidth; distributed tracing/debugging adds operational toil; multiple deployments increase deployment risk |
| **NServiceBus** | Licensing cost (~€2k+/year per project); lock-in to Windows Server; steeper learning curve for small teams |
| **Modular .NET assemblies without schema separation** | Increased coupling; harder to extract as separate services; requires API boundaries from day one |

## Mitigation Strategy

- **Enforce Module Boundaries:** Each module's public API strictly limited to interfaces in SharedKernel
- **Schema Isolation:** SQL Server per-module schema enforces table ownership and access control (see ADR-003)
- **Phase 2 Preparation:** Document strangler pattern approach in module-creation-guide.md; prepare domain event streaming for future async extraction
