# The Alert Job Couldn't See Itself (Because Nothing Could)

**Date**: 2026-07-21 12:45
**Severity**: High
**Component**: Observability — `AlertEvaluationJob`, OpenTelemetry metrics, Prometheus exporter
**Status**: Resolved

## What Happened

Backlog row `ALERTING-LOGCRITICAL-HAS-NO-ALERT` (T2) asked for "an ops-side alert on the job's LogCritical, not more guard code." That fix cannot exist in this repo: log sinks are Console + File only (`SerilogConfiguration.cs:27-33`), no aggregator, and there is no alert-rule or alertmanager file anywhere under `infra/` — `infra/prometheus/recording-rules.yml` holds recording rules only. Nothing to alert on, nowhere to put a rule. Pivoted, with user approval, to metric counters plus a seeded self-monitor `AlertRule`.

Scoping that pivot surfaced something worse than the row described: every module `Meter` (`AqtCoreFW.Audit`, `.Identity`, `.Workflow`, `.BusinessProcess`, `.Collaboration`) was exported via OTLP only. `/metrics` is served by prometheus-net, which cannot read `System.Diagnostics.Metrics` meters at all. So `audit_ingestion_lost_total`, `signing_provider_allowlist_denied_total`, and every other counter shipped since 2026-07-04 were invisible to anything scraping Prometheus — including `AlertEvaluationJob` itself, which alerts *by querying Prometheus over HTTP*. A rule seeded against `signing_provider_allowlist_denied_total` would have been silent forever had ops ever enabled it.

Shipped in two commits on purpose: `0bb63099b` adds `OpenTelemetry.Exporter.Prometheus.AspNetCore` and a new `/metrics-otel` endpoint (revives the 8 already-shipped-but-dead counters, standalone value even without the alerting change); `c6b09a283` adds `AlertingMetrics` (two counters, closed non-PII tag sets) wired into all 6 failure branches of the evaluation job, plus a self-monitor rule seeded disabled. Merged as PR #126, merge commit `822f15027`.

## The Brutal Truth

The backlog row's prescribed fix was never buildable — it pointed at infrastructure that doesn't exist. That's a cheap lesson: verify the fix direction is physically possible before scoping effort. The expensive lesson is what I did *after* proving the pipeline worked end-to-end: I treated "counter shows up at `/metrics-otel`" as proof the alerting semantics were correct, and shipped a threshold that could never fire. `GT 10` looked reasonable until I did the arithmetic on the job's own cadence — the job runs every minute, so a continuously failing rule accumulates ~10 over a 5-minute window, and `GT 10` is false at exactly 10. With a single enabled rule, it would never have breached. I didn't catch this. Code review did. Verifying the plumbing is not verifying the alert — and I'd conflated the two with enough confidence to write it into a seeded default.

I also wrote a comment claiming the self-monitor rule catches "the outage no other rule can detect." False, and I should have seen it while writing it: the self-monitor rule is evaluated by the exact same job over the exact same query path as every other rule, so during a total Prometheus outage its own query fails too and it can never breach. It only catches sustained *partial* degradation. Grepped `infra/` for `up{}` or any watchdog/dead-man's-switch pattern — zero hits, so that gap is real and uncovered today. Filed as `PROMETHEUS-DEADMAN-SWITCH-MISSING` rather than scope-creeping this slice into ops-side Alertmanager config that doesn't exist yet.

## Technical Details

- `AlertingMetrics` (new, `src/shared/AqtCoreFW.Infrastructure/Alerting/`): `alerting_notification_failed_total{channel, reason}` and `alerting_evaluation_failed_total{reason}`. Deliberately not tagged with `AlertRule.Name` (operator-supplied, unbounded cardinality) or `NotifyTarget` (an email address — PII in a scraped Prometheus label).
- `/metrics-otel` (new route, separate from `/metrics`): both are terminal handlers writing a full exposition body; sharing one path means whichever registers first silently answers every scrape while the other's series vanish with no error. ServiceMonitor updated to scrape both.
- Metric names were **observed, not derived** — spun up a throwaway host with the identical package pair and scraped it. Annotation-only UCUM units (`{event}`, `{denial}`, `{failure}`) contribute no suffix, so counters are just dots→underscores plus `_total`; the 2026-07-04 seed string needed no change. A real unit does suffix: `workflow.execute_transition.duration_ms` with unit `ms` exports as `..._milliseconds`. Recorded in `docs/dependency-map.md` as a name-coupling contract because `AlertRuleDevSeeder` seeds a rule whose `MetricName` is the literal PromQL string — renaming the counter breaks that rule silently.
- End-to-end proof, live process: with the seeded rule enabled and no Prometheus reachable in dev, `/metrics-otel` returned `alerting_evaluation_failed_total{reason="query_failed"} 1` and `{reason="no_data"} 1`.
- Threshold bug: `GT 10` → `GT 5`, comment in `AlertRuleDevSeeder.cs:54` now carries the arithmetic (5 runs/5min × 2 increments per failing run ≈ 10 → 5 fires on the very first sustained failure), pinned by a test.
- Three telemetry files (`AuditMetrics.cs`, `SigningMetrics.cs`, `CollaborationMetrics.cs`) had XML docs asserting "The Prometheus exporter appends the conventional `_total` suffix" — a claim about a pipeline that had never existed. Corrected.

