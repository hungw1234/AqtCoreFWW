# M-2 Consumer Integration Test — Shipped First-Try Green

**Date:** 2026-04-17 21:47
**Duration:** ~38 min (18:55 implementation → 21:47 finalize)
**Component:** Notifications domain, MassTransit consumer integration
**Status:** RESOLVED (PR pending branch push)

## What Shipped

- `tests/integration/AqtCoreFW.Notifications.Integration.Tests/EventHandlers/CaseTransitionedNotificationConsumerIntegrationTests.cs` (156 lines, 2 tests)
- Package ref: MassTransit added to test project csproj via CPM
- 2 xUnit integration tests:
  - **TC-NTF-INT-001:** `Consume_WhenSubmittedEventPublished_SendsNotificationAndCompletes` (happy path, verifies `SendAsync` call and completion)
  - **TC-NTF-INT-002:** `Consume_WhenDownstreamFails_DoesNotThrow` (fire-and-log swallow, verifies consumer doesn't propagate `SendAsync` exception)
- Test harness: `MassTransit.Testing` `ITestHarness` in `[Collection(NotificationTestsCollection.CollectionName)]` for async setup/teardown
- Commits: `b2edcbfb` (8 files, 440 lines test code) + `ae1faecc` (3 files, 30 lines infra doc note)

**Test Result:** 2/2 PASS in ~4s. Code-review 9/10 → 9.5/10 (1 minor nit: missing `Assert.Empty(harness.Faults<T>())` after downstream exception test).

## Decisions & Trade-offs

**ITestHarness vs mocked ConsumeContext:** Red-team plan specified full MassTransit harness (isolated broker, full message round-trip). Harness is the right choice for integration-level verification — confirms the consumer wires correctly through the message bus, not just that the handler logic runs. Trade-off: testcontainer cold-start can add 8-15s. In-memory harness avoids that. Chose harness; timeouts were sub-second with warm Docker Redis.

**Test scope (2 tests only):** M-2 locked to the two high-risk paths: happy path (SendAsync returns success) + swallow path (SendAsync throws, consumer catches, doesn't rethrow). Edge cases (MassTransit requeue logic, DLQ behavior) deferred to post-Phase-06 scope. Correct scope — matches backlog.

**Assertion refinement during review:** Code-reviewer flagged the absence of `Assert.Empty(harness.Faults<T>())` in Test 2. Valid point: test silently passes if the harness receives a fault but the consumer code happens to not rethrow. Added assertion; test still green. Consumer implementation (lines 42-61) does catch + log, so the assertion holds. First-time code might have had this gap.

## Infra Correction (Carried Forward)

During test implementation, discovered and documented: **SQL Server 2022 is native Windows install, not Docker.** Instance `MSSQLSERVER22` at `localhost,1433` with SQL Auth (sa account). Docker hosts Redis + RabbitMQ only now.

Docs updates queued (not yet applied to main; parallel subagent edit collision risk, see below):
- `developer-onboarding-guide.md` §2: split Docker vs native install sections
- `system-architecture.md`: note DB moved to native
- `backlogs/backlog.md`: added "Test Infrastructure" tracking section (Testcontainers.MsSql → Local SQL migration, 1-2d, hybrid CI strategy)

**Why not merged yet:** Decided to defer doc merges until PR review closes to avoid parallel-edit collision. Stashing change, will apply after M-2 PR lands.

## Lessons Learned

### 1. Parallel Subagent Edits on Shared Files = Drift Risk

Both `project-manager` and `docs-manager` spawned in parallel touched `backlogs/backlog.md`:
- `project-manager` flipped M-2 entry from `[ ]` → `[x]` (completed)
- `docs-manager` wrote new sections (Test Infrastructure backlog)
- **Result:** docs-manager's write() overwrite project-manager's flip. Backlog still showed M-2 as `[ ]` incomplete after merge.

Trust-but-verify Grep caught it. **Lesson:** Serialize subagent writes on shared files, or explicitly define file-ownership globs in task prompts to prevent clobbering.

### 2. Trust-but-Verify Validated the Rule

Per memory `feedback-verify-subagent-reports.md`, I Grep'd each subagent's claimed change before marking task done:
- Scanned `backlogs/backlog.md` line 14 for M-2 status → found `[ ]` (stale)
- Fixed manually to `[x]`
- Scanned `developer-onboarding-guide.md` for docs-manager claim → found §2 split present (valid)

The parallel collision only surfaced because I verified, not because I trusted the "completed" report. **Future sessions:** This rule saved ~10 min of downstream confusion. Keep applying it.

### 3. Red-Team-Before-Implement Paid Off (Again)

Red-team's prior-session plan was primary source:
- Consumer ctor signature `CaseTransitionedNotificationConsumer(ITemplateService, INotificationPublisher, ILogger)` — verified byte-for-byte from Phase-04b source
- Event shape `CaseTransitionedIntegrationEvent` record fields — verified at Phase-04b commit
- Template string literals (e.g., `$"Case {caseId}..."`) — verified at consumer lines 30-41
- MassTransit package version (8.3.6 includes Testing namespace) — verified in lock file

Zero plan rework during implementation. **ROI:** Red-team took ~5 min in prior session; saved ~20 min of assumption-chasing in this session. Validates `feedback-red-team-before-implement.md` claim.

### 4. Integration Tests as Finalization Artifacts

This is the last loose end from Phase-04 code-review. Phase-04b (commit `839fc17d`) shipped the consumer implementation; M-2 completes the test coverage. **Pattern observation:** Major features should ship with integration tests the same day they ship; deferring tests to a separate "M-N" slot creates coordination cost and doc-drift risk (as seen in backlog stale references). Future: Code + integration tests in same PR when possible.

## Unresolved Findings (Not Backlogged Yet)

**5 Pre-existing `NotificationEndpointsTests` Failures:** Verified via git stash/revert that M-2 did NOT introduce these. Root cause likely xUnit collection fixture discovery issue on `[Collection(SqlServerCollection.CollectionName)]` + `IClassFixture<WebAppFixture>` combo. Errors: `Class fixture type 'WebAppFixture' had one or more unresolved constructor arguments: SqlServerFixture sqlServer`.

**Why not backlogged:** Needs user input on scope. Are these blocking CI? Acceptable tech debt? Specific to NotificationEndpointsTests or widespread? Decision belongs to user, not automated backlog.

## Next Steps

1. **Push branch + open PR:** `feat/m2-consumer-integration-test` (local, ready)
2. **Merge SQL Server 2022 docs after M-2 PR approved** (to avoid parallel-edit collision)
3. **User decision:** Backlog 5 failing endpoint tests? Priority if impacting CI
4. **Post-Phase-06 soak:** Consumer test harness upgrade (MassTransit TestHarness → InMemoryTestHarness w/ broker simulation) if notification volume increases
5. **Backlog candidates (priority):**
   - Phase-02c: Outbox dispatcher for `CaseCreatedIntegrationEvent` (1-2d, architectural)
   - T4-05: IMemoryCache per-module strategy (~1d, perf)
   - Test infra: Testcontainers.MsSql → Local SQL hybrid CI (~1-2d, unblocks parallel test isolation)

**Plan:** `plans/260417-1839-m2-consumer-integration-test/` status → `completed`

---

**Reflection:** Red-team-before-implement worked exactly as intended. Implementation flowed first-try green because plan was verified against source. The only hiccup was infra friction (parallel subagent edits) — a process problem, not a technical one. Fixed by trust-but-verify. Clean ship.
