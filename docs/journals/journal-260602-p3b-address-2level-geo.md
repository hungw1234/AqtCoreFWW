# Journal — P3b: 2-level address field (hệ-34 geo migration) (2026-06-02)

## Session goal
Cook P3b — the last gated phase of the reseed plan: migrate the form address field from a 3-level cascade (Tỉnh → Huyện → Phường) to 2-level (Tỉnh → Phường), reading the `AdministrativeUnit` tree. P3b was gated on 5 open questions + a treasury-coupling blocker.

## What happened
- **Scout de-risked the scary parts before planning.** The plan estimated 2-3d. Two probes collapsed it: (1) the re-point endpoint `/masterdata/admin-units?level=&parentCode=` already exists — no new BE route; (2) live orphan-district probe returned 0 submissions — no migration/backfill. Scope shrank to ~0.5d.
- **Resolved the 5 open questions** via the plan's recommended minimal-churn defaults: keep District/Ward + their endpoints **dormant** (mandatory — `OrgTreeSeeder.Treasury.cs:47` reads `masterdata.Wards`), treasury untouched, accept orphan keys, admin-UI tabs untouched.
- **Two material facts emerged AFTER the user said "implement now" — both surfaced, not silently chosen:**
  1. **Province-source fork.** `admin-units?level=1` returns only 3 seeded cities (HN/HCM/ĐN); the 34-province list lives in the separate `Provinces` lookup. The plan criterion said "data from AdministrativeUnit" but didn't resolve province-source. Surfaced A (34 provinces, 31 empty wards) vs B (3 provinces). User chose **B** — matches the plan literal, accepts the 3-province demo limit.
  2. **Validation regression.** `FormValidationService.ValidateAddressField:164` REQUIRED province + district + ward. Dropping district from the widget would make every address submission fail validation. Widget-only was not a viable scope — the BE validation relaxation (province + ward) is mandatory. Surfaced + reconfirmed before coding.

## What worked
- 6-file change (1 BE validation + 5 FE: widget rewrite, 2 tests, en/vi i18n). `FlattenAddress` + `FormTableMigrator` left intact — the widget stops sending district, so the flattener writes a harmless `_district=null` and the Table-mode column is a harmless orphan (consistent with accept-orphan, 0 submissions).
- **Live HTTP verification** (user chose stack-up; obstacle: no seeded form has an AddressField to render the widget). Stood the API up and hit the real endpoints: `admin-units?level=1` → exactly 3 provinces (01/79/48); `?level=2&parentCode=01` → 126 HN wards (diacritics intact), shape `{code,nameVi}` matching the widget. This proved the data path end-to-end through the real API without needing to build a form.
- Verify: BE build slnx 0E/0W · FE tsc 0 · vitest 47/47 (widget renders 2 selects + i18n) · BE SubmitForm 24/24 · review APPROVE-WITH-NITS.
- **Closed the one review nit** by adding `AddressFieldValidationTests` (5/5): province+ward pass, legacy-district pass, missing-province/ward error, null no-error — a regression guard for the relaxed BE contract.

## Decisions made
- **Option B (3 provinces)** over A — user call, matches plan literal; 3-province limit is accepted demo reality (seeding the other 31 provinces' wards is a separate data-sourcing task).
- **Minimal BE churn:** only `FormValidationService` changed; `FlattenAddress`/`FormTableMigrator` left dormant. The dormant District/Ward entities + endpoints stay for treasury.
- **HTTP + unit verification** in place of browser render (no seeded address form) — the live endpoint proves the data, the widget unit test proves the render logic.

## Lessons
- **Material facts can emerge after sign-off — surface, don't silently pick.** The user approved "implement now" against a clean re-point; the province fork + validation regression appeared during implementation scouting. Both contradicted either the plan criterion or the happy-path assumption, so each got a confirm before proceeding. Cheaper than a demo-time surprise.
- **The dangerous part of a "small FE change" was a BE contract.** Dropping a field from a widget silently breaks server-side validation that requires it. Always grep the removed field's consumers (validation, flatten, table-migration) — `FormValidationService` would have rejected every submission.
- **Verify the data path, not just the component, when a browser render is blocked.** No seeded form had an AddressField, so a literal browser cascade was expensive. The live endpoint (real API, real seeded data) + the widget unit test together cover data + render without the form-building detour.
- **`plans/` is gitignored here** — plan sync-back edits persist on disk but are intentionally not committed; don't alarm when a plan file is absent from a commit.

## Next session
- **Reseed plan fully closed** (P2–P7 seeders + txn-wrap parity + WF-DEMO-SEED-SMOKE live + P3b geo migration).
- Open follow-ups: seed the other 31 provinces' ward data (data-sourcing, would lift Option B's 3-province limit); `SEED-ORPHANPURGER-TEST-DATA-STALE`; `BP-FORM-OPTIONS-SNAPSHOT`; `BTC-SKIPPED-TESTS-REACTIVATE`.
- Auth + API were stood up for the endpoint check; API stopped after. DB holds freshly-seeded demo data.
