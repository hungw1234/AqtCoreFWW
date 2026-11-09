# AuthServer Production Certificate Provisioning Runbook (Vault PKI)

**Purpose:** Operational procedure for issuing, delivering, and rotating the OpenIddict signing + encryption certificates the AuthServer needs in non-Development environments, using HashiCorp Vault PKI as the certificate authority + custody store.

**Compliance:** QĐ 742 cấp 4 mục 4.4.a — no hardcoded credential/key material; certificates issued and custodied by a managed authority, never committed.

**Audience:** Ops engineers standing up a production/staging AuthServer deployment.

**Custody decision:** cert custody = **Vault PKI** (user decision 2026-07-30). This runbook supersedes the interim file-Secret assumption in `infra/helm/authserver/values-prod.yaml` (self-signed local PFX stays for the kind rehearsal only).

**Template-repo note:** AqtCoreFW is a template repository with no live production environment. This runbook describes the pattern a downstream deployment must implement. The Vault PKI engine, HA cluster, and AppRole auth are infrastructure responsibilities (D10.5), not application scope — none exist in this repo yet.

---

## 1. What the app needs (fixed contract — do not change here)

The AuthServer loads certificates the SAME way in every non-Development environment. This is the contract the provisioning mechanism must satisfy; it is intentionally NOT changed by this runbook.

