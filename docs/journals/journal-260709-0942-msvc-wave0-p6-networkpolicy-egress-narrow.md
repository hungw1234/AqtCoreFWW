# Journal — MSVC-WAVE0-P6-NETWORKPOLICY-EGRESS-NARROW (security, helm arch-guard)

**Date:** 2026-07-09 09:42 · **Type:** security fix / helm template · **Severity:** High · **Status:** DONE

## What

Closed egress exfiltration hole in Helm NetworkPolicy template `infra/helm/aqtcorefw/templates/network-policy.yaml`. One egress rule had `- ports: [443, 4317, 4318]` WITHOUT `to:` selector, admitting egress to ANY destination on those ports — circumventing the `policyTypes:[Ingress,Egress]` default-deny design. **No runtime incident; caught via architecture test.**

3 files changed:
- `network-policy.yaml` — narrowed unscoped ports to [53, 443] only; OTEL 4317/4318 now guarded behind `otel.enabled` + namespace-scoped `to:` block
- `values.yaml` — added `httpsCidrs` list (external auth + signing provider FQDNs as CIDR); documented broad-443 fallback when empty (honest residual)
- `NetworkPolicyEgressGuardTests.cs` (new) — xUnit arch-guard validating every egress rule is scoped (`to:`) or ports ⊆ {53, 443}

Build: 0W/0E · Architecture.Tests: 161/161 (158+3 new).

## Technical Trade-offs & Decisions

### 1. Vanilla K8s NetworkPolicy ≠ FQDN Egress
VanillaNetworkPolicy supports `namespaceSelector` (pod selectors within cluster) or `ipBlock` (CIDR outside). FQDN matching requires a service mesh (Istio `VirtualService`). 

External destinations: auth endpoint (`auth.aqtcorefw.internal`, static IP in secrets), signing providers (Viettel/VNPT, public HTTPS). **Cannot lock 443 by FQDN alone.**

Design choice: `httpsCidrs` allowlist (values-driven, documented as operator responsibility) + safe default (broad 443) when empty = **honest trade-off, not false-narrowing.** If operator omits CIDRs, egress 443 is open; design document must warn this is residual risk (P7 blocker: egress mesh gateway).

OTEL 4317/4318: NO collector wired yet (default `otel.enabled: false` → renders nothing). When enabled, ports rendered inside scoped `- to: [namespaceSelector: otel-system]` block only. Loopback instrumentation endpoints bypass NetworkPolicy anyway.

### 2. Tooling Gap → Template Parsing Guard (Not Helm/Rego Test)
No `helm` / `conftest` / `helm-unittest` in CI pipeline (parked). A `helm template` rego assertion would false-green (never executes). A Helm chart unit test would mock-pass without validation.

**Chose:** Static-source xUnit guard reading **template text as string** (pattern mirrors `MarsConnectionStringGuardTests`). Architecture-test suite runs in normal `dotnet test` (already CI-gated, no new tooling). 

Limitations: Asserts template structure, NOT rendered output (e.g., doesn't proof otel.namespace expansion). Documented + acceptable (rendered proof is an e2e concern, template structure is a code-review concern).

### 3. Helm Template Nesting Gotcha
First impl of `ExtractGuardedBlock` naively grabbed the FIRST `{{- end }}` from the OTEL block. Problem: the strict ipBlock loop is nested:
```yaml
{{- if and .Values.otel.enabled ... }}
  - to:
    - namespaceSelector: ...
    - ipBlock:
        cidr: ...
        {{- range .Values.httpsCidrs }}
        - {{ . }}
        {{- end }}  ← INNER end
{{- end }}           ← OUTER end (wanted this)
```

**Fix:** Balance-aware depth counter (if/range/with → +1, end → -1). Test mutation (4317 in fallback → test red ✓; delete strict block → test red ✓; restore byte-identical → test green ✓) proved non-vacuous.

## Verification

**Code review:** APPROVE_WITH_NITS
- Nit #1 (OTEL-coupling doc): Added OTEL-OFF-by-default callout in values.yaml comment + FU filed (MSVC-P6-OTEL-COUPLING-DOC: operators must flip otel.enabled when wiring a real collector; telemetry silently drops otherwise)
- Nit #2 (nil-safety): Chart uses no `dig` convention; skipped defensive check (acceptable per project Helm maturity)
- Nit #3 (`otel.namespace` grouping): Moved namespace param adjacent to enabled flag in values.yaml (clarity)

**Verification rigor:** Reviewer mutation-tested the guard itself (not just "test exists"):
- Injected 4317 into unscoped fallback → test C red (fact: 4317 caught)
- Deleted ipBlock strict branch entirely → test C red (fact: absence caught)
- Restored byte-identical → test green (fact: no false-green on whitespace)
- Compared rendered outputs (before/after) → **default render is provably STRICTLY tighter** (removes any-dest 4317/4318, keeps broad 443 only) — new egress NOT opened

## Root Causes / Why It Mattered

1. **Copy-paste template edit** — The unscoped `ports:` rule was added during OTEL integration spike (pre-Phase-0) without a guard. Editor saw "ports can be a list, so unscoped = apply to all dests" and cargo-culted the syntax. No test existed to catch it. ✓ Guarded now.

2. **Default-deny architecture not force-checked** — NetworkPolicy YAML is infrastructure config, not application code. No lint/audit in the CI pipeline before now. Different from "code review catches bugs" — infra YAML is write-once, rarely re-read.

## Lessons & Follow-up

**Lesson:** Kubernetes egress `to:` is **mandatory**, not optional. Omitting it = admit all. xUnit guard pattern is portable — can reuse for other Helm templates (ServiceAccount, RBAC, Secrets mount paths).

**FU:** MSVC-P6-OTEL-COUPLING-DOC — operator runbook must document "flip otel.enabled=true ONLY when collector is running; without it, telemetry queue → silent loss, no error."

## Next Steps

1. **git-manager** — commit: `fix(infra): k8s NetworkPolicy egress FQDN/OTEL scope + arch-guard`
2. **code-review** — verify nits applied (OTEL doc + namespace grouping)
3. **Backlog:** MSVC-P6-OTEL-COUPLING-DOC (runbook entry) — not blocking ship, docs-only
4. **Roadmap:** P7 egress-gateway (FQDN mesh, defers until Istio pilot decided)

---

**Status:** DONE  
**Summary:** Egress exfil hole closed via port narrowing + namespace-scoped OTEL + xUnit guard. No new egress opened; default render strictly tighter. code-review APPROVE_WITH_NITS (nits applied). Awaiting git-manager commit.
