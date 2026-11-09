# Journal — Signing core 3-tier + Viettel 3-mode SHIPPED 2026-05-11

## Outcome

9 commits W0→W8 on origin/main (`2c8d4f7a` → `5a8621ca`) completing the production-grade signing core:

- **VGCA USB Token full 3-tier**: raw-hash (W1), structured XML/JSON (W2), doc-flow PDF (W3)
- **Viettel Mysign full 3-mode**: sync async=0 (W4), async-poll async=2 (W5), async-callback async=3 (W6)
- **Cross-cutting**: contract refactor D19 (W0), arch guards + capability tests (W7), integration guide + cookbook docs (W8)

## Pipeline summary

| Phase | Commit | What | Tests |
|-------|--------|------|-------|
| W0 | `2c8d4f7a` | D19 contract refactor — `SignatureProviderCapabilities` flag expansion (+RawSign/XmlDsig/JsonSign/DocFlow), `IStructuredSignatureService`, `IDocumentSigningWorkflow`, `SignatureFormat` enum, capability-gated `Resolve(key, required)` | 169/169 + 87/87 (no regression) |
| W1 | `6b14756c` | VGCA Tier-1 raw-hash fix — FE adapter SHA-256 + correct `{HashAlg,HashValue,Encode}` JSON + `Status==0` parse + BE raw RSA/ECDSA verify + Vite static-copy `vgcaplugin.js` | 175/175 + 87/87 |
| W2 | `fdc00a89` | VGCA Tier-2 XML/JSON — `StructuredSignatureService` XMLDSig + JWS (alg=none rejected CVE-2015-9235) | 181/181 + 87/87 |
| W3 | `084ba357` | VGCA Tier-3 doc-flow — 7 wrappers + `FileUploadHandlerController` GET/POST multipart + JWT 5min + nonce 2-slot + Files module bucket | 186/186 + 87/87 |
| W4 | `513c29e7` | Viettel sync — replace VNPT sp769; 3 typed HttpClients + Polly + IMemoryCache + 120s timeout | 181/181 (-9 deleted VNPT +4 new) + 87/87 |
| W5 | `e80319cd` | Viettel async-poll — `MysignStatusMapper` 15 codes + InlinePoll loop + DeferredPoll + ConfirmSignatureCommand | 186/186 + 87/87 |
| W6 | `3614b410` | Viettel async-callback — `SigningPendingTransaction` + `MysignInboundToken` + 2 webhooks (AllowAnonymous + Bearer + FixedTimeEquals) + idempotent + cleanup job | 192/192 + 87/87 |
| W7 | `bec64e2d` | Tests + arch guards — `CapabilityGateTests` (6) + `SignatureProviderCapabilityArchTests` + `CallbackAuthArchTests` + FE `vgca-availability-probe.test.ts` | 198/198 + 87/87 |
| W8 | `5a8621ca` | Docs — `docs/signing/integration-guide.md` (~850 LOC) + `usage-cookbook.md` (~650 LOC) + dependency-map + codebase-summary | n/a |

## Key technical decisions

