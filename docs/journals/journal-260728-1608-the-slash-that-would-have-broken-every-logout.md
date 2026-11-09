# Journal — 2026-07-28 16:08 · Dấu gạch chéo suýt làm hỏng mọi lần logout

**Commits:** `f7c3f81ca` + `ebae6f11b` → merge `3c0492d9a` (PR #135) · **Plan:** `260728-1331-oidc-spa-redirect-uris-from-config` (3 phase, COMPLETE)

---

## Session goal

Mở bằng `/aqt-next`. Backlog vừa triage sáng nay còn 6 row `[blocks-v1.0]`; trong đó chỉ
`OIDC-SPA-CLIENT-REDIRECT-URIS-HARDCODED` là **không chờ user hay prod env** — 4 row còn lại đều
gated. Chọn nó. Mục tiêu: đưa redirect URI của SPA client từ hardcode sang config, gỡ chặn
browser-login trên kind.

## What worked

- **Audit ground-truth trước khi mở scope** (INVARIANT 260522-A). Row 5 ngày tuổi. Grep xác nhận
  hardcode còn nguyên `OpenIddictDataSeeder.cs:62-93` và **zero** config key `SpaClient` tồn tại →
  verdict real-ready. 5 phút, tránh được rủi ro mở nhầm row đã stale.
- **Scout tìm ra blast radius mà row không nhắc.** `scripts/3_setup-tunnel-urls.py:100-124`
  regex-rewrite chính file C# sắp đổi. Nếu không thấy, đổi shape xong script thành no-op im lặng —
  vẫn in "[OK]" nhưng không match gì. Đây là loại hỏng tự báo cáo thành công.
- **Hỏi 3 câu trước khi plan.** Config shape (URI đầy đủ vs origin+ghép path), fallback posture,
  và xử lý script tunnel. Cả ba đổi hẳn shape implementation. Rẻ hơn nhiều so với đoán rồi làm lại.
- **Planner sửa 3 điểm sai trong brief của tôi** — `Program` là `public partial class` chứ không
  `internal` (nên không cần `InternalsVisibleTo`); `AuthServerHostFixture.cs:98-101` ghi verbatim
  rằng seeder chạy thật trong fixture (nên test-path thật khả thi); chart authserver **không có**
  `values-staging/prod.yaml`. Tôi verify độc lập cả ba trước khi tin.
- **Validate trước `DeleteAsync`.** Client bị delete-and-recreate mỗi boot, nên lỗi config giữa
  chừng sẽ để lại deployment không có SPA client và không cách nào login vào sửa. Thứ tự này là
  yêu cầu R5 trong plan, reviewer xác nhận diff enforce đúng.

## What didn't / lessons

- **Tôi suýt ship một lỗi làm hỏng mọi lần logout, dựa trên suy luận không kiểm chứng.**
  Giữa slice tôi rút danh sách post-logout từ 8 xuống 4 entry, tin rằng OpenIddict normalize URI.
  **Nó không normalize.** Chuỗi sự thật đúng: `new Uri("http://h:p")` bằng `new Uri("http://h:p/")`
  → `ISet<Uri>` chỉ giữ spelling **được chèn đầu tiên** → OpenIddict lưu `OriginalString` của
  instance đó → `ValidatePostLogoutRedirectUriAsync` so sánh `StringComparison.Ordinal` (nhánh
  parse URI nới lỏng chỉ dành cho native client, client này là Public) → SPA gửi
  `window.location.origin`, luôn không slash. Bản tôi ship sẽ trả **400 invalid_request mọi lần
  logout** ở dev/kind/tunnel, trong khi **login vẫn chạy bình thường** — triệu chứng không giống
  nguyên nhân chút nào.
- **Điều tệ hơn: tôi tưởng mình đã kiểm chứng.** Tôi có đọc store và thấy nó trả về dạng không
  slash, rồi coi đó là xác nhận. Thực tế fixture load `appsettings.Development.json`, mà file đó
  vẫn liệt kê dạng không-slash trước — nên cái tôi "quan sát" là config, không phải cơ chế. Một
  quan sát khớp với giả thuyết sai vẫn là quan sát khớp. Đáng lẽ phải probe `HashSet<Uri>` trực
  tiếp (mất 2 phút, tôi làm sau đó và nó trả lời dứt khoát) thay vì suy từ một lần đọc gián tiếp.
- **Test lẽ ra bắt được thì lại xanh xuyên qua lỗi.** Smoke test tưởng cover fallback nhưng đang
  đọc block config mà script tunnel để lại trong dev config. Reviewer chẩn đoán đúng: nó xanh vì
  lý do khác với điều nó tuyên bố. Sau khi revert block, mutation slash-first mới làm nó đỏ —
  trước đó không. **Cùng một test, cùng một assertion, khác nhau ở chỗ nguồn config.**
- **Tiêu chí acceptance do tôi tự đề xuất để thay thế cũng quá lỏng.** A7 gốc ("byte-identical với
  HEAD") bất khả thi vì slice này chính là thứ thêm block đó. Tôi đề xuất "idempotent + khớp
  fallback" — reviewer chỉ ra nó sẽ **pass trong khi H2 còn sống**. Tiêu chí đúng là "`--restore`
  trả file về đúng committed state". Tôi nới tiêu chí về mức mình đã đạt, thay vì về mức bảo vệ
  được thứ cần bảo vệ.
- **3 vòng review, vòng nào cũng ra lỗi thật.** R1 BLOCKED (C1 post-logout + H1 A7 + M1 origin-drop
  + A3 false-green) → R2 DONE_WITH_CONCERNS (H2 `--restore` re-inject + L2 comment cũ) → R3 DONE.
  Không vòng nào là thủ tục.

## Decisions made

- **Config shape = 2 mảng URI tuyệt đối** (user chọn), không phải origin-list rồi ghép path. Đánh
  đổi: helm dài dòng hơn, bù lại khớp 1-1 với OpenIddict descriptor.
- **Dev fallback / non-Dev fail-fast** (user chọn), theo đúng posture `Auth:SigningCertificate` đã
  ship. Base `values.yaml` **cố ý không set** SPA key → deploy prod đầu tiên CrashLoop, đó là
  thiết kế: thà chết ở boot còn hơn seed client sai origin rồi hỏng mờ ám ở login.
- **`appsettings.Development.json` giữ sạch, không commit block `SpaClient`.** Fallback C# là
  nguồn sự thật duy nhất cho dev zero-config. `--restore` **xoá** khoá thay vì ghi lại giá trị
  tương đương — vì "tương đương nhưng có mặt" đủ để làm smoke test mất tác dụng.
- **Không làm L1 scheme allowlist** trong slice (reviewer đồng ý ngoài scope) → đã tạo row T4.

## Test status

| Gate | Kết quả |
|---|---|
| AuthServer build `-warnaserror` | 0W / 0E |
| AuthServer unit | 40/40 (9 test mới, mutation-verified) |
| Startup smoke (**bin sạch**) | 3/3 (1 test mới qua registration path thật) |
| Architecture | 203/203 |
| Full solution build | 0 error (64 warning NU1902 AngleSharp — pre-existing) |
| `helm template` values-local | 2 redirect key, `__0` slashless |

Mutation check chạy 3 lần và đều đúng chiều: bogus URI trong seeder → smoke đỏ; fallback
slash-first → unit test đỏ; fallback slash-first (sau khi revert dev config) → smoke đỏ.

## Next session

1. **`OIDC-BROWSER-LOGIN-ROUNDTRIP-VERIFY-ON-KIND`** — chưa có login thật nào chạy qua kind.
   Slice này gỡ blocker *code*; round-trip và **logout** (đúng chỗ C1 suýt lọt, không có
   integration coverage) vẫn chưa được chứng minh. Cần cluster lên.
2. **`AUTHSERVER-PROD-VALUES-SPA-REDIRECT-URIS`** `[blocks-v1.0]` — chưa có overlay prod, cần
   origin thật. Nhớ thứ tự slashless-first.
3. Các row user-owned còn treo: VNeID v1.0 có/không · prod cert custody · seed-posture §4-Q2 ·
   prod Vault seal-posture.
4. `SMOKETEST-BINDS-COPIED-APPSETTINGS-STALE-BIN-TRAP` (T4) — bẫy test-infra vừa cắn một lần.

## Ghi chú ngoài lề

Xoá một `.git/index.lock` mồ côi (0 byte, không process git nào chạy) sau khi hỏi user — nó chặn
mọi thao tác git ghi suốt nửa session, và `git checkout` để verify A7 phụ thuộc vào nó.
