# Journal — BP MVP P6-FU2 + P6-FU3 SHIPPED

**Date:** 2026-05-16 ~15:00 ICT
**Version:** v2.137p28b
**Ships of day:** #7 + #8
**Commits:** see below

---

## Scope

Close the JsonElement-vs-CLR asymmetry between Json-mode and Table-mode form
submission storage that was knowingly left open in BP MVP P6.

- **P6-FU2** — boundary normalizer (`JsonValueNormalizer`) + applied at
  `IFormSubmissionRunClient.GetFullSubmissionAsync` Json branch.
- **P6-FU3** — integration round-trip test in `Forms.Integration.Tests` using
  existing `WebAppFixture` + Testcontainers SQL fixture.

Both shipped in one batch because FU3's value depends on FU2 being live (it
asserts the asymmetry is closed; without FU2 it would document an existing
bug, not verify a fix).

---

## D9 explicitly invalidated

BP MVP P6 plan locked D9 as "no coercion, pass-through CLR objects" — the
prefill resolver passes whatever it gets from `GetFullSubmissionAsync` through
unchanged. That decision presumed both Json and Table backends returned native
CLR types.

Reality: `JsonSerializer.Deserialize<Dictionary<string, object?>>` boxes every
value as `JsonElement`. The Json branch was leaking storage-backend internals
to BP consumers.

D9 is now superseded. xmldoc trail captures the inversion at both ends:
- `JsonValueNormalizer.cs` — "Explicitly invalidates D9 ('no coercion, pass-through CLR')..."
- `InProcessFormPrefillContextClient.cs` — "GetFullSubmissionAsync normalizes JsonElement → native CLR at the boundary, so this resolver sees uniform types across both backends (supersedes D9 from P6 plan)."

---

## Number coercion ladder (N-1 from review)

Initial draft used `long → double`. Reviewer flagged divergence from same-
module write-path `FormDataPreprocessor.JsonElementToObject:174` which uses
`long → decimal`. For a financial-data framework the read-back must be
lossless against the write, so flipped to `long → decimal → double`.

Decimal covers ~28-29 significant digits (any realistic JSON Number from a
form submission). Double fallback handles the exotic out-of-decimal-range
case (`|x| > ~7.9e28`) without throwing — important because the resolver
contract is partial-fail tolerant (D4 from P6).

L-2 fact added inline: `Normalize_NumberOutOfInt64Range_ReturnsDecimal`
covers `long.MaxValue + 1` falling through to the decimal arm.

---

## Switch-expression trap

First impl had a silent bug:
```csharp
JsonValueKind.Number => element.TryGetInt64(out var i) ? i : element.GetDouble(),
```

Test `Normalize_IntegerElement_ReturnsLong` failed with "Expected `Int64`,
but found `Double`." The ternary unifies `long` and `double` to `double` (the
wider numeric type), silently boxing the long-branch as double. Fix: `(object)i`
cast forces both arms to be `object?`. Later refactored into a private
`NormalizeNumber` helper since the ladder grew to 3 levels.

Filed mentally as a C# generic-method pitfall: any switch arm where two
numeric types unify needs an explicit `(object)` cast on the narrower one,
or a helper method.

---

## "4-mode storage matrix" was over-scoped framing

Memory carried "P6-FU3 (Low, deferred): 4-mode storage matrix integration
test (Json/Table × source/target)". On scoping FU3, realized:

1. Target template's `StorageMode` is irrelevant to prefill resolution.
   Resolver only reads the source submission's data + the target's schema
   snapshot (which is mode-independent metadata).
2. The real axis is source backend = 2 modes.
3. Table-mode already returns native CLR via Dapper — the asymmetry is
   one-sided.
4. So 1 Json-mode round-trip fact suffices to verify FU2 closed it;
   Table-mode regression is implicitly covered by existing
   `FormEndpointsTests.FormLifecycle_CreatePublishSubmitQuery_Succeeds`.

Documented this clarification both in `PrefillJsonElementNormalizationTests`
xmldoc (durable, lives next to the test) and in the changelog (durable,
lives next to the version). Future-self reading "4-mode" in memory should
find the correction here.

**Lesson:** when memory describes a deferred test as "matrix × N modes",
revalidate the modes before opening the work. The carrying language can
embed analysis that no longer holds.

---

## Ship-band band note

Ship #7+#8 of the day. Memory L3 5+1 override gate explicitly satisfied:

| Condition | Status |
|-----------|--------|
| Additive-only scope | ✅ — new helper + 1 line edit + new tests |
| Regression green | ✅ — Forms.App 177, Integration FU3 PASS, Arch 97 |
| User explicit | ✅ — option C "Cook cả FU2 + FU3" |
| Reviewer ≥ 9.0 | ✅ — 9.0/10 APPROVE_WITH_NITS |

Per memory L1 (5+1 always reviewer-gated): `code-reviewer` agent ran scoped
review pre-commit; N-1 + L-1 + L-2 folded inline before ship. Per L2
(`--auto` orthogonal to fatigue): `--auto` flag did not bypass the gate —
reviewer-spawn was a hard requirement regardless of mode.

After #8 cadence is at 4.5 ships/hour averaged across day. Memory should
codify that ship #9+ this session moves into reviewer-vetoed territory
unless scope is sub-30-LOC and orthogonal to today's BP work.

---

## Validation final

| Suite | Before | After | Δ |
|-------|--------|-------|---|
| Forms.Application | 165 | **177** | +12 normalizer facts |
| Forms.Integration | (existing) | +1 | FU3 round-trip |
| BusinessProcess.Application | 59 | 59 | regression |
| Forms.Infrastructure | 28 | 28 | regression |
| Architecture | 97 | 97 | module boundary intact |
| Build | 0E/0W | 0E/0W | — |

Code review: 9.0/10 APPROVE_WITH_NITS. N-1/L-1/L-2 folded inline before commit.

---

## Carry-overs

- **P6-FU2 L-4** (Trivial): 1-line comment on the unreachable `_ =>` arm in
  `NormalizeElement` explaining defensive intent (all `JsonValueKind` cases
  covered; arm exists for forward-compat if future runtime adds a kind).
- **P6-FU3 L-3** (Low, blocked-on-P7/P8): nested-object/array integration
  fact. Defer until P7/P8 FE confirms whether nested prefill is in MVP
  scope. If FE only consumes flat scalars, this is permanent YAGNI.
- Earlier P6-FU1 L-1/L-2/L-5 + P4-2C-FU1/FU2/FU3 + P4-2A-Write-FU1 +
  P3-FU1 + P2-FU1/FU2 unchanged.

---

## Next session

BP MVP backend is now complete for MVP scope through P6 + all P6 follow-ups.
P7 (Admin UI BP designer, 3d FE) + P8 (User UI BP runtime, 2.5d FE) are
ready in parallel. Recommend fresh window for either — context-cold start
on a fully orthogonal stack is the right move after 8 ships.

---

## Unresolved questions

- P6-FU3 L-3 deferral hinges on FE MVP scope decision (flat vs nested
  prefill) — answer needed before P7/P8 starts.
- Number coercion `long → decimal → double` ladder vs `FormDataPreprocessor`'s
  `long → decimal` (which throws on out-of-decimal-range). Should
  `FormDataPreprocessor` mirror our `double` fallback to be symmetric?
  Filed mentally; not blocking.
