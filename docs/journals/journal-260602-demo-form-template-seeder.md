# Journal — 2026-06-02 · DemoFormTemplateSeeder (P5)

**Commits:** `83f93ede` feat + `84f08d60` docs (v2.137p138), pushed origin + bitbucket, ahead=0.

## What
NEW `DemoFormTemplateSeeder` (Forms.Infrastructure/Seeding) — seeds one published intake FormTemplate per `TenantSeedProfiles` entry (BTC + BNV). 7 fields across 7 distinct types: `ho_ten` (Text), `noi_dung` (Textarea), `so_tien` (Number), `ngay_de_nghi` (Date), `muc_do_uu_tien` (EnumRef dropdown), `khan_cap` (Boolean), `tai_lieu` (File). Vietnamese labels. EnumRef dropdown carries 3 static FormFieldOption (Thấp/Trung bình/Cao). StorageMode.Json (Publish→Active immediately). Gated `Forms:DemoFormTemplateSeed` (OFF default). Idempotent per `(TenantId, Code="demo-intake-form")`. 2-phase save.

Mirrors P4 DemoWorkflowSeeder pattern faithfully (same seeder convention, same SystemSeederTenantContext, same RLS bypass via db_owner).

## Key design decisions (audit-at-slice-start caught the unproven write path)
- **EnumRef is the canonical option-bearing type** — plan-doc assumed "Select" FormFieldType existed. Audit probe: enum has NO `Select` member. Canonical type = EnumRef + static FormFieldOption. `GetFormFieldOptionsQueryHandler:39` serves static options ahead of DataSourceJson for ANY type. Explicit mapping at `FormFieldConfiguration:69` (HasMany(Options).WithOne().HasForeignKey) — cascade-insert proven from principal side, not convention-assumed.
- **2-phase save (not 3)** — initial plan: (1) template, (2) fields, (3) options (separate SaveChanges). Scout `AddFormFieldCommandHandler:58-60` proved `field.Id=0` at AddOption time → EF FK-fixup on cascade save. Simplified to: (1) template+fields+options one SaveChanges, (2) PublishAndSnapshot+version. KISS reduced complexity.
- **RLS risk resolved (NOT new)** — forms FormTemplates has RLS; `SystemSeederTenantContext.TenantId=null` → `TenantSessionContextInterceptor` SKIPS sp_set_session_context → RLS bypass only via IS_MEMBER('db_owner')=1. Same db_owner path proven P2.3c runtime (reset-all BTC 10k+BNV 1498) + P4. Reviewer strengthened: non-db_owner run = fail-loud (BLOCK predicate raises, host won't start), never silent cross-tenant corruption.
- **StorageMode.Json chosen** — Table mode triggers background DDL Materializing (out of scope for definitions-only seed).

## Verification
Build `src/AqtCoreFW.slnx` 0W/0E · `DemoFormTemplateSeederTests` 4/4 real-SQL (TestContainers): T1 published + 7-types + diacritic + EnumRef present (PROVES cascade-insert path), T2 EnumRef 3 static options diacritic roundtrip, T3 idempotent no-dup template/version, T4 version snapshot captures all 7 active fields. Code-review APPROVE-WITH-NITS (0 Crit/High/Med). Edits: `FormsInfrastructureModule.cs` (gated AddHostedService) + `appsettings.Development.json` (+Forms block) + `reset-all.ps1` (env override + marker + $pending).

## Lessons (META)
1. **Probe the one unproven write path, not all ten verified ones** — advisor's value was pinpointing the single assumption (options cascade-insert) among ~10 primary-source-verified facts. That's where extra probe spends.
2. **Mechanism-equivalence is the proof** — audit asks "does prior slice prove this path?" YES, via shared db_owner-bypass RLS mechanism common to forms+workflow schemas. Equivalence across modules is stronger than "looks similar."
3. **Build is the oracle on enum assumptions** — CS0117 'Select' not in FormFieldType surfaced the plan-doc's stale assumption before any test ran. Read source-of-truth (the enum), not the plan.
4. **KISS: 3→2 phase once FK-fixup confirmed** — EF cascade-insert proved via production handler read, not test mock.

## Carry-forward
- **P6 BusinessProcess Defs** now unblocked (needed P4+P5) OR **P7 Niche Defs** (needs only P2) — both cookable next.
- Manual smoke owed: reset-all → intake form in UI → enter 7 fields → submit → confirm stored.
- **Known gap BP-FORM-OPTIONS-SNAPSHOT (deferred)** — version snapshot omits FormFieldOption rows → EnumRef renders empty at BP-run time. NOT a P5 regression — documented so future smoke doesn't read it as one. Blocks P6 BP form binding (awaits cleanup).
