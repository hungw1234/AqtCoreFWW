# Forms Cascade H2 Phase-05 — Reject Authed-External DataSource on Public Forms

**Date**: 2026-08-06 16:06
**Severity**: High (security guard — C1×H2 intersection)
**Component**: Forms / Domain publish-time validation
**Status**: Shipped (`4e0bf9f68`); Phase-05 of `FORMS-CASCADE-PUBLIC-EXTERNAL-INROW`. H2 slice (Phase 04+05) complete.

## Session goal

Close the C1×H2 intersection left open by Phase-04: the Phase-04 Vault-authed external-fetch config (`AuthMode`/`VaultKeyId`) had a producer (the resolver) but NO validator. A public form (reachable through the anonymous options endpoint) carrying an authed-external DS would read a Vault API key and issue an authenticated outbound fetch on behalf of an unauthenticated caller. Phase-05 rejects that config at publish/republish.

## What worked

- Guard `CascadeDependencyValidator.EnsureNoAuthedExternalOnPublic(activeFields, isPublic)` wired at `FormTemplate.Publish` + `Republish`, right after the existing cascade-graph `Validate(...)`. Error code GOV_FRM_044. No-op on private forms (`!isPublic` short-circuit).
- Guard iterates ALL active fields, not top-level-only like `Validate` — the anonymous endpoint resolves a field by FieldKey with NO top-level filter, so an in-row (child) field's authed-external DS is also anonymously reachable and must be guarded.
- Tests green: Forms.Domain 192/192 (7 new), Publish/Republish handlers 33/33, Infrastructure builds 0-warn.
- Commits: `4e0bf9f68` (feat, 3 files) + `bbd5fc659` (plan docs). Journal for the Phase-04 fetch path was committed earlier this session (`2d62bdf37`).

## What didn't / lessons

**Parse-parity self-verification failed — recurred a known lesson.** Initial impl parsed `AuthMode` with a hand-rolled `JsonDocument.TryGetProperty` walk (FIRST-property-wins). The runtime resolver uses `JsonSerializer.Deserialize` (LAST-property-wins). I "verified" the two agreed by writing a manual parity TABLE (case-variants, empty, whitespace) — all matched, looked safe. I never ran both parsers on adversarial input.

Code-reviewer's empirical probe found the divergence: a duplicate-key craft `{"AuthMode":"","AuthMode":"Bearer"}` → guard reads first (`""` = not authed → allows publish) but resolver binds last (`"Bearer"` = authed → reads Vault key → anonymous authed fetch). That is exactly the breach GOV_FRM_044 exists to prevent, and my manual table was blind to it because I never enumerated the duplicate-key row.

This is instance #2 of `feedback-observation-matching-hypothesis-is-not-verification` (instance #1 = OIDC URI-normalize, 2026-07-28). The specific rule crystallized: **parse-parity across a Domain/Infrastructure boundary MUST reuse the same deserializer, never a hand-rolled re-parse with different tolerance.** A 2-line probe running both parsers answers definitively; a manual table does not.

## Decisions made

- **Fix = STJ mirror record, not JsonDocument.** Domain cannot reference the Infrastructure config record (dependency direction), so the guard re-parses — but now via a local `record ExternalAuthProbe(string? AuthMode)` deserialized with the SAME `JsonSerializerOptions` (`PropertyNameCaseInsensitive=true`) the resolver uses. Last-wins parity is now byte-identical; the whole class of tolerance mismatches is structurally eliminated.
- **Fail-CLOSED on unparseable AuthMode.** A number/bool-typed `AuthMode` throws in STJ (both guard and resolver). The guard now returns true (REJECT publish) on any `JsonException`. This also closes review finding H1: previously such a config would publish, then throw an unhandled `JsonException` out of the anonymous endpoint = 500 DoS. Now it can't publish. "Authed-or-unparseable external DS on a public form" is an unrepresentable state.
- **Guard scope = all active fields (intentional, test-locked).** Reviewer flagged M1: the all-fields scope (vs `Validate`'s top-level filter) is load-bearing, not an oversight. Added a child-field test to lock it against a future "optimize to match Validate" regression.

## Test status

- Forms.Domain: 192/192 (was 189; +3 net-new adversarial: duplicate-key→GOV_FRM_044, number-typed→GOV_FRM_044 fail-closed, child-field→GOV_FRM_044). The other 4 Phase-05 tests (public+authed, private+authed OK, public+static OK, republish public+authed) were in the earlier 189 count.
- Publish/Republish command handlers: 33/33.
- Infrastructure: builds 0-warn/0-err.

## Next session

- **M1 (Phase 06-07)** remains — the last open slice of this plan. Phase-06 BE (~1.5d): extend `CascadeDependencyValidator` for in-row child-field cascade + `FormValidationService` submit-time option-membership guard on BOTH authed and public submit paths. Phase-07 FE (~1.25d): per-row cascade wiring in TableFieldWidget. Independent of C1/H2, TDD-able.
- Branch is 8 commits ahead of origin/main; per v1 pattern do not merge until M1 lands. Already pushed bitbucket + origin (NOT oracle).
- Advisory (non-blocking): if a future import pipeline round-trips DataSourceJson through a serializer, the duplicate-key vector narrows to direct-API only. No action needed now — no such path exists.
