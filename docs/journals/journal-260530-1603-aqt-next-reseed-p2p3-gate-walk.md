# Journal — 2026-05-30 16:03 · `/aqt-next` Reseed P2/P3 Gate-Walk (no-code, decision session)

## Session goal
`/aqt-next` orient → user chọn track reseed plan (`260530-0803-reseed-all-modules-demo-data`) → đi qua gate chặn P2/P3 trước khi cook. Mục tiêu: chốt các quyết định user-only để unblock cook Phase 2/3. KHÔNG code, KHÔNG commit.

## What worked (verified)
- **State survey:** working tree sạch, 7 ships hôm nay (phiên trước), 0 ahead/behind. Active plan P1 ✅ runtime-verified (`a75b03bb`+`08f4788d`).
- **Verify-gate TRƯỚC khi hỏi** (scout-first): probe schema + consumer trước khi present gate → bắt được 4 câu địa giới gốc **sai nền**:
  - Live consumer = `web/src/features/forms/builder/address-field-widget.tsx` — cascade **3 cấp** Tỉnh→Huyện→Phường (`useProvinces`→`useDistricts(p)`→`useWards(p,d)`, endpoints `MasterDataController:42,55,68`).
  - `Ward.Code` + `Ward.DistrictCode` + `District.Code` **đều required** (entity factory) → "không cần mã" bất khả thi.
  - Model `AdministrativeUnit` (level-aware, hệ-34-ready) tồn tại nhưng widget KHÔNG consume.
- **5 quyết định CHỐT** (persist `plan.md` Validation Session 3 + memory): BNV scale **22 cấp-2** (ND 25/2025, lật "~100-200") · geo model **2-cấp hệ-34 thật** · scope **đầy đủ HN126/HCM168/ĐN94 + 2 đặc khu** · code **generated** · bundling **(b) bundle P3a+P3b**.
- **Stale sync:** plan.md/phase-02 `~100-200`→`22`; P1 status→runtime-verified.
- **Process cleanup:** stop 2 dev host leftover (`AqtCoreFW.Api` 24740 + `AqtCoreFW.AuthServer` 25164), port 5001 freed.

## What didn't / lessons
- **(advisor catch) Writing got ahead of the decision.** Sau khi user chọn "2-level", tôi viết migration thành *the path* trong plan rồi quay sang verify downstream — trong khi đó là một **FE feature migration bundle vào data-task**, không bắt buộc cho seed definitions-only (option A pseudo-district seed được data hệ-34 với 0 FE-risk). Nhãn "hệ-34 thật" hấp dẫn, cost ở description, KHÔNG phải option recommended (A là). Fix: KHÔNG đảo lựa chọn 2-level (end-state user chọn) mà tách **timing** → gate (b) discriminating "data-vs-feature bundling". CLAUDE.md §3 (guard user decision) + anti-bundling preference đều trỏ cùng hướng.
- **Lesson (promote):** verify-gate phải chạy TRƯỚC khi đóng khung câu hỏi, không chỉ trước khi code — 4 câu research-surface sẽ làm cook vỡ scope nếu rubber-stamp.

## Decisions made
Xem `plans/260530-0803-reseed-all-modules-demo-data/plan.md` → **Validation Session 3** (canonical). Tóm: 5 gate chốt + P3 reshape thành P3a (seed AdministrativeUnit 2-cấp) + P3b (FE migration, user chọn bundle). P3b PRE-BUILD verify (do bundle): re-level AdministrativeUnit hiện 3-cấp · liệt BE consumer `provinces/{c}/districts` · định lượng saved-form orphan `district` · số phận entity legacy Ward/District.

## Test status
N/A — no code, no test run this session.

## Next session (carry-over)
1. **Cook P2** (BNV=22, generalize `Btc*Seeder` — độc lập geo, ~5-8d) — first-cook an toàn nhất.
2. **Rewrite `phase-03`** thành cấu trúc P3a/P3b trước khi cook P3.
3. **P3b pre-build verify** 4 mục (re-level / BE consumer / orphan district / Ward-District fate).
4. **Backlog-optimize DUE** — CLOSED_HDRS=12 (≥10 trigger), BL_LINES=567. Surface, không auto-run (archive-pass nặng, orphan-risk) — chạy có chủ đích session sau.

## Refs
- Plan: `plans/260530-0803-reseed-all-modules-demo-data/plan.md` (Validation Session 3)
- Memory: `saturday-260530-reseed-all-modules-demo-data-plan.md` (S3 GATE RESOLVED block)
- Research: `research/bnv-org-structure-source.md` · `research/dia-gioi-3-thanh-pho-source.md`
