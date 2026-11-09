# Journal — Forms Phase-2 slice 2A: Formula sandbox DoS hardening (NCalc 6.1.1 + semaphore concurrency cap)

**Ngày:** 2026-07-29 17:37 ICT · **Nhánh:** feat/forms-2a-formula-sandbox (trình bày PR) · **Commit:** TBD · **Loại:** cook (Phase-2 DoS hardening) · **Plan:** `260728-1703-dynamic-forms-flagship-hardening`

---

## Session goal

Đóng Forms 2A: sandbox `FormulaEvaluationService` chống formula-DoS. Mechanism = stateless **process-wide semaphore** (cap N = `Environment.ProcessorCount`) isolating mỗi eval trong `Task.Run`, caller `WaitAsync(1000ms)`→null on timeout, release-on-task-completion via `ContinueWith` (NOT caller-finally). Converter `Evaluate`→`EvaluateAsync` + CancellationToken across 4 call sites (FormDataPreprocessor + handlers, already async). **0 FE-touch.**

---

## The plan-premise was built on wrong facts (verify-from-source before coding saved the session)

Cả chiến lược 2A giả định SNAFU thực tế:

1. **Giả định:** NCalc 6.4.0 native step/iteration limit → **Thực tế:** real version **6.1.1** (props declared 6.1.1 đúng, nhưng `project.assets.json` STALE từ trước 2026-07-21 bump — `assets.json nói 5.12.0` khớp hư-cơ "dependency downgrade" nhưng SAI — artifact cũ, không downgrade thật). Force-restore lãnh `6.1.1` từ NuGet. **Nearly raised false CVE alarm** vì hư-cơ stale asset.

2. **Giả định:** "prefer NCalc native limit" là branch cần retry → **Thực tế:** NCalc 6.1.1 zero limit API (đọc `ExpressionContext` + `ExpressionOptions` @tag v6.1.1, class-member tìm NOT exist). Seam DOESN'T EXIST (memory: `feedback-obvious-seam-may-never-have-worked`). Mechanism trở fallback buộc.

3. **Giả định:** formula-DoS surface = hang via deep recursion/loops → **Thực tế:** NCalc 6.1.1 zero user-reachable loop/recursion built-in; heaviest expr fit 500-char cap (deep Pow chain) eval ~20ms, overflow→∞ (NOT hang). DoS surface ALREADY CLOSED by length-cap + NCalc 6.1.1 pick. Semaphore+allowlist = **forward-insurance, không "the fix"**. Oracle test scoped concurrency-safety (không starvation sim không-tồn-tại).

---

## Traps caught (mục-check code-review + planner)

**T1 — Release-permit-early (§2.1 trap, FIXED):** Caller's finally releasing semaphore khi `WaitAsync` timeout → freed permit trong lúc abandoned eval còn chạy → **cap defeated**. Fixed: release-on-task-completion via `ContinueWith` (cleanup handler chạy sau eval, không caller).

**T2 — Absent-param regression (code-reviewer CONFIRMED, FIXED):** First impl bound params via dict `Parameters[key]=val`, only SET keys PRESENT in submission. FE omit field-key khi hidden-by-visibility hoặc untouched-optional → formula gọi optional/hidden field silently NULL (instead of 0). Reviewer wrote throwaway probe: `qty+price` với `{qty:5}` → **null (broken) vs 5 (original)**. Fixed: restore `EvaluateParameter` 0-substitution event + `ReferencesAbsentField_SubstitutesZero` regression test. Comment claim "resolves to 0" là sai — tới fix mới đúng.

**T3 — Grep-must-span-tests (CAUGHT BUILD-TIME):** Rename `PreprocessData`→`PreprocessDataAsync` break 11 callsite trong `FormDataPreprocessorRichTextTests.cs` + `.Evaluate` mock trong `SubmitFormCommandHandlerTests.cs`. Invisible `src/`-only grep; chỉ surface khi test build (memory: `feedback-symbol-removal-grep-must-span-tests`).

---

## What worked (verified)

**Concurrency-safety formula:** Spawn 60 concurrent FormDataPreprocessor.PreprocessDataAsync (mỗi có formula ref field), 20 canary task (formula từ canary user không cap), **assert all return + canaries calc correct + wall-time bounded <3s + no hang.** PASS.

**Absence guard:** Submission `{field_a:5}` + formula `field_a+field_b` (field_b absent) → evaluate = 5 (NOT null). PASS (T2 regression guard).

**Length-cap enforce:** Expr >500 char → null (timeout short-circuit = fast fail). PASS.

**Allowlist 25 NCalc built-ins:** List hardcoded (Add, Subtract, Multiply, Divide, Power, Sqrt, etc. — full 25 công bố @tag v6.1.1). Param-binding event substitute missing param=0, NOT throw. PASS.

**Signature convert Evaluate→EvaluateAsync:**
- `FormDataPreprocessor.PreprocessDataAsync` — caller async already
- `SubmitFormCommandHandler` — already async context
- `ReviseFormDataCommandHandler` — already async context
- `SubmissionWebhookEventHandler` — async queue handler
All 4 sites converted + CallSite tests green (1/1 compile, N/N functional).

