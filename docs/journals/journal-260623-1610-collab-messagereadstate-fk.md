# Journal — Collaboration MessageReadState → Message Foreign Key

**Date:** 2026-06-23 16:10 ICT · **Skill:** `/cook COLLAB-MESSAGEREADSTATE-FK-RLS` · **Commits:** `2b8ff20a` (code), `c2f34e18` (docs) — pushed origin + bitbucket.

## Session goal

Close the follow-up filed when the per-message read-receipt feature shipped earlier today: the new
`collab.MessageReadStates.MessageId` column had no foreign key, and the table's RLS/tenant posture was
flagged as undecided.

## What worked

- **Empirical audit before opening (INVARIANT 260522-A) paid off twice** even though the row was filed
  the same day. Two of the row's own claims were wrong on inspection:
  1. The "cascade-on-message-delete" suggestion was moot — `grep` for `.Remove/.RemoveRange/ExecuteDelete/
     HardDelete` across the whole module returned zero. Messages are only ever soft-deleted, so a cascade
     would be dead code. Orphaned receipts are impossible today; the FK is a referential-integrity guard,
     not a fix for an active leak.
  2. "Document under ADR-009's allowlist" was a category error. ADR-009's guard
     (`SharedInfraTenantTableIsolationGuardTests`) walks **shared-infra** DbContexts only; `MessageReadStates`
     lives in the **module** `CollaborationDbContext`, which the guard never sees. Module RLS only covers
     `TenantId`-bearing tables; this table is intentionally tenant-less (tenant-safety rides on the
     tenant-scoped message lookup, already test-proven). So RLS was a non-issue — document-only.
- **FK NoAction** (user's choice) matched the module convention exactly (`Thread→Message`,
  `Message→Conversation` are both NoAction). Migration came out FK-only with no schema drift; the FK column
  was already covered by the composite unique `(MessageId, UserId)` index, so EF added no redundant index.
- **Teeth test** proves the FK is live in the DB (orphan insert → `DbUpdateException`), and after the
  review LOW-2 note it pins the assertion to the FK *by name* in the inner exception message — so a future
  added constraint on the same insert can't turn it into a false-green.

## What didn't / lessons

- **`dotnet ef` local-tool resolution quirk on Windows:** `dotnet tool restore` reported success, but
  `dotnet ef` (and `dotnet tool run dotnet-ef`) kept printing "Run dotnet tool restore". Worked around it
  by invoking the already-installed global `~/.dotnet/tools/dotnet-ef.exe` directly. Lesson: when the local
  manifest tool won't resolve, fall back to the global tool path rather than re-restoring in a loop.
- **Row decay is real even at zero age.** A follow-up row written hours earlier still encoded two
  inaccuracies (cascade, ADR scope). Probing the actual schema/guard scope took ~5 min and prevented both a
  needless cascade and a misplaced ADR edit.

## Decisions made

- FK `OnDelete(NoAction)` over cascade — convention consistency + cascade is dead code given soft-delete.
- RLS/tenant = document-only (comment in the entity config: "do not restore parity by adding RLS here").
  No ADR-009 edit (wrong scope). No dependency-map edit (intra-module FK, no cross-module edge/contract).
- Deploy note + a cheap pre-deploy orphan-check SQL recorded in the changelog (review LOW-1, operational).

## Test status

Build 0 errors · Collaboration integration **20/20** (1 new teeth test) · Architecture guards **145/145** ·
no public-contract change. code-reviewer: DONE, 0 blocking, 2 LOW (both applied/recorded).

## Next session

- Backlog `COLLAB-CHATHUB-ISMEMBER-INTEGRATION-TEST` (T4) — ChatHub join-gate + `IsMemberAsync` still have
  no direct test.
- No carry-over from this task; it shipped complete.
