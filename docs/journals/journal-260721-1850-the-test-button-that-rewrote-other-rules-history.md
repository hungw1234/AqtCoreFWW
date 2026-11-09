# The Test Button That Rewrote Other Rules' History

**Date**: 2026-07-21 18:50
**Severity**: High
**Component**: Alerting — `AlertingAdminController.TestRule`, `AlertEvaluationJob`, `AlertingMetrics`
**Status**: Resolved

## What Happened

Backlog row `ALERTING-TESTRULE-REPORTS-202-ON-FAILED-DELIVERY` (T4, filed 2026-07-17) said: an admin
testing a broken EMAIL rule gets `202 Accepted` while delivery actually failed, because the per-rule
catch in `AlertEvaluationJob` swallows dispatch failures. True. Reading `AlertingAdminController.cs:125-134`
against the job it calls showed the row had understated its own defect by two out of three. `TestRule`
didn't test the requested rule — it called `ExecuteAsync`, which evaluates `Where(r => r.Enabled)` over
the *whole table*. Testing rule X therefore fired rule Y's real notification, called `RecordBreach()` on
Y, and moved Y's `LastTriggeredAt`. A test of one rule rewrote the operational history of others. Second
defect: a disabled rule was never evaluated at all, yet the endpoint still answered 202 — and disabled is
exactly the state of a freshly seeded rule, including the self-monitor rule seeded that same morning
(`journal-260721-1245-alert-job-alerting-on-its-own-blindness.md`). The row's prescribed fix (return
per-rule outcomes from `ExecuteAsync`) addressed only the swallowed-outcome defect and would have left
both worse ones standing. Rejected; shipped `AlertEvaluationJob.TestRuleAsync(rule, ct)` instead —
evaluates exactly the requested rule, really dispatches, writes no state.

## The Brutal Truth

I made this morning's exact misjudgement again, in the opposite direction, in the same afternoon. This
morning's entry's whole point was: I verified the plumbing and let that stand in for verifying the
semantics, and code review caught a threshold that could never fire. This afternoon I wrote, in the plan,
that the test path's `AlertingMetrics` increments were "accepted rather than engineered" — I *saw* the
question and chose not to do the arithmetic. The arithmetic that refuted me took the reviewer under a
minute, using thresholds I had shipped myself six hours earlier: `AqtCoreFWAlertEvaluationFailing` fires
at `> 5` over 5m, each failing test press records two increments, so three presses page ops about a
healthy evaluator; `AqtCoreFWAlertNotificationFailing` fires at `> 0`, so *one* press against a broken
EMAIL rule — the single most likely reason anyone uses this button, given the row's own origin — pages
ops. Worse: the test path reaches disabled rules the scheduled job never evaluates, so the slice widened
the counter's input set past what it was scoped to measure. "Accepted, not engineered" is what I write
when I've noticed a risk and decided the arithmetic wasn't worth doing. It always is.

Then, fixing that, I nearly built a second false-green on top of the first. Narrowing the alert
expressions to `source="scheduled"` would make every promtool fixture in
`infra/prometheus/alert-rules-test.yml` stop firing, since none carried a `source` label — while still
reporting green. I caught it and relabelled the fixtures, added two cases proving `source="test"` stays
silent. But `promtool` isn't installed locally and CI is off, so those fixtures were never actually run.
I edited a test file for internal consistency and cannot claim it works. That's not a fix, it's a
plausible-looking diff. Filed honestly rather than left silent.

## Technical Details

- `AlertingAdminController.cs:125-134` → `evaluationJob.ExecuteAsync(ct)`, no rule-id filter — root
  cause of defects #1 and #2.
- Response shape: `202` fixed message → `200 AlertRuleTestOutcome { evaluated, metricValue, breached,
  dispatch: { channel, delivered, detail } }`. `[ProducesResponseType(typeof(ApiResponse<...>), 200)]`
  set explicitly — the untyped attribute would have regenerated the Orval client as `void`, hiding the
  new body from FE and reintroducing the exact same class of defect client-side.
