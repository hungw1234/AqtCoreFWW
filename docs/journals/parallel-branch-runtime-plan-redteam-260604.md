# Parallel-Branch Runtime Enablement — Investigation + Plan + Red-Team (2026-06-04)

**Type:** planning/investigation session — **ZERO code, ZERO commits** (git tree clean). Output = 2 plans + memory + this journal.

## Session goal
Start: `/cook "Thêm seeding 1 Workflow Def có nhánh song song để làm sample"` (sau `/ask` "sao branch không hiện trên UI"). Mục tiêu gốc = thấy nhánh song song render trên Visual Designer.

## What happened (escalation chain)
1. **/ask** — giải thích: branch CÓ render trên canvas (gateway diamond + dashed edges via `gateway-node-build-utils.ts` + read-back `AttachBranchesAsync`). Canvas trống chỉ vì chưa seed branch nào.
2. **/cook → Phase-0 GO/NO-GO** — user muốn def "chạy được", không chỉ render. Scout sâu phát hiện **runtime fork chưa từng chạy end-to-end + bị chặn**:
   - **G2 blocker:** `ExecuteTransitionCommandHandler.cs:194-216` WF_NO_HANDLER pre-check fire TRƯỚC fork staging `:361`, không branch-aware → vào fork-only branching state = 422 trước khi fork chạy.
   - **G1:** `WorkflowGraphValidator.cs:306-328` R1-activate bắt mọi non-final state có outgoing transition → parallel def không activate.
   - **G3:** zero test fork end-to-end.
   - → NO-GO cho "runnable via seed". User chọn tách feature riêng.
3. **/ck-plan validate** (Full tier, 0 FAILED) — chốt Q1=pool/claim, Q1b=per-child assignment-rule full → scope nở thành subsystem mới (5→7 phase, ~1-2 tuần).
4. **/ck-plan red-team** (4 reviewer, 16 finding, 100% file:line evidence) — reshape plan + bắt 4 bug engine; effort thật ~3-4 tuần.

## What worked (verified)
- **Scout-first + advisor** bắt blocker NOH×fork TRƯỚC khi viết code (tiết kiệm nhiều tuần đi sai).
- **Red-team cực giá trị:** 4 reviewer độc lập, mọi finding có evidence. Bắt được:
  - 4 bug engine plan bỏ sót: fork-escape (RT1), join partial-commit stranding (RT2), không stage continuation task (RT3), partial-empty deadlock (RT4).
  - 3 giả định "reuse" sai: không có resolver API cho child (RT5), resolve&claim disjoint (RT6), validator là pure-fn không DB (RT9).
  - 2 đường bypass pool (assign endpoint + DefaultAssignee skip candidate-index).
  - Hội tụ 3/4: **DefaultAssigneeId ĐÃ chạy end-to-end** (`ParallelBranchService.cs:76-77`) → MVP demo ~2-3d, subsystem là gold-plating cho demo.

## What didn't / lessons
- **Mỗi nấc escalation lộ một constraint code-located mới** đảo lại quyết định trước (tenant BTC/BNV→DevTenant vì loginable user; view-only→runnable→full pooling). → khi user chọn "cái lớn hơn" liên tiếp, surface bức tranh hợp nhất + off-ramp, đừng grind.
- **Validate tôi đưa DefaultAssignee-vs-pool nhưng CHƯA verify** DefaultAssignee đã chạy thật — red-team mới chứng minh `:76-77`. Lesson: verify "đã chạy" trước khi đưa làm option, không chỉ "tồn tại field".
- Scope nở 20-40× so với yêu cầu gốc qua chuỗi quyết định cục bộ — mỗi câu hỏi hợp lý, tổng lại thành feature 3-4 tuần.

## Decisions made
- **Tenant:** DevTenant (loginable `officer@dev.local`), KHÔNG BTC/BNV (fill-user không login được).
- **Q1/Q1b:** giữ FULL pooling subsystem (production intent) — red-team MVP-cut REJECTED bởi user (hợp lệ).
- **OQ-A ✅ RESOLVED:** reuse `WorkflowTransitionAssignment`/`...Clause` keyed by `BranchChildId` + resolver overload `ResolveAssignmentAsync(clauses, ctx)` (không mint type mới, feed resolver trực tiếp — RT5).
- **Off-ramp giữ mở:** view-only seed 30ph (mục tiêu /ask gốc) / MVP DefaultAssignee ~2-3d.

## Test status
N/A — không code, không test chạy. Plan có Phase 5 = first end-to-end fork integ test (gate "chạy thật").

## Next session (carry-over)
- **User đang review plan** `plans/20260604-2205-parallel-branch-runtime-enablement/` (7 phase, validated + red-teamed, OQ-A resolved). Chờ quyết appetite.
- Nếu cook full → Phase 1 (per-child assignment datamodel reuse `WorkflowTransitionAssignment` + resolver overload).
- Nếu đổi ý → view-only seed (30ph) hoặc MVP DefaultAssignee (~2-3d).
- Backlog row: `WF-PARALLEL-RUNTIME-ENABLEMENT`.
- Memory: `thursday-260604-parallel-branch-runtime-unproven-blocked.md` (chi tiết đầy đủ chuỗi + red-team).