1. **Provider granularity locked at 2** (`usb-vgca` + `remote-viettel-mysign`) with rich capability flags — not multiple per-tier providers.
2. **Vendor pivot mid-stream**: original Phase-05b assumed VNPT SmartCA sp769; W4 replaced with Viettel Mysign per user directive (Viettel SDK PDF on-disk).
3. **VGCA SDK reality**: `vgca_sign_msg` returns raw RSA/ECDSA bytes + separate `CertBase64` (NOT CMS/PKCS#7). Original Phase-05b's `UsbTokenSignatureProvider.VerifyAsync` was fundamentally wrong; W1 rewrote the verify path.
4. **Tier-3 storage**: integrate `IFileStorageService` from Files module (bucket "signing-docflow"). No new wwwroot/Upload directory.
5. **Callback auth model**: JWT short-lived 5min for VGCA doc-flow; HS256 inbound tokens (32-byte random, SHA-256 hashed in DB) for Mysign callbacks. NO cookie passthrough.
6. **Status codes locked** from Viettel spec p.43: 15 codes mapped via `MysignStatusMapper`; unknown → terminal failure.

## Process insights

1. **net10 SYSLIB0057**: must use `X509CertificateLoader.LoadCertificate(byte[])`, NOT `new X509Certificate2(byte[])`. Caught in W1 via subagent's pre-emptive guard.
2. **TS BufferSource type narrowing**: `crypto.subtle.digest('SHA-256', payload as unknown as BufferSource)` to bypass `Uint8Array<SharedArrayBuffer>` vs `Uint8Array<ArrayBuffer>` divergence in TS5.
3. **WireMock.Net scenario state machine unreliable** cross-version — counter-based callback pattern (`Interlocked.Increment` + `WithCallback`) is more reliable for sequenced poll responses.
4. **Test DB migrations**: WebAppFixture calls `IdentityDbContext.MigrateAsync` at startup; new EF migrations require full rebuild (NOT `--no-build`) so test assembly picks up the migration class.
5. **In-memory config seeding required**: appsettings.Development.json NOT picked up in `UseEnvironment("Testing")`. W3 + W6 had to seed `Signing:DocFlowJwt:*` + `Signing:Providers:ViettelMysign:Callback*` via `AddInMemoryCollection`.
6. **EF accessibility cascade**: when adding `using System.Security.Cryptography.X509Certificates` for `GetRSAPublicKey` extension method, BouncyCastle's `X509Certificate` clashes — alias `BcX509Certificate = Org.BouncyCastle.X509.X509Certificate`.
7. **Internal vs public DTO accessibility**: typed HttpClient methods returning `Task<MysignSignHashResponse>` requires the DTO record be `public` (CS0050 inconsistent accessibility otherwise).
8. **Test-driven fabrication caught**: tester subagent's `CrossModeFallbackTests.cs` referenced non-existent `SignatureRequest` type + invalid `SignatureFormat.{JsonSign,DocFlow}` values (those are Capability flags, not Format values). Deleted; per-phase tests already cover all modes.
9. **Subagent Bash output suppression**: 4 subagent calls reported empty Bash output despite exit-0 success. Main session re-ran builds/tests independently to verify. Pattern: subagent claims must be verified by main session.
10. **Vietnamese diacritics preserved**: 8 doc-flow terms (Phát hành, Phê duyệt, Ký công văn đến, etc.) used consistently in code comments + docs per QĐ 742 mục 5.1.

## Production gates remaining

| Gate | Owner | Lead time |
|------|-------|-----------|
| NEAC root cert → `VnCaRootStore` | secops + gov procurement (`rootca.gov.vn`) | 3-5 days |
| Viettel sandbox creds (ClientId/ClientSecret/ProfileId) | partner contract via Viettel | weeks |
| Viettel callback inbound creds (Vault `aqt/signing/viettel-mysign/callback-inbound`) | secops + Vault rotation policy | hours after sandbox |
| DocFlow JWT signing key (Vault `aqt/signing/docflow-jwt/key`) | secops + key rotation runbook | hours |
| Mysign callback source-IP gateway whitelist | infra team | hours coordinated |
| Cleanup job enabled outside test env | DevOps deploy config | post-MVP |

## Cumulative QĐ 742 impact

- 7/8 plan phases complete (01-06, 05b + signing-core)
- Mục 5.1 chữ ký số CA: BE + FE + audit + D6 acceptance gate + EF migrations all live
- ReqID 5.1 row 37 stays **PARTIAL** until NEAC root cert seeded (external blocker — independent of code)
- Coverage estimate: 40 MET + 1 PARTIAL = 97.6% (unchanged; this session was foundation rework not new requirements)
- Phase-07 Wave-3 (manual verify + 41 screenshots + audit dry-run) + Wave-4 (sign-off package) still pending

## Next session candidates

1. **Phase-07 Wave-3**: capture 41 screenshots + 4 UNVERIFIED scenario walkthroughs + audit dry-run report
2. **NEAC root cert procurement**: download from rootca.gov.vn → embed in `VnCaRootStore._productionThumbprints` → flip ReqID 5.1 to MET (small code <1d once cert in hand)
3. **Viettel sandbox E2E**: when partner creds available, flip W4-W6 from WireMock-only to live API smoke

## References

- Plan: `plans/260511-0857-signing-core-3tier-viettel/`
- Memory snapshot: `~/.claude/projects/C--AqtCoreFW/memory/signing-core-3tier-viettel-shipped-260511.md`
- Spec amendments: `docs/signing/spec-amendments.md` (D19 entry)
- Integration guide: `docs/signing/integration-guide.md`
- Cookbook: `docs/signing/usage-cookbook.md`
- VGCA SDK on-disk: `other/vgca_usb_token/`
- Viettel SDK PDF: `other/remote_signing_viettel/Tai lieu tich hop dich vu Mysign API SDK v1.9.pdf` (extracted text `plans/reports/viettel-mysign-extracted-260511.txt`)
