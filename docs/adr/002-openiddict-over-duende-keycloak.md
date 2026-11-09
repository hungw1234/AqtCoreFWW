# ADR-002: OpenIddict over Duende Identity Server / Keycloak

**Date:** 2026-03-04
**Status:** Accepted
**Deciders:** AqtCoreFW Architecture Team

## Context

Vietnamese Government projects require OIDC/OAuth2 authentication with integration potential for VNeID (national digital identity) and eID ecosystems. Three primary options exist:

1. **Duende Identity Server:** Industry-standard commercial product, enterprise-grade, requires per-project licensing (~€1,500+/year)
2. **Keycloak:** Open-source IAM platform, mature feature set, requires separate Java infrastructure (Docker container, Postgres, operational overhead)
3. **OpenIddict:** Open-source MIT-licensed library, embeds directly in ASP.NET Core, EF Core-backed, lightweight, minimal ops burden

GOV procurement constraints prevent per-project licensing costs; infrastructure teams prefer consolidated technology stacks (minimize Java dependencies). Custom VNeID integration (NĐ59 compliance) requires deep authorization customization not available out-of-box in any platform.

## Decision

We adopt **OpenIddict 7.x** as the embedded OIDC/OAuth2 provider:
- MIT-licensed, no per-project cost
- Integrated into AqtCoreFW.Api ASP.NET Core application
- EF Core-backed token/consent storage (shared SQL Server)
- Custom claims pipeline enables VNeID stub → production connector (NĐ59)
- RBAC (role-based) + ABAC (attribute-based) authorization via custom claims transformation
- Single technology stack (no Java dependency)

This trades convenience (Keycloak admin UI) for flexibility, cost savings, and operational simplicity. Custom admin screens developed iteratively per GOV project needs.

## Consequences

### Positive
- **Zero Per-Project Licensing:** MIT license applies across all GOV organizations cloning AqtCoreFW
- **Single Tech Stack:** No Java runtime dependency; simplified Docker Compose and Kubernetes manifests
- **Deep Customization:** Claims pipeline, consent workflows, MFA strategies fully customizable for VNeID/eID integration
- **Embedded Deployment:** Reduces operational surface area; monolith includes identity server (Phase 2: extract if separate auth infrastructure needed)

### Negative
- **No Admin UI Out-of-Box:** Admin screens for client registration, consent management must be developed per project
- **Learning Curve:** OpenIddict API less documented than Duende; requires OIDC protocol depth
- **Operational Visibility:** No production-grade admin dashboard; monitoring via logs/metrics required
- **Token Management:** Custom code required for token revocation, client credentials rotation

## Alternatives Considered

| Option | Why Rejected |
|--------|-------------|
| **Duende Identity Server** | Per-project licensing cost (~€1.5k+/year) unsustainable across multiple GOV organizations; closed-source limits VNeID customization |
| **Keycloak** | Introduces Java dependency; separate infrastructure increases Docker Compose complexity; admin UI not built for VNeID workflows |
| **Azure AD / Okta** | Cloud lock-in; VNeID integration impossible; data residency conflict with NĐ53 requirements |
| **Auth0** | Cloud lock-in; licensing cost; PII residency concerns; limited VNeID connector ecosystem |

## Mitigation Strategy

- **Custom Admin Module:** Develop Identity.Admin controller set for client CRUD, consent management (future phase)
- **Comprehensive Testing:** Unit tests for claims transformation, consent workflows, VNeID stub behavior
- **Documentation:** Extensive docs for custom authorization requirements per GOV project
- **Phase 2 Extraction:** When VNeID scale justifies separate auth infra, extract to standalone service; OpenIddict still embedded in Identity Module for intra-monolith auth
