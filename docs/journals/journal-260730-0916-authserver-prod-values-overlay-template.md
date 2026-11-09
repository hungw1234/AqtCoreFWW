# Journal — AuthServer prod values overlay (template) + Forms-2A changelog backfill

**Date:** 2026-07-30 · **Branch:** main · **Commits:** `2a8d1ac37`, `6c4f138f5`

## What happened

`/aqt-next` → recommended Option A (housekeeping + close the easiest launch blocker,
`AUTHSERVER-PROD-VALUES-SPA-REDIRECT-URIS`). `/ak:cook "theo đề xuất"`.

Audit-at-slice-start (INVARIANT 260522-A) on the target row **partially reversed the plan**:
the row is real (chart has only `values.yaml` + `values-local.yaml`, no prod overlay — probe
confirmed), BUT it needs a real prod domain to close, and no prod cluster/domain exists
(backlog "waiting-on-infrastructure" — do-NOT-auto-propose queue). Authoring an overlay with
a hardcoded domain would be fake data. Surfaced the conflict; user chose **template overlay**.

## Delivered

- NEW `infra/helm/authserver/values-prod.yaml` — every deployment-specific value is a
  `<<REPLACE-...>>` placeholder (7 distinct tokens). Not fake data: placeholders render literally,
  fail loud at boot if unreplaced. Structure-ready, real values substituted at deploy time.
- Mirrors `values-local.yaml` shape; prod deltas: HTTPS enforced (`Auth__AllowHttp` omitted,
  ssl-redirect true, real TLS placeholder), real registry, prod cert-custody placeholder
  (ties to `AUTHSERVER-PROD-CERT-PROVISIONING-RUNBOOK`).
- `helm template -f values-prod.yaml` renders 0-error (oracle). code-review DONE 0-crit.

## What was non-obvious / learned

- **env is Helm full-replace here** (`deployment.yaml` renders `toYaml .Values.env`), so the
  overlay must repeat the WHOLE env list — a dropped base key (e.g. `ConnectionStrings__Default`)
  would silently vanish. `resources`/probes/`podSecurityContext` are deep-merged (NOT repeated).
  Reviewer confirmed no base key dropped.
- **SPA URI ordering invariant (permanent authserver trap):** `PostLogoutRedirectUris` must list
  slashless-origin FIRST, trailing-slash SECOND. `new Uri("https://h") == new Uri("https://h/")`;
  seeder `ISet<Uri>` keeps the first spelling; OpenIddict matches logout by ordinal vs the SPA's
  slashless `window.location.origin`. Reversed ⇒ 400 on every logout, but login still works —
  exactly the [[feedback-observation-matching-hypothesis-is-not-verification]] failure shape.
- **Placeholder-not-fake is the right call** for a blocker whose only missing input is an
  environment that does not exist. Closing the code-shape without inventing data avoids the
  "opens, closes with still-blocked" waste the backlog warns about (`PROMETHEUS-DEADMAN-SWITCH`
  precedent 2026-07-21).

## Row status

`AUTHSERVER-PROD-VALUES-SPA-REDIRECT-URIS` stays `[blocks-v1.0]` OPEN — the overlay cannot deploy
until real origins/registry/cert are substituted. That substitution is the remaining launch gate.

## Still pending (not this slice)

- The **HARD GATE re-weigh** (Forms Phase-3/4 vs the 6 `[blocks-v1.0]` blockers) — not yet
  presented to the user. Forms Phase-2 is complete; Phase-3/4 are gated behind it.
- 3 blockers are user/auditor-decision-bound (VNeID v1.0 yes/no, prod cert custody,
  GOV auditor purge-chain re-anchor) — the real launch critical path.
