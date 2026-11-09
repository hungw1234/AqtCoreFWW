# Runtime E/W Triage — EF Config Warnings Fix (Fix A + B + B1)

**Date:** 2026-06-11 ~18:45
**Commits:** `ffbe2fac` (Fix A) · `ef6ffa7a` (Fix B) · `67653452` (B1) — all pushed origin + bitbucket, ahead=0
**Flow:** `/ck-debug` (runtime e/w triage) → `/aqt-runapp` → `/ck-debug` (B) → `/fix` (B1) → `/git cm & p`

## Session goal
Kiểm tra errors/warnings khi runtime API + Auth server để đưa phương án xử lý, rồi fix.

## What was done

### Runtime e/w triage (root cause split into 3 independent groups)
- **Build sạch** 0W/0E. Mọi vấn đề thuần runtime.
- **Nhóm A — Vault chưa seed (operational, NOT code):** Vault chạy dev-mode in-memory (`docker-compose.yml:40`, không persistent volume), `vault-init` one-shot (`restart:"no"`) → KV rỗng sau restart. Verified empirical: `secret/aqt/encryption/v1` + 4 email-pepper đều MISSING. Cascade → `[ERR] Failed to fetch encryption key/pepper` → dev-user seed fail (admin/officer/viewer) → app start được nhưng vô dụng (không login); API log phình **6.7MB** vì warning "Email pepper not found" lặp per-user. **Fix = re-seed Vault** qua `/aqt-runapp` Step 1.5 (`docker compose run --rm vault-init`). Sau seed: ERR 8→0 cả Auth+API, log 6.7MB→64K. KHÔNG sửa code.
- **Nhóm B + B1 = EF model warnings (code-level, độc lập Vault).**

### Fix A — IpFilterRule insert-flip (bug bảo mật, commit `ffbe2fac`)
`IpFilterRule.IsEnabled`/`IsBlock` có `HasDefaultValueSql("1")` (DB default `true`) nhưng CLR default `false`. EF sentinel = CLR default → khi insert `false`, EF **bỏ cột** → DB default `1` áp → **disabled rule lưu thành enabled, allow-rule lưu thành block** (đảo ngược ngữ nghĩa bảo mật). Reachable qua `CreateIpRuleCommand`. **Fix migration-free:** `.HasSentinel(true)` → false luôn được gửi tường minh. Regression test RED (`Expected False, found True`) → GREEN.

### Fix B — 5/6 cosmetic sentinel warnings (commit `ef6ffa7a`)
6 cột còn lại có DB default **==** CLR default → **không thể flip** (benign noise). Fix migration-free:
- bool insert-chỉ-false (`SigningDocFlowRecord.IsDeleted`, `MysignInboundToken.IsRevoked`) → `.HasSentinel(true)`
- enum (`SigningDocFlowRecord.Status`, `SigningPendingTransaction.Status`, `TenantSignatureConfig.OcspMode`) → `.HasSentinel((Enum)(-1))` (giá trị không tồn tại → luôn gửi explicit)
- **`TenantSignatureConfig.IsEnabled` GIỮ NGUYÊN** — `Create()` nhận `bool isEnabled` → insert-true khả dĩ → `HasSentinel(true)` sẽ lật true→false (bug mới). Để lại + why-comment. Còn đúng 1 warning benign.

### B1 — soft-delete query filter trên 3 junction (commit `67653452`)
`EntityTypeConfigurationBase.ConfigureSoftDelete:60` áp global `!IsDeleted` cho UserGroup/AppMenu (kế thừa base); junction (`UserGroupMembership`/`GroupRoleAssignment`/`MenuRolePermission`) dùng config trần, required-FK tới principal, không filter → EF warning. **Latent** (không handler soft-delete group/menu, không Include từ junction). Fix = matching filter `!Principal.IsDeleted` trên 3 junction (correct-by-construction: principal soft-deleted → junction tự ẩn). Regression test: soft-delete group → membership ẩn, `IgnoreQueryFilters` vẫn thấy.

## What worked / verified
- Build 0W/0E · Fix A regression RED→GREEN · IpRulesApiTests 21/21 · Signing integ 85/85 · IdentityModuleClient 18/18 · PermissionAuth 5/5 · MenuSeederOrphanPurge 1/1 · MigrationConsistency 2/2.
- Runtime model WRN: **11 → 1** (1 còn = TenantSignatureConfig.IsEnabled benign).
- Không migration nào cần (HasSentinel + HasQueryFilter đều không vào snapshot/DDL — MigrationConsistency xác nhận).

## Lessons
1. **EF store-default-without-sentinel: flip CHỈ xảy ra khi DB default ≠ CLR default.** Phân loại từng cột (DB default vs CLR default) trước khi fix — đừng blanket-apply. 2/8 là bug bảo mật thật, 6/8 noise.
2. **HasSentinel KHÔNG nhân bản máy móc:** sentinel phải là giá trị **không bao giờ insert**. bool chỉ an toàn nếu một phía không bao giờ được insert (IsDeleted/IsRevoked); cột insert cả 2 giá trị (TenantSignatureConfig.IsEnabled) → không sentinel an toàn → để lại. (review-audit-self-decision §2: walk threat model từng case.)
3. **Test UPDATE-path pass KHÔNG bác bug INSERT-path** — sentinel/omit chỉ áp dụng cho INSERT. Test `Update_false/false` xanh ≠ insert-flip không tồn tại.
4. **Vault dev-mode in-memory = ephemeral**; luôn start qua `/aqt-runapp` (có auto-seed Step 1.5), không `dotnet run` trực tiếp.
5. **Running .NET host lock module DLL** → phải kill host trước khi `dotnet test` (MSB3026 file-lock). `dotnet run` qua `&` trong Bash tool bị teardown; dùng tool `run_in_background`.
6. **HasQueryFilter/HasSentinel không vào ModelSnapshot** → không gây MigrationConsistency drift, không cần migration (khác HasFilter index = DDL → cần migration).

## Test status
All targeted suites green (xem trên). CI parked (GitHub Actions disabled từ 04-13) → push không chạy CI; verify thủ công đầy đủ.

## Next session
- (Optional) Migration drop redundant DB default `TenantSignatureConfig.IsEnabled` → về 0 warning (YAGNI).
- Prod data audit: nếu env khác đã tạo IpFilterRule allow/disabled **trước** `ffbe2fac`, các rule đó có thể đang bị lật trong DB.
- Backlog tại threshold (640 lines, 12 closed) → cần optimize pass riêng.
