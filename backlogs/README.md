# AqtCoreFW Backlogs

Thư mục quản lý backlog dự án. Source of truth cho remaining work items.

## Files

| File | Purpose |
|------|---------|
| `backlog.md` | **Active backlog** — all remaining tasks, tiered by priority |
| `completed.md` | Archive of completed work by session date |
| `README.md` | This file |

## Workflow

1. Mỗi session, Claude đọc `backlog.md` để biết task tiếp theo
2. Khi hoàn thành task, move item từ `backlog.md` → `completed.md`
3. Khi phát hiện task mới, thêm vào `backlog.md` đúng tier
4. Memory `backlog.md` trỏ về file này thay vì duplicate nội dung

## Tier System

| Tier | Priority | Meaning |
|------|----------|---------|
| T1 | 🔴 Blocker | CI/CD broken, production issue, ship-blocking |
| T2 | 🟠 High | Feature incomplete, security gap |
| T3 | 🟡 Medium | UX gap, missing pages, test coverage |
| T4 | 🔵 Low | i18n, polish, optimization |
| T5 | ⬜ Strategic | Future roadmap, architectural evolution |

## Launch Buckets

Orthogonal to tier. Tier = how bad; bucket = when it must ship. Tagged inline right after the row ID in `backlog.md`.

| Bucket | Tag | Meaning |
|--------|-----|---------|
| Blocks launch | `` `[blocks-v1.0]` `` | Must ship before v1.0. Affects a v1.0 module, or is a security/compliance/data-integrity defect reachable in production. |
| After launch | *(untagged — default)* | Real work, ships after v1.0. Untagged is deliberate: a row nobody triaged must never read as a blocker. |
| Closed | `` `[wontfix]` `` | Decided not to do, or already resolved. Row kept as a record so it is not re-filed. |

Find the launch-critical set: `grep '\[blocks-v1.0\]' backlogs/backlog.md`
