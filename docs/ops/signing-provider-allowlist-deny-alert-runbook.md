# Signing Provider-Allowlist Deny — Alert Runbook

**Scope:** Alerting for the signing provider-allowlist guard (`SigningProviderAllowlistGuard`).
**Backlog:** `SIGN-REQUEST-PROVIDERKEY-NO-TENANT-ALLOWLIST`.
**Audience:** SOC / on-call ops enabling and triaging the deny-rate alert.

## What the guard does

`SignatureOrchestrator.RequestSignatureAsync` calls the guard first (shared chokepoint for the self-sign HTTP
path and the Forms submission-signing path). It denies (**403**) a request whose **assigned signer** holds no
Approved active enrollment of the requested provider's `EnrollmentKind`. Every deny emits **two** independent
records:

- **Audit row** — `action = SIGNING_PROVIDER_ALLOWLIST_DENIED` (module `Identity`). Forensic per-event record;
  carries `providerKey`, `requiredKind`, `signerUserId`, `tenantId`, `reason`. The alert evaluator does NOT
  query the audit DB.
- **Prometheus counter** — `signing_provider_allowlist_denied_total{provider,tenant,reason}` (meter
  `AqtCoreFW.Identity`, scrapeable at `/metrics`). This is the alert trigger.

`reason` is `unmapped` (a registered provider with no `ProviderEnrollmentKindMap` entry — a config gap) or
`not_enrolled` (an enrolled signer lacking an Approved enrollment of the required kind).

## The AlertRule (rate-based)

The alert evaluator (`AlertEvaluationJob`, Hangfire cron `* * * * *`) is a **Prometheus-query rule
evaluator** — it has no imperative "raise now" API, so the alert MUST be a threshold over the counter.

**Startup seed (disabled).** `AlertRuleDevSeeder` idempotently seeds this rule **DISABLED** at API startup
when config `Alerting:DevSeed:Enabled=true` (default OFF — no production auto-seed). A disabled rule is skipped
by the evaluator (`.Where(r => r.Enabled)`), so it adds zero noise while making the rule PRESENT for ops to
enable. The seed only ever CREATES the disabled rule; it never re-enables or overwrites an ops-tuned rule.

**Enable / create.** Ops enables the seeded rule (or creates it where the seed did not run) via
`POST /api/v1/admin/alerts` (SystemAdmin + MFA) and owns its `Enabled`/`Threshold` state per environment.

The seed is intended for **local dev** (flag OFF everywhere by default). It is idempotent by rule Name via a
check-then-insert, which is NOT atomic across replicas — so do NOT enable `Alerting:DevSeed:Enabled` on a
multi-replica deployment (two instances could each insert a duplicate rule). For staging/prod, either create
the rule directly via the admin API, or run the seed from a single instance only.

| Field | Value |
|-------|-------|
| `Name` | `Signing provider allowlist denials` |
| `MetricName` | `sum(increase(signing_provider_allowlist_denied_total[5m]))` |
| `Condition` | `GT` |
| `Threshold` | `5` (env-tunable — see below) |
| `WindowMinutes` | `5` |
| `NotifyChannel` | `Log` (Webhook deferred until a SOC endpoint exists; Email not wired) |
| `NotifyTarget` | `null` |

`MetricName` accepts the full PromQL expression verbatim (`AlertEvaluationJob` passes it to
`GET /api/v1/query?query=...`). Validate the query against the live Prometheus in staging before enabling in
prod. Keep the rule **disabled** in any environment where Prometheus is absent (e.g. local dev) to avoid
per-minute soft-warnings — a disabled rule is skipped entirely by the evaluator.

### Threshold guidance

A sustained deny spike is the signal. Baseline the environment's normal deny rate (ideally ~0 once the
pre-merge data audit has reconciled mis-tagged enrollments) and set `Threshold` a few denies above noise. Start
at `5/5m` and tune down if the environment is quiet.

## Triage — IMPORTANT: this is a deny-RATE (mixed-cause) signal, not pure attack detection

The `not_enrolled` deny fires for BOTH a genuine FE-bypass attempt AND a legitimate enrolled-signer whose
client sent the wrong `providerKey` (mis-configured FE, wrong-kind retry). The metric alone **cannot** separate
them — splitting the `reason` tag does not help (cross-kind IS `not_enrolled`). Root-cause per event via the
**audit rows**:

1. Pull `SIGNING_PROVIDER_ALLOWLIST_DENIED` audit rows for the alert window.
2. Inspect `signerUserId` + `providerKey` + `requiredKind`:
   - **Same signer, repeated, provider they ARE enrolled for a different kind of** → likely a mis-configured
     client / wrong-kind retry. Reconcile the client's providerKey or the signer's enrollment kind.
   - **Many distinct signers / unexpected providerKeys / no plausible enrollment** → possible FE-bypass probe.
     Escalate per the security incident process.
   - **`reason = unmapped`** → a provider was registered without a `ProviderEnrollmentKindMap` entry. This is a
     deployment/config defect, not user behavior — the arch drift test should have caught it at CI. Add the map
     entry (see the VNPT revival checklist) and redeploy.

## Related

- Guard: `src/modules/identity/AqtCoreFW.Identity.Application/Signing/SigningProviderAllowlistGuard.cs`
- Metric: `src/modules/identity/AqtCoreFW.Identity.Application/Telemetry/SigningMetrics.cs`
- Map: `src/modules/identity/AqtCoreFW.Identity.Infrastructure/Services/Signing/ProviderEnrollmentKindMap.cs`
  (behind the `IProviderEnrollmentKindMap` abstraction in Identity.Application — keys stay out of Domain/Application per D6/QĐ742)
- Evaluator: `src/shared/AqtCoreFW.Infrastructure/Alerting/AlertEvaluationJob.cs`