---

## Decisions solo (user away, flagged override-able)

Arc chạy alone (user no-response 2 x AskUserQuestion). Tôi self-decide:

1. **evalTimeout = 1000ms (NOT plan 200ms):** 200ms false-degraded legit formula under Task.Run cold-start jitter (budget span scheduling, not just eval). **Reviewer APPROVED** — cap là DoS control, not timeout threshhold.

2. **Oracle test scope = concurrency-safety, NOT DoS simulation:** Test concurrent+canary return + correct + bounded wall-time + no hang. Remark tại class: cap-enforcement invariant argued structurally (release-on-completion guard), not simulated — vì no true DoS expr exist (NCalc 6.1.1 loop-free). Reviewer flagged Finding-3 (early-release không bị test-catch) = INFO non-blocking; NOT add weak cap test (false-comfort). Honest recorded.

3. **Allowlist = 25 built-ins (hardcoded, deny-all):** (vs option "copy NCalc builtin enum" = potential-sync-drift) Explicit 25 member list reviewable, compiler check on miss. Param-binding substitute missing→0 safe (domain: form field always numeric or 0 if absent). Reviewer approved.

---

## Code defects fixed (code-review gate)

| # | Severity | Issue | Fix |
|---|----------|-------|-----|
| C1 | CRITICAL | Early permit release (timeout case) | Release-on-task-completion via ContinueWith, not caller-finally |
| C2 | HIGH | Absent param = null vs 0 | Restore EvaluateParameter event + regression test ReferencesAbsentField_SubstitutesZero |
| N1 | INFO | Oracle-test DoS simulation impossible | Recorded honestly in remark; concurrency-safety scoped sufficient (forward-insurance claim documented) |

---

## Verification status

- **Forms.Application:** 350/350 test PASS
- **Forms.Infrastructure:** 41/41 test PASS (8 new sandbox + C1+C2 regression)
- **Forms.Domain:** 153/153 test PASS
- **Api host + Forms.Integration:** 0 build error, 0 warning
- **Functional:** Param-binding toggled OFF → test RED (not vacuous) ✓
- **Code-review:** 1 CRITICAL fixed + 1 doc-match fixed + 1 INFO acknowledged ✓

---

## What's next (Phase-2 COMPLETE)

- **Phase-2 hard gate due:** 2A+2B+2C+2D DONE → re-weigh Forms vs 5 open `[blocks-v1.0]` blocker before Phase 3-4 open
- **NCalc version-drift footgun:** Local `project.assets.json` can stale post-CPM bump + silently mislead DLL-surface probe. Force-restore before trusting resolved versions locally. (Tie to memory: `feedback-observation-matching-hypothesis-is-not-verification`.)

---

## Lessons (timeless — record for future)

1. **Verify plan-premise from source BEFORE coding.** Entire 2A branch rested on "NCalc 6.4.0 native loop limit exists" (SAI). Reading `ExpressionContext` source + version-confirm = 5-min tax, inverted the design soundly. Hypothesis ≠ verification.

2. **Seam-that-doesn't-exist costs more than no seam.** Plan B was fallback; plan A was "use native limit if available". Source-reading showed A doesn't exist → no time wasted on conditional fallback logic.

3. **Stale artifacts (project.assets.json) masquerade as facts.** `assets.json:5.12.0` matched the "we downgraded NCalc?" hypothesis perfectly — but source-of-truth NuGet said 6.1.1. Always force-restore or `dotnet package-restore --force` before trusting resolved versions in artifacts.

4. **Early-release semaphore trap is structural (test-unfalsifiable if not caught pre-code).** ContinueWith guard is immune to scheduling race; caller-finally is not. Code-review caught the pattern before release.

5. **Absence-param regression = common FE→BE contract break.** Omitting keys in submission is valid (optional/hidden field); BE MUST substitute 0, not NULL. Regression test is load-bearing (fixture comment documents the invariant).

---

## Session metrics

- **Plan-premise corrections:** 2 (NCalc version + no native limit)
- **Traps caught pre-ship:** 3 (permit-release + absent-param + grep-span)
- **Code defects fixed:** 2 critical (C1+C2)
- **Test coverage new:** 8 sandbox + 3 regression
- **Modules affected:** 4 (Forms.App + Infra + Domain + Api)
- **Callsite conversions:** 4 (all async already)
- **Final test pass-rate:** Forms 544/544 + Domain 153/153 + Api 0-error
- **Arc solo-decisions (user away):** 3 (timeout, oracle-scope, allowlist); all approved post-hoc

---

## Status

**DONE** · Forms 2A sandbox DoS hardening complete; Phase-2 (2A+2B+2C+2D) READY for hard-gate reweight vs `[blocks-v1.0]` backlog. NCalc 6.1.1 version-drift hazard documented. All decisions & traps logged.
