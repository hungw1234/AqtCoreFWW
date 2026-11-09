# Security Incident Runbook — AqtCoreFW

**Version:** 1.0
**Effective:** 2026-03-24
**Owner:** Platform Security Team

---

## Severity Classification

| Severity | Definition | Response SLA |
|----------|------------|--------------|
| P1 — Critical | Active data breach, RCE, auth bypass, tenant data leak | 15 min |
| P2 — High | Suspected breach, privilege escalation, credential exposure | 1 hour |
| P3 — Medium | Vulnerability discovered (no active exploit), anomalous access | 4 hours |
| P4 — Low | Policy violation, failed attack attempt, informational finding | 24 hours |

---

## Phase 1 — Detection

**Sources:**
- Grafana alert: `ErrorRateHigh`, `AvailabilityBudgetBurn`
- SIEM / audit log anomaly (unusual tenant cross-query, bulk data export)
- Dependency vulnerability scan in CI (`dotnet list package --vulnerable`)
- Semgrep / SAST finding in PR
- External report (bug bounty, user report)

**Initial triage checklist:**
- [ ] Confirm alert is not a false positive (check Grafana SLO dashboard)
- [ ] Identify affected tenant(s), endpoint(s), time window
- [ ] Preserve evidence: export relevant Grafana snapshots + audit log entries
- [ ] Assign severity (P1–P4)
- [ ] Notify incident commander (P1/P2: immediate page; P3: Slack #security-incidents)

---

## Phase 2 — Triage

**For suspected tenant data leak:**
1. Query audit log: `SELECT * FROM audit.AuditEntries WHERE TenantId = @suspect AND CreatedAt > @windowStart ORDER BY CreatedAt DESC`
2. Check API gateway logs for abnormal request volumes or cross-tenant patterns
3. Verify RLS policy status: `SELECT * FROM sys.security_policies WHERE is_enabled = 1`
4. Verify EF global filters active (no `IgnoreQueryFilters` outside Admin)

**For credential / secret exposure:**
1. Rotate affected secrets immediately (K8s secret → Vault → redeploy)
2. Invalidate all issued tokens via Keycloak realm → Sessions → Logout all
3. Check git history: `git log --all -p -- '*.json' '*.cs' | grep -i password`

**For vulnerability in dependency:**
1. Identify CVE from `reports/vulnerable-packages.txt`
2. Check exploitability against actual usage in codebase
3. Pin or upgrade affected package; add to `Directory.Packages.props`

---

## Phase 3 — Containment

| Scenario | Containment action |
|----------|--------------------|
| Active exploit of API endpoint | Add rate-limit rule in ingress-nginx; deploy WAF rule |
| Compromised pod / container | `kubectl delete pod <name> -n <ns>`; scale down deployment |
| Leaked DB credential | Rotate SQL login password; cycle K8s secret; redeploy |
| Tenant data cross-contamination | Enable maintenance mode; isolate affected tenant namespace |
| Compromised service account | `kubectl delete secret <sa-token>`; rotate service account |

**Maintenance mode toggle (emergency):**
```bash
kubectl set env deployment/aqtcorefw MAINTENANCE_MODE=true -n <namespace>
```

---

## Phase 4 — Eradication

- [ ] Remove the root cause (patch vulnerability, fix misconfiguration, revoke access)
- [ ] Verify RLS policies intact on all schemas: `cases`, `files`, `audit`, `notifications`
- [ ] Re-run full test suite including tenant isolation tests
- [ ] Re-run Semgrep scan: `semgrep --config .semgrep/ src/`
- [ ] Re-run `dotnet list package src/AqtCoreFW.slnx --vulnerable`
- [ ] Confirm no residual attacker access (check K8s RBAC, Keycloak roles)

---

## Phase 5 — Recovery

- [ ] Restore service from known-good state (previous release tag if needed)
- [ ] Verify SLO metrics return to baseline (Grafana SLO dashboard)
- [ ] Monitor for recurrence for 24h post-recovery
- [ ] Notify affected tenants per PDPL breach notification requirements (≤ 72h)
- [ ] Update internal status page

---

## Phase 6 — Post-Mortem

**Timeline:** within 5 business days of incident closure.

**Template:**
```
## Incident Summary
- Date/time:
- Duration:
- Severity:
- Affected tenants/users:

## Root Cause
(5 Whys analysis)

## Timeline of Events
- HH:MM — detection
- HH:MM — containment
- HH:MM — eradication
- HH:MM — recovery

## Impact
- Data exposed: yes/no (if yes: records count, classification level)
- SLO breach: yes/no (minutes of downtime / error budget consumed)

## What Went Well

## What Could Be Improved

## Action Items
| Action | Owner | Due |
|--------|-------|-----|
|        |       |     |
```

Post-mortem stored in: `docs/post-mortems/YYYY-MM-DD-<slug>.md`

---

## Key Contacts

| Role | Contact |
|------|---------|
| Incident Commander | Platform Lead |
| Security Lead | Security Team |
| DPO (PDPL notifications) | Legal/Compliance |
| On-call Engineer | PagerDuty rotation |