- `AlertingMetrics`: `source` tag (`scheduled`/`test`) added to all 7 increment sites; both Prometheus
  expressions and the seeded in-app rule narrowed to `source="scheduled"`.
- Build 0 errors; `Infrastructure.Extensions` 216 passed / 1 pre-existing skip (alerting 34/34, up from
  25 before this slice); Architecture 185/185.
- Mutation-proved twice: injecting `await ExecuteAsync(ct)` at the top of `TestRuleAsync` turned 3/6 new
  facts red (`DoesNotEvaluateOtherRules`, `WritesNoState`, `ReallyDispatches`); flipping `TestSource` to
  `"scheduled"` turned both new source-tag facts red. Both green on revert.
- FE consumer check reframed urgency, not correctness: `usePostApiV1AdminAlertsIdTest`
  (`web/src/api/generated/alerting-admin/alerting-admin.ts:427`) has zero callers outside the generated
  file — nobody can press this button yet. The user chose to fix the shape now, before a screen gets
  built against the broken contract, rather than defer.

## What We Tried

Nothing exotic on implementation — the "what we tried" here is entirely about catching my own two
near-misses before they shipped:

1. Re-read the plan's `Out of scope` section during the outcome write-up specifically looking for
   anything marked "accepted" without a number attached to it. Found the metrics line, did the
   arithmetic against the thresholds from `journal-260721-1245-*`, reversed it in-slice.
2. After adding the `source` tag, ran the promtool fixture file mentally against the new label filter
   before assuming it still passed — realized every existing fixture series lacked the label and would
   silently stop matching. Relabelled all of them plus added two negative cases. Then checked whether the
   file actually executes anywhere: it doesn't, locally or in CI. Did not pretend otherwise.

## Root Cause Analysis

Same root cause as this morning's entry, restated because restating it clearly didn't fix it the first
time: verifying that code compiles and mutation-tests pass is not verifying that its *consequences*
compose with the rest of the system. The test path reused the scheduled evaluator's counters because that
was the path of least resistance — same function, same increments, done. Reuse is usually the right
instinct; here it silently coupled two things (an admin manually pressing a button, and an automated
5-minute alerting window) that have completely different call rates and completely different meanings for
"this rule is unhealthy." I clocked the coupling, wrote a sentence dismissing it, and moved on. The
reviewer didn't have any more information than I did — same thresholds, same code, same morning's commit
history. The only difference was doing the multiplication.

## Lessons Learned

"Accepted, not engineered" is a tell, not a decision — if that phrase is about to go into a plan, stop
and compute whatever number would prove or disprove it first; if it's cheap to check (it was: two
thresholds, two increment counts, arithmetic a human does in under a minute), there is no excuse to leave
it as a shrug. Second: a backlog row's *fix direction* can be stale even when its *defect description* is
accurate — this is the second time today (SMS row this afternoon was the first) that the prescribed
remedy addressed the least of several problems the row's own symptom was hiding; read the code path the
row points at, not just the row's sentence. Third: editing a test fixture file for consistency is not the
same claim as "this test passes" — if the runner that would execute it isn't installed and CI is off, say
so in the same breath as the edit, don't let a green-looking diff imply a green run.

## Next Steps

Row closed, shipped. Two follow-ups filed, left open on purpose:
- `ALERTING-PROMTOOL-FIXTURES-UNVERIFIED` (T3) — install `promtool` (ships in the Prometheus tarball, no
  cluster needed) and wire a local script, or accept a weaker static-source assertion on the rendered
  template text in the meantime. Owner: whoever next touches alerting or CI.
- `ALERTING-SOFTDELETE-COMMENTS-CONTRADICT` (T4) — `AlertingDbContext.cs:9-11` claims soft-delete
  filters are "intentionally omitted," but `AlertRuleConfiguration.cs:10` applies one and it works.
  Behaviour is correct; the comment is stale. Whoever touches the file next should delete the wrong
  comment rather than re-derive the truth from scratch again.
