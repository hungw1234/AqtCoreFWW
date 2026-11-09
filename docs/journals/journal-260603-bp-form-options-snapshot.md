# Journal — Form-version snapshot now carries EnumRef field options (BP-FORM-OPTIONS-SNAPSHOT)

**Date:** 2026-06-03 · **Ship:** v2.137p148 · **Commits:** `7333714c` (fix) + `37332c61` (docs) (pushed origin + bitbucket, ahead=0)

## What shipped
The published `FormTemplateVersion` snapshot serialized field metadata but omitted `FormFieldOption` rows. When BP forms (intake/state/transition) rendered an EnumRef field (dropdown with static choices), they used the snapshot and found no options—Select rendered with an empty choice list. BE `FormTemplateVersionFactory` now includes each field's options as an **additive-nullable** `Options` key on `FieldPayload` (SchemaEnvelopeVersion stays 1, following the PrefillSource precedent). FE adapter maps the options; the renderer already read inline. Dropdowns now render choices.

## Advisor caught the blind spot: verification only on the producer side
I verified the producer thoroughly—repo `GetByPublicIdWithFieldsAsync` eager-loads `.ThenInclude(f=>f.Options)` as intended. But I never verified the consumer. The acceptance criterion ("Select RENDERS choices") lives on the consumer side. The real risk was that the renderer fetches options via a separate query—if so, the fix ships green, renders nothing. Traced one hop: EnumRef reads inline `field.options ?? []`. Confirmed. **Lesson: when a fix spans producer→consumer, verify BOTH ends; the acceptance criterion's side is the one to validate, not the side you're editing.**

## Additive-key safety: proven with grep, not assumed
Grep found zero `UnmappedMemberHandling.Disallow` in `src`—every snapshot reader ignores unknown keys by default. Hand-verified all 3 BP handlers (intake/state/transition execute handlers) return raw `snapshot.SchemaJson` string unchanged (no re-serialize that strips keys). Form submission validation reads LIVE `template.Fields`, not the snapshot—unaffected by the snapshot schema change.

## Backward-compat: additive-nullable + self-heal, no migration
Old snapshots without an `options` key parse to `null` on both BE and FE (verified non-vacuously). Active templates keep optionless snapshots until republished. The shared `Snapshot()` factory self-heals on next publish; no backfill migration (YAGNI). **Dev caveat:** DemoFormTemplateSeeder's already-seeded `muc_do_uu_tien` EnumRef snapshot stays optionless until the next `reset-all` (which regenerates it with options). No prod template currently affected.

## Verification
BE `FormTemplateVersionFactoryTests` 9/9 (+2 cases: scrambled-order → sorted, null-when-empty) · full Forms.Domain.Tests 132/132 · FE `tsc --noEmit` 0E · `form-snapshot-adapter.test` 5/5 (+2 cases: EnumRef inline map, null backward-compat) · full forms suite 201/201 · code-review independently verified state+transition handlers + re-confirmed Radio is not a FormFieldType (EnumRef only static-choice type). No migration / no entity / no public-API change. Dependency-map unchanged (additive serialization key only).

## Carry-forward
- `SEED-ORPHANPURGER-TEST-DATA-STALE` (4 pre-existing red tests, separate row).
- `BP-TRANSITION-FORM-E2E` and `BP-RUN-DOSSIER-LISTS-PHASE-5` remain open.

## Meta
Scout de-risked the producer path (eager-load confirmed, no alternative query path). Advisor pinpointed the consumer-verify gap—highest-value finding (would have shipped green-but-broken). Additive-nullable precedent (PrefillSource) provided the forward-compat template. Grep-for-strict-deserialize proved the additive key safe without tracing every consumer. Naming precision: Radio is not a FormFieldType; EnumRef is the only static-choice type.