| Config key | Meaning | Source |
|------------|---------|--------|
| `Auth:SigningCertificate:Path` | Filesystem path to a **PFX** file (PKCS#12) | mounted file |
| `Auth:SigningCertificate:Password` | PFX password | Vault-via-config |
| `Auth:EncryptionCertificate:Path` | Filesystem path to a **PFX** file | mounted file |
| `Auth:EncryptionCertificate:Password` | PFX password | Vault-via-config |

Reference: `src/host/AqtCoreFW.AuthServer/Program.cs` — `LoadRequiredCertificate` reads a PFX file from disk (`X509CertificateLoader.LoadPkcs12FromFile(path, password)`) and **fails fast at startup** if the path is unset or the file is missing. It does not accept PEM or raw bytes. An OIDC server that silently minted tokens with an ephemeral key would invalidate every token on each restart, so the loud failure is deliberate.

**Consequence:** whatever Vault PKI delivers must end up as a **PFX file on a path the pod can read**, with its password available via Vault-config. Section 4 covers how.

---

## 2. Vault PKI Topology Requirements

| Component | Production requirement |
|-----------|------------------------|
| Vault cluster | HA ≥3 nodes, Raft/Consul backend, auto-unseal via KMS/transit (NEVER Shamir in prod) |
| PKI mount | Dedicated PKI secrets engine, e.g. `pki_authserver/` |
| Issuer | Intermediate CA signed by the org root (root stays offline); intermediate in Vault |
| Role | A PKI role scoped to the AuthServer's cert needs (see §3) |
| Auth method | AppRole (per-host RoleId/SecretId), token auth disabled |
| Audit | File audit device + syslog forwarder |

The dev/kind Vault (dev-mode, in-memory) is for local rehearsal ONLY and does NOT run PKI — the kind cluster uses self-signed PFX from Secret `authserver-openiddict-certs` minted by `scripts/k8s-local-generate-prod-secrets.sh`. That path is explicitly out of scope for prod.

---

## 3. Vault PKI Engine + Role Setup (one-time, per environment)

```sh
# 1. Enable a dedicated PKI mount for the AuthServer
vault secrets enable -path=pki_authserver pki
vault secrets tune -max-lease-ttl=8760h pki_authserver   # 1y max; certs issued shorter (§5)

# 2. Configure an intermediate issuer signed by the org root (root CA stays OFFLINE).
#    Generate a CSR, have the offline root sign it, import the signed cert:
vault write pki_authserver/intermediate/generate/internal \
    common_name="AqtCoreFW AuthServer Intermediate CA" key_type=rsa key_bits=4096
#    → send CSR to offline root → import signed intermediate:
vault write pki_authserver/intermediate/set-signed certificate=@signed_intermediate.pem

# 3. Define a role for the OpenIddict signing/encryption certs.
#    OpenIddict signing/encryption certs are used for JWT/token protection, NOT TLS server auth,
#    so no SAN/DNS constraints are required — key usage is the constraint that matters.
vault write pki_authserver/roles/openiddict \
    allow_any_name=true \
    max_ttl=2160h \                # 90d (see rotation §5)
    key_type=rsa key_bits=2048 \
    key_usage="DigitalSignature,KeyEncipherment"
```

Store the PFX password used by the delivery step (§4) as a Vault KV secret so it flows through `AddVaultConfiguration` into `Auth:{Signing,Encryption}Certificate:Password` — mirror the existing secret-path convention in `docs/ops/vault-key-rotation-runbook.md` §2 (e.g. `secret/data/aqt/authserver/openiddict-pfx-password`).

---

## 4. Delivery: getting a PKI-issued cert onto the pod as a PFX

Vault PKI issues **PEM** (cert + a separate private key). The app reads a **PFX file**. Two options bridge that gap.

### Option A — init-container PEM→PFX (RECOMMENDED)

An init-container authenticates to Vault (AppRole), requests a cert from the `openiddict` role, packs the returned PEM cert + key into a password-protected PFX, and writes it to a shared `emptyDir` the app container mounts at `/certs/signing.pfx` and `/certs/encryption.pfx`.

- **Zero app-code change** — `LoadRequiredCertificate` stays exactly as-is; the PFX-file contract is honored.
- The PFX password the init-container uses to pack MUST equal the one the app reads from Vault-config (§3).
- Rotation (§5) = restart the pod (init-container re-issues) or run the pack step as a sidecar that re-packs on Vault lease renewal.
- Trade-off: the private key transits an `emptyDir` (memory-backed) for the pod's lifetime. Acceptable — `emptyDir` never touches durable storage and is destroyed with the pod. Set `emptyDir.medium: Memory`.

Sketch (per environment overlay — NOT added to the base chart until a real cluster exists):

```yaml
initContainers:
  - name: vault-pki-fetch
    image: <<REPLACE-VAULT-PKI-FETCH-IMAGE>>   # vault CLI + a PEM→PFX pack step (e.g. openssl pkcs12 -export)
    env: [ /* VAULT_ADDR, AppRole RoleId/SecretId from Secret, PFX password from Vault-config */ ]
    volumeMounts: [ { name: openiddict-certs, mountPath: /certs } ]
volumes:
  - name: openiddict-certs
    emptyDir: { medium: Memory }
```

### Option B — teach the app to read PEM from Vault-config (DEFERRED)

Modify `LoadRequiredCertificate` to accept a PEM cert+key (Vault-injected into config) and build the `X509Certificate2` in memory (`X509Certificate2.CreateFromPem`), removing the file/PFX hop.

- Cleaner data path (no PFX pack, no emptyDir) but changes a **load-bearing startup contract** and its 4 unit tests (`OpenIddictCertificateLoadingTests`).
- Deferred: do this only if Option A's operational cost proves real. Keeping the file-PFX contract keeps the app agnostic to how the cert arrives.

---

## 5. Rotation

- Issue certs with a **90-day** TTL (role `max_ttl=2160h`); rotate well before expiry.
- Option A: a scheduled pod restart (or lease-renewal sidecar) re-issues + re-packs. OpenIddict picks up the new signing cert on process start; both the old and new cert should overlap in the OpenIddict key ring during the changeover so in-flight tokens validate — issue the new cert, add it as an additional signing credential, retire the old after the max token lifetime (refresh-token = 7d) has elapsed.
- Never let a signing cert expire in place: an expired signing key breaks all token issuance.

---

## 6. Failure Modes

| Symptom | Cause | Fix |
|---------|-------|-----|
| Pod CrashLoops at startup, "certificate not configured" | `Auth:SigningCertificate:Path` unset | overlay/init-container did not populate the path |
| Pod CrashLoops, "certificate file not found" | init-container failed to write the PFX | check Vault AppRole auth + PKI role permissions |
| All tokens invalid after a deploy | signing cert changed without key-ring overlap | follow §5 overlap procedure |
| Logout returns 400 | unrelated — SPA post-logout URI ordering | see `values.yaml` cert comment + plan `260728-1331-*` |

---

## 7. Related

- `infra/helm/authserver/values-prod.yaml` — prod overlay (template); its cert comment points here.
- `docs/ops/vault-key-rotation-runbook.md` — KV secret rotation (the PFX password lives there).
- `docs/ops/vault-secrets-management-guide.md` — secret-path conventions.
- Backlog row `AUTHSERVER-PROD-CERT-PROVISIONING-RUNBOOK` — the doc-portion this runbook closes; the engine build + delivery wiring remain gated on a real cluster.
