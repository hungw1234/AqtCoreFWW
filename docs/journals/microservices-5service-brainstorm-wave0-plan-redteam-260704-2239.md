# Journal — Microservices 5-service brainstorm + Wave 0 plan + red-team

**2026-07-04 (PM)** · Session type: brainstorm → plan → red-team (advisory, **0 code, 0 commit**; plans/ gitignored).

## Session goal

Tiếp brainstorm chuyển đổi toàn diện sang microservices (deep-dive tầng chưa đụng report `1900`), chốt target, rồi plan + red-team Wave 0.

## What worked (verified)

- **Brainstorm deep-dive → target LOCK @5 coarse service** (từ 11 report cũ). 1 team + on-prem + mesh/mТLS bắt buộc = distributed-tax cao nhất → coarse-graining load-bearing. Map 15 module → S1-S5; đường cắt nuốt Workflow-write saga vào in-process (0 distributed 2PC). Reports: `plans/reports/brainstorm-260704-1918-microservices-5service-operational-deep-dive-report.md` (+`.html`).
- **Scout-first thắng report claims** (INVARIANT 260522-A): probe thật phát hiện Outbox hạ tầng ĐÃ CÓ, Contracts đã 11 csproj riêng → thu hẹp Wave 0 mạnh trước khi plan.
- **Red-team 4 hostile reviewer → 15 findings (6C/6H/3M) all-accept** → re-scope plan thành ship-now core + defer track. Whole-plan consistency sweep: 0 contradiction.

## What didn't / lessons

- **Plan gốc (tôi viết) có lỗi thực tế — red-team bắt hết:**
  - Outbox: `IOutboxStore.Enqueue` KHÔNG tồn tại; event plain record không `IExternalDomainEvent` → interceptor không capture; outbox lưu **AssemblyQualifiedName** → versioning độc lập làm **mất event** silently. Handler no-UoW → atomicity sai. → P2 thực chất là event-model refactor (M→L), không phải routing.
  - **OrgUnit KHÔNG internalized** — Workflow(S3) consume `IOrgUnitModuleClient` nặng → S3→S1 cross-service (brainstorm 1918 §1.2 SAI — cần đính chính report).
  - Client inventory đúng = `I\w+Client` (18), không `*ModuleClient` (8) → bỏ hết S4 signing seams.
  - Cross-schema "~20" là stale report-1900; `CrossSchemaSqlScanner` whitelist active = ~6 seeder + 2 D7-query, đã document YAGNI-legitimate. `SeedOrphanPurger` DELETE không route qua client (mất data).
- **Lesson:** viết plan trên report-claim mà chưa probe từng identifier = lặp lỗi backlog-stale. Scout đã bắt phần lớn, nhưng P4 (OrgUnit/inventory) + P3 (count) vẫn lọt tới red-team. Grep consumer thật TRƯỚC khi phân loại seam internalized/cross-service.
- **Lesson (test):** Outbox at-least-once test KHÔNG được dùng InMemory harness — `WebAppFixture:95` strip `OutboxProcessor` → false-green ([[false-green-pattern-3rd-occurrence]]). Cần real-broker Testcontainer HOẶC white-box processor-drive.
- **Consistency:** project-state.md ghi 1 red-team CŨ (04-21) cũng tìm "OutboxProcessor stub + OutboxMessage missing TenantId" — cùng lớp vấn đề, xác nhận Outbox tenant/versioning là điểm yếu dai dẳng.

## Decisions made

- **Target = 5-service, S4 "6-ready"** (Reporting internal split-seam). Mesh=Linkerd trừ distro=OpenShift→Istio. Data-residency hybrid (S1+S2 physical, S3/S4/S5 logical DB). Monorepo N-deployable.
- **Wave 0 re-scoped:** SHIP-NOW (P2 Outbox-done-right ưu tiên #1, P1 packaging-metadata-only, P3 request-path-only giữ whitelist, P6-egress narrow) vs DEFER (P4/P5/P6-umbrella/P7 — blocked-on extraction schedule + GOV distro/feed/key).
- **Honesty:** Wave 0 thực chất = 1 bug-fix thật (P2) + vài seam rẻ, không phải 7-phase infra build. Red-team cứu khỏi churn xây scaffold cho microservices chưa có lịch trình.

## Test status

N/A — session advisory, không chạy test, không sửa code.

## Next session

1. **Confirm GOV open Q #5 — extraction có lịch trình chưa?** Nếu chưa → defer track đóng thành 1 backlog row, không giữ 4 phase pending.
2. `/ck-cook` **P2 Outbox-done-right** (bug thật, độc lập, không cần GOV) khi sẵn sàng ship.
3. Ship-now carve-out an ninh rẻ: enforce CI gates + docker-build event-guard (P5) — độc lập microservices.
4. Đính chính brainstorm 1918 §1.2 "OrgUnit internalized" (thực tế cross-service S3→S1).

Plan: `plans/260704-2205-microservices-wave0-foundation/` (gitignored, local).
