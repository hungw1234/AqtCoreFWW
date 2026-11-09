# RabbitMQ readiness probe — and two assumptions I invented instead of read

**Date:** 2026-07-17 (afternoon, ~15:58–17:05 ICT)
**Commits:** `beba99e8` (docs/backlog housekeeping), `1613bdd1` (feat/health) — both pushed to `origin/main`
**Branch:** `main` · working tree clean · ahead/behind 0/0

---

## Session goal

`/aqt-next` to orient after the PR #121 merge, then cook the cheapest remaining P1
stable-monolith row. Landed on `P1-STABLE-RABBITMQ-HEALTHCHECK` (T2, effort S) over
`P1-STABLE-MIGRATION-PREDEPLOY-DRYRUN` (T2, effort M) — smaller blast radius, fully
local-verifiable, late in a 4-ship day.

## What worked

- **Housekeeping closed** (`beba99e8`) — the previous session's backlog-optimize pass + PR #121
  merge notes were sitting uncommitted. Verified the merge via `gh pr view 121` (MERGED) rather
  than trusting the backlog's claim.
- **Probe shipped** (`1613bdd1`) — transport-gated RabbitMQ check on `/health/ready`, `Degraded`,
  9 new tests, full solution 0W/0E, `Infrastructure.Extensions.Tests` 183/183 (+4 over the 179
  baseline). Detail → `docs/project-changelog.md` + `backlogs/completed.md`.
- **Mutation-proof on both gates** — inverting the transport gate turned 3 tests RED; disabling
  both cache branches turned the 2 cache tests RED. Neither test file is vacuous. This is the only
  reason I believe the tests are worth anything.
- **Two design calls the user made, both right:** gate the probe behind `Bus:Transport` (the AI
  probes' existing precedent), and `Degraded` not `Unhealthy` (the outbox absorbs an outage).

## What didn't / lessons

Three self-inflicted, all worth remembering. None were caught by tests — two by code-review, one
by a re-run.

**1. I fabricated package semantics in a code comment (Critical).**
Wrote `AddRabbitMQ(async _ => await factory.CreateConnectionAsync(), ...)` under a comment
asserting *"AddRabbitMQ caches the connection it resolves."* I invented that. The package's own
shipped XML doc — the file I already had open to look up the overload signature — says the
opposite two lines below: *"factory will be called each time the healthcheck route is requested"*,
and the async overload never disposes the result. ~8,640 leaked broker connections/pod/day at the
Helm `periodSeconds: 10` cadence. The nasty shape: it leaks only on the HEALTHY path, only under
`Bus:Transport=RabbitMQ` (staging/prod), never in dev/CI where the gate strips the probe — a probe
added to detect a broker outage would eventually have caused one.
Caught by `code-reviewer`, who read the upstream source at the pinned commit rather than taking my
comment at face value. I verified the XML doc myself before fixing.

**2. Then I did it again, fixing the first one (N1).**
The timeout nit fix passed `timeout: 3s` to `AddRabbitMQ` — assuming that bounded the probe. It
does not: the framework implements registration timeout as a linked token + `CancelAfter`, and the
package forwards that token to `CreateChannelAsync` only, never to the connection factory. A
black-holing broker holds the connect for `RequestedConnectionTimeout` — **30s** default, which I
measured with a throwaway xunit fact rather than taking the reviewer's number on trust. Same root
error as #1: read the signature, invent the semantics.

**3. I told the user their .NET install was corrupt. It wasn't.**
`dotnet --version` failed once with "No .NET SDKs were found". Instead of re-running it — the
cheapest possible probe — I went hunting for confirming evidence and found things that *looked*
like it: `sdk\10.0.200` is a hollow shell with no `dotnet.dll` (true, but it's a harmless leftover —
I listed the `sdk` dir, read entry #1, and stopped, one line above **10.0.302 with 195 files**);
"no 10.x runtime" (false — I skimmed past `Microsoft.NETCore.App 10.0.10`). Then I invented "the
SDK vanished in the last 24h" to explain it. `dotnet --list-sdks` refuted the whole thing in one
command, same shell, nothing changed in between. The original failure never reproduced; root cause
unknown (transient probe race or AV lock) and not worth guessing. Cost: ~10 min + nearly had the
user reinstall a healthy toolchain.

**Also:** `git-manager` hit a stale `index.lock` and correctly reported BLOCKED instead of
`git reset --hard` (the invariant from 2026-07-16 held). I verified staleness empirically — 0-byte
file, `Get-Process git` empty, timestamp matching the agent's own run — before removing just the
lock file.

**Disagreed with the reviewer on one point:** they said the lost trailing newline in
`tests/Directory.Packages.props` matched house style because `src/`'s copy lacks one. `git show
HEAD` proved the tests file *did* have one — `dotnet add package` stripped it, along with 3
section-separating blank lines. Restored; diff went from 6 changed lines to 1.

## Decisions made

- **Probe gated on `Bus:Transport`, not unconditional** (user) — the bus defaults to InMemory, so
  an ungated probe swaps a false-green for a false-red. Follows the `AI:Ollama:Enabled` precedent
  in the same method.
- **`Degraded` not `Unhealthy`** (user) — matches Redis; outbox + retry absorb transient outages
  and most traffic never touches the broker.
- **`ProbeConnectionSource` takes `Func<Task<IConnection>>`, not `ConnectionFactory`** — the
  concrete factory has no interface and can't be doubled; the indirection is what made the cache
  testable. Extracted to its own file rather than a third type in `HealthCheckConfiguration.cs`.
- **`public`, not `internal` + `InternalsVisibleTo`** — the assembly has no IVT today; one public
  helper widens the surface less than opening the whole assembly for tests.
- **AC "broker down → Degraded" left without an automated test** — it's the package's own
  behavior and needs a live broker. Deliberately NOT mocked; a mock would have proven only that my
  mock returns what I told it to. Tracked instead.

## Test status

| Suite | Result |
|---|---|
| `AqtCoreFW.Infrastructure.Extensions.Tests` | 183 passed / 1 skipped (pre-existing) / 0 failed |
| Full solution build (`src/AqtCoreFW.slnx`) | 0 Warning / 0 Error |
| Mutation check — transport gate inverted | 3 RED (expected), restored |
| Mutation check — cache disabled | 2 RED (expected), restored |

CI not observed: GitHub Actions stays disabled by user choice (Free-tier, solo dev). Local-verify
is the closure bar, consistent with PR #121.

## Next session

1. **`P1-STABLE-MIGRATION-PREDEPLOY-DRYRUN`** (T2, M) — the `UX_FormTemplate_PublicCode`
   `THROW 50000` guard hard-fails boot on dirty data + unhandled Hangfire boot lock timeout. Wants
   a fresh session with a prod-like snapshot; it's the real "clean boot/migration" gap in the
   Definition-of-Stable.
2. `P1-STABLE-CI-GATES-ENFORCE` (T2, S–M) — needs the key-custody decision first.
3. `P1-STABLE-OBSERVABILITY-HYGIENE` (T4, S) — EMAIL alert channel stub; cheap batch.
4. **FU-A** remains the sole gate on physical extraction — non-code, user-owned, drafted since
   07-13.
