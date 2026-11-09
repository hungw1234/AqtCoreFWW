# Journal — WEB-TYPECHECK-TEST-CI-GATE stale close (docs-only)

**Date:** 2026-05-28 ~19:15 ICT  
**Commit:** `cc525857` "docs(backlog): close stale WEB-TYPECHECK-TEST-CI-GATE — CI gate wired 2026-04-14" — pushed origin + bitbucket

## Session goal

Close `/cook WEB-TYPECHECK-TEST-CI-GATE` (backlog row: "typecheck:test is NOT in any CI workflow → add it to the FE CI job" claimed ~30min docs fix). Applied INVARIANT 260522-A (audit-at-slice-start) and found the gate already wired.

## What happened

**Initial claim:** `typecheck:test` missing from CI → FE test-type-rot not caught (filed after commit `1a56c684` resolved 26 accumulated test tsc errors 2026-05-28).

**Empirical audit (260522-A):** Grepped `.github/workflows/ci.yml` for the command. **Initial probe false-negative:** grepped for string "typecheck" (the package.json alias name, `web/package.json:18`) → found zero matches → appeared ungated. **Recheck:** read ci.yml:260-261 directly; `npx tsc --noEmit -p tsconfig.test.json` is present in the `frontend` job. That IS the underlying command `typecheck:test` runs. Verified git history: wired by commit `bb555c1e` (2026-04-14, "fix(web): resolve test tsc errors + wire CI typecheck:test gate (T5-21-C)") — six weeks before this row was filed.

**The real problem + answer (review-audit §2 — validate against threat model):** Gate exists since 2026-04-14 but 26 type errors still accumulated to 2026-05-28. Why? The gate is **not enforced**. This repo is GitHub private-free → branch protection / required-status-checks unavailable (HTTP 403 "Upgrade to GitHub Pro or make public"). CI goes RED on type errors but cannot block push/merge to main. Row's stated remedy ("add to CI") would not have prevented the accumulation even if the gate were absent — the real gap is enforcement (a GitHub plan limitation), not workflow coverage.

**Verdict:** WEB-TYPECHECK-TEST-CI-GATE STALE — gate already wired, command already running, zero code work needed. Mechanisms that WOULD achieve prevention: (a) GitHub Pro → branch protection required checks (out of scope); (b) local pre-push hook via husky (new devDep + workflow, not scheduled). User chose close STALE only (docs-only).

## Changes (1 file, docs-only)

- **backlogs/backlog.md:** Closed WEB-TYPECHECK-TEST-CI-GATE with strikethrough + evidence-cited CLOSED note (ci.yml:260-261 + bb555c1e + enforcement nuance); removed from Top-open-items; tier table T3 6→5; total open 17→16.

Per rule 260527-K (docs-only cook skips code-reviewer/tester), substitute gate: direct ci.yml read + git log for commit date + source-citation (file:line + commit hash).

## Why this matters (lessons)

1. **(260527-F reinforcement — high-value catch)** When checking "is X gated in CI", grep the UNDERLYING COMMAND/TOOL INVOCATION (`tsc`, `vitest`, `dotnet test`), NOT the package.json script-alias name. CI workflows frequently inline the command, bypassing aliases. The false-negative (grepping "typecheck" found nothing) misled a prior-session audit into flagging this row as open-real ~30min. Slice-start re-verification (reading actual ci.yml lines, not just alias-grepping) caught it.

2. **(Reinforces 260522-A)** A backlog row filed post-fix (this one filed after `1a56c684` resolved test tsc errors) often reflects a problem ALREADY SOLVED by that fix, OR describes a non-existent state. Audit the claim empirically before trusting the row's age. Six-week-old wiring + zero mention in the `1a56c684` commit message (despite exact same scope) was a red flag worth probing.

3. **GitHub plan limitations are not code gaps.** Enforcement via branch protection is a real prevention mechanism, but it's outside the engineering backlog — it's a business decision (Pro tier cost vs risk tolerance). Document this distinction in the closed note so future readers don't re-file the same row.

## Next session

- Optional: evaluate husky pre-push hook infrastructure (3-4h) if enforcement via Pro tier not approved by business.
