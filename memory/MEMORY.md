# Memory Index — AqtCoreFW

| File | Type | Description |
|------|------|-------------|
| [project-state.md](project-state.md) | project | **v2.133p13 HOT-FIX on `main` (SQL Server, 2026-04-29 ~14:40 ICT)** — Integration migration drift recovery. v2.133p12 shipped ClaimedAtUtc column across 14 modules, but Integration's 2 migration files left untracked (glob path mismatch). v2.133p13 adds missing Integration migrations. No code change. T1 75/75, T2 4/4, T3 16/16. Code-review APPROVE 10/10. Prior **v2.133p12** (OUTBOX-1-DISP atomic claim). Prior **v2.133p11** (OUTBOX-1 unique filtered index). Prior **v2.133p10** (CSQ-6 arch gate + AUD-2 cancel propagation). Prior **v2.133p8** (RBAC-3 + RBAC-5). |
| [feedback-red-team-before-implement.md](feedback-red-team-before-implement.md) | feedback | Red-team detailed phase plans before implementing — catches factual errors early (6-8× ROI) |
| [user-profile.md](user-profile.md) | user | Developer profile and preferences |
| [backlog.md](../backlogs/backlog.md) | reference | v2.121 — T4-05 family shipped → `backlogs/backlog.md` |
| [reference-backlogs-location.md](reference-backlogs-location.md) | reference | Backlogs system: backlog.md + completed.md in backlogs/ dir |
| [feedback-dapper-no-positional-records.md](feedback-dapper-no-positional-records.md) | feedback | Dapper DTOs must use class, not positional record (nullable params fail) |
| [docker-containers.md](docker-containers.md) | project | Existing Docker containers — do not recreate |
| [feedback-docker-check-first.md](feedback-docker-check-first.md) | feedback | Always check existing Docker containers before creating new ones |
| [feedback-subagent-permissions.md](feedback-subagent-permissions.md) | feedback | Background subagents fail on Bash — run tester in foreground |
| [feedback-always-suggest-next.md](feedback-always-suggest-next.md) | feedback | Always propose next steps after completing any task |
| [feedback-sync-docs-after-task.md](feedback-sync-docs-after-task.md) | feedback | Update docs + memory to reflect actual state after every task |
| [feedback-browser-test-required.md](feedback-browser-test-required.md) | feedback | Run Playwright Docker E2E after every task |
| [reference-sql-cli.md](reference-sql-cli.md) | reference | sql-cli installed — use for database queries/debugging |
| [local-sqlserver-2022.md](local-sqlserver-2022.md) | project | **SQL Server 2022 = native Windows install (NOT Docker)** — instance MSSQLSERVER22 @ localhost,1433, SQL Auth (sa). Integration tests migration to local SQL **SHIPPED v2.114 (2026-04-18)**. 4 persistent DBs: Main, Messaging, TenantIso, Concurrency. Hybrid fixture (local or Testcontainers fallback). |
| [reference-docker-sqlserver-access.md](reference-docker-sqlserver-access.md) | reference | OBSOLETE — SQL moved to native install; see local-sqlserver-2022.md. Pipe pattern still kept for fallback container use. |
| [feedback-kanban-yaml-frontmatter.md](feedback-kanban-yaml-frontmatter.md) | feedback | Plan.md YAML frontmatter must use simple status values |
| [feedback-python-command.md](feedback-python-command.md) | feedback | Use 'python' not 'python3' — MS Store alias intercepts python3 |
| [feedback-tests-must-be-written-with-code.md](feedback-tests-must-be-written-with-code.md) | feedback | Implementation agents MUST write tests alongside code |
| [feedback-test-projects-must-be-in-solution.md](feedback-test-projects-must-be-in-solution.md) | feedback | New test projects MUST be added to AqtCoreFW.slnx |
| [feedback-enum-values-must-match-source.md](feedback-enum-values-must-match-source.md) | feedback | Tests must use actual enum values from source |
| [feedback-correct-agent-for-task.md](feedback-correct-agent-for-task.md) | feedback | Use correct agent type for task |
| [phase2-unit-tests.md](phase2-unit-tests.md) | project | Phase 2 test structure: 22 files, DDD+CQRS patterns |
| [feedback-hybrid-test-strategy.md](feedback-hybrid-test-strategy.md) | feedback | Hybrid test scope — module-only during dev, ALL tests at finalize |
| [feedback-ef-concurrency-patterns.md](feedback-ef-concurrency-patterns.md) | feedback | EF Core pitfalls: RowVersion, history tracking, raw SQL |
| [feedback-always-vietnamese.md](feedback-always-vietnamese.md) | feedback | Luôn trả lời bằng tiếng Việt cho dễ hiểu |
| [feedback-debug-timeout-check-config-first.md](feedback-debug-timeout-check-config-first.md) | feedback | Debug timeout: kiểm tra config port/URL trước, đừng đổ lỗi hạ tầng |
| [feedback-fe-dto-must-match-be-controller.md](feedback-fe-dto-must-match-be-controller.md) | feedback | FE DTO phải đọc từ BE controller thực tế — không đoán tên trường |
| [feedback-yarp-reserved-policy-names.md](feedback-yarp-reserved-policy-names.md) | feedback | YARP reserv tên "default" — dùng "gateway" thay thế |
| [feedback-ef-migration-needs-designer-snapshot.md](feedback-ef-migration-needs-designer-snapshot.md) | feedback | EF migration thủ công cần Designer + Snapshot, thiếu sẽ bị bỏ qua |
| [feedback-alerting-table-must-exist.md](feedback-alerting-table-must-exist.md) | feedback | alerting.AlertRules phải tồn tại — Hangfire cascade nếu thiếu |
| [feedback-all-modules-must-work-e2e.md](feedback-all-modules-must-work-e2e.md) | feedback | Mọi module phải hoạt động khi test thủ công — không lỗi 500 |
| [feedback-debug-root-cause-no-guessing.md](feedback-debug-root-cause-no-guessing.md) | feedback | Debug PHẢI tìm root cause, KHÔNG phỏng đoán. Fix PHẢI báo ảnh hưởng |
| [feedback-use-mcp-chrome-not-preview.md](feedback-use-mcp-chrome-not-preview.md) | feedback | Khi services đã chạy, dùng MCP Chrome — không dùng preview tools |
| [project-forms-fe-gap-plan.md](project-forms-fe-gap-plan.md) | project | Forms FE gap closure plan — 5 phases, 12 gaps, ~10.5d |
| [test-session-m2-m7.md](test-session-m2-m7.md) | project | M2-M7 deferred fixes verified: 244 BE tests pass |
| [architecture-cross-module-data-strategy.md](architecture-cross-module-data-strategy.md) | project | 4-layer hybrid strategy for cross-module data |
| [feedback-scalar-not-swagger.md](feedback-scalar-not-swagger.md) | feedback | API docs use Scalar, not Swagger — always reference correctly |
| [feedback-dependency-map-must-update.md](feedback-dependency-map-must-update.md) | feedback | Read + update docs/dependency-map.md when modifying modules/entities/endpoints |
| [feedback-finalize-checklist-must-complete.md](feedback-finalize-checklist-must-complete.md) | feedback | Cook finalize: all 5 steps mandatory — especially dependency-map + journal |
| [feedback-hangfire-bypass-tenant-filter.md](feedback-hangfire-bypass-tenant-filter.md) | feedback | Hangfire workers must IgnoreQueryFilters + validate TenantId manually |
| [feedback-testcontainer-cold-start.md](feedback-testcontainer-cold-start.md) | feedback | Integration timeout often infra — check `docker ps` + retry warm before BLOCKED |
| [feedback-vitest-v4-antd-form-rejection.md](feedback-vitest-v4-antd-form-rejection.md) | feedback | vitest v4 flags antd Form validateFields rejection — wrap in try/catch |
| [feedback-webappfixture-removes-hostedservices.md](feedback-webappfixture-removes-hostedservices.md) | feedback | WebAppFixture.ConfigureTestServices removes IHostedService — integration tests must implement IAsyncLifetime.InitializeAsync to manually invoke seeders |
| [feedback-docs-manager-all-tracked-files.md](feedback-docs-manager-all-tracked-files.md) | feedback | docs-manager phải update 3-file minimum (roadmap + changelog + dependency-map) khi phase status change |
| [c5-finalize-blockers.md](c5-finalize-blockers.md) | project | C5 Finalize BLOCKED: integration test timeouts (Track 2b 6/15 pass) + jsdom/Antd Modal (Track 3 9/150 fail) |
| [feedback-verify-subagent-reports.md](feedback-verify-subagent-reports.md) | feedback | Trust but verify: sau subagent call mutate, MUST Grep/Read sample để confirm change thực sự — subagent reports mô tả ý định, không phải kết quả |
| [feedback-journal-before-git-commit.md](feedback-journal-before-git-commit.md) | feedback | Cook finalize order: journal-writer MUST run TRƯỚC git-manager, else journal untracked + split audit trail |
| [feedback-polish-modules-before-docker-ci.md](feedback-polish-modules-before-docker-ci.md) | feedback | Docker CI (T3-01) chờ sau khi modules polished — infra không ưu tiên trước feature completeness |
| [multi-tenant-deployment.md](multi-tenant-deployment.md) | project | Prod deployed multi-tenant — cross-tenant isolation P0 security critical cho mọi feature |
| [feedback-auto-scout-no-ask.md](feedback-auto-scout-no-ask.md) | feedback | Auto-scout để verify facts (namespace, signature, DI, callers) — không hỏi permission. Scout = free action |