## What We Tried

Two evidence-trimming mistakes, both caught before shipping but both from the same habit — read a truncated slice of output and reason confidently from the fragment instead of the whole thing.

1. Ran `git remote -v | head -2`, saw only `bitbucket`, concluded `origin` didn't exist and the repo's PRs must live elsewhere. There are three remotes (`bitbucket`, `oracle`, `origin`); `bitbucket` sorts first alphabetically-by-coincidence. `origin` is the actual GitHub repo where PRs #123–#126 all merged.
2. Asserted the dependency-map needed no update because `AlertingMetrics` is "Infrastructure-internal." Code review checked and found `docs/dependency-map.md` already documents the sibling `SigningMetrics` in exactly that shape — the pattern for documenting infra-internal telemetry classes was already established and I'd almost skipped it.

Also hit a real test-isolation failure, not a false alarm: the counters are static/process-wide, so the first `MeterListener` assertion in the new test class picked up measurements from a sibling test class running in the same test collection in parallel, and failed with four foreign tag sets mixed into the expected set. Fixed with a shared `[CollectionDefinition(..., DisableParallelization = true)]` spanning both classes that drive the job. Separately, the webhook-channel test initially exercised nothing at all — `WebhookUrlValidator` DNS-resolves the target host, and `hooks.example.gov.vn` doesn't resolve offline, so the guard rejected before ever reaching the POST it was supposed to test. Switched to a public IP literal to clear the validator's fast path without needing DNS.

## Root Cause Analysis

The upstream defect (module meters OTLP-only, `/metrics` blind to them) is a straightforward gap: whoever wired `AddMeter(...)` calls across 5 modules over the past weeks never confirmed a Prometheus scraper could actually read them, because nothing that exercised the counters also asserted they were externally visible. The counters looked done — they compiled, they incremented in unit tests via `MeterListener` — but "increments in-process" and "visible to the thing that queries Prometheus" are different claims, and every one of those PRs shipped on the first claim alone.

The threshold bug is the same root cause one level up: I verified the counter→endpoint pipeline exhaustively (host-level scrape, live process, exact metric text) and let that stand in for verifying the alert's *decision logic*. Plumbing correctness is not semantic correctness. Nobody — including me — ran the actual math on "how many failures accumulate in one evaluation window" until code review forced it.

## Lessons Learned

A metric that increments and a metric that's queryable are two separate facts — check both, explicitly, every time a new counter is added. Before trusting a default threshold on a self-referential monitor (a thing that watches itself), compute the accumulation rate against the evaluator's own cadence; "looks like a reasonable number" is not a threshold, it's a guess with units. And a monitor that shares its own failure mode with the thing it's meant to catch (self-monitor rule evaluated by the same job it's checking) cannot detect a total outage of that shared dependency — say so explicitly in the code comment, don't let "self-monitoring" imply "monitors everything." Finally: `head -N` on evidence-gathering commands (`git remote -v | head -2`) truncates exactly the row that would have corrected the wrong conclusion — don't pipe through a limiter on a command whose whole point is completeness.

## Next Steps

None blocking — row closed, PR #126 merged to `main`. Two follow-ups filed and left open, deliberately not folded into this slice:
- `PROMETHEUS-DEADMAN-SWITCH-MISSING` (T2, ops) — needs a Prometheus-side `up{job="aqtcorefw"} == 0` alert plus an always-firing watchdog routed to an external receiver. Blocked on the same gap as everything else in this area: no Alertmanager config exists in the repo yet, so this is ops-side infra work, not app code.
- Ops must confirm two things on first deploy that were not verifiable locally: `helm template` was never run (helm isn't installed here) so the second ServiceMonitor endpoint block is checked by reading only, not rendering; and the actual cluster scrape of `/metrics-otel` is unverified outside the dev host. Also flagging for whoever manages package pins: `OpenTelemetry.Exporter.Prometheus.AspNetCore` 1.15.3-beta.1 has never shipped a stable release — pinned to match the repo's existing OpenTelemetry 1.15.3 line, consistent with an existing prerelease precedent already in `Directory.Packages.props`, but worth revisiting when a stable build lands upstream.
