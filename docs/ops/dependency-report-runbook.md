# Dependency Report Runbook

The `Dependency report` workflow lists every library and platform version the base uses, compares it with what is published, and writes the result into one pinned GitHub issue. It is read-only: it does not edit `Directory.Packages.props`, `package.json` or any other file, and it does not open pull requests. Upgrades are done by asking Claude to work from the issue (see "Acting on the report").

## Where the report is

- Issue: the single open issue labelled `dependency-report`, titled "Dependency report", pinned on the repository's Issues tab. The first one is [#148](https://github.com/thiennd212/AqtCoreFW/issues/148).
- Full report (no row limits) and raw data: artifact `dependency-report` on each workflow run, kept 30 days (`full-report.md`, `report-data.json`, plus the raw `dotnet` and `pnpm` JSON).
- The issue text is in Vietnamese because it is read by the team; package names are verbatim.

## When it runs

- Every Monday 01:00 UTC (08:00 Asia/Ho_Chi_Minh), from `.github/workflows/dependency-report.yml` on `main`.
- On demand: Actions tab, "Dependency report", "Run workflow"; or `gh workflow run dependency-report.yml`.

## What it covers

| Source | How | Report section |
|---|---|---|
| `src/Directory.Packages.props`, `tests/Directory.Packages.props` (including transitive pins) | Parses `<PackageVersion>` entries, queries the nuget.org flat-container API and skips versions nuget.org marks unlisted | Major, Minor/patch, version mismatch between the two files |
| NuGet vulnerabilities | `dotnet list src/AqtCoreFW.slnx package --vulnerable --include-transitive --format json` | CVE |
| `web/package.json` | `pnpm outdated --format json` for installed versions, npm registry for published versions (deprecated ones and anything above the `latest` dist-tag skipped), `pnpm audit --json` | CVE (critical and high listed, moderate and low counted), Major, Minor/patch, deprecated |
| `dotnet-tools.json` | nuget.org flat-container API | Major, Minor/patch |
| `src/global.json` SDK | .NET release index (`builds.dotnet.microsoft.com/.../releases-index.json`) | Summary and Minor/patch (flags a channel release that carries security fixes) |
| Docker images (Dockerfiles, compose files, workflow `services`) and GitHub Actions `uses:` | Listed with file and line; versions are not compared | Pinned images and actions |
| Licences | `scripts/dependency-report/license-watchlist.json` | Licence changed to commercial |

Prerelease versions are only offered when the package is already on a prerelease, the same rule `dotnet list package --outdated` applies. "Major" follows caret-range semantics: for 0.x packages a minor bump (0.4 to 0.5) counts as major, because it may break.

## Report sections

- **CVE**: fix first. Each web row shows the minimum patched version per major line and the dependency path that pulls the package in.
- **Licence changed to commercial**: packages whose newer majors moved to a paid licence. "Đang dùng bản thương mại" means the version in use is already under the paid licence; "Kẹt ở bản miễn phí cuối" means the base is on the last free major and will stop receiving security patches. Both need a replacement plan, not an upgrade. Packages listed here are tagged `⚠️ license` in the Major table; do not upgrade them.
- **Version mismatch between props files**: the same package pinned to different versions in `src/` and `tests/`.
- **Major** and **Minor/patch**: a package can appear in both; Minor/patch shows the newest version within the current major.
- **Thu thập lỗi** (collection errors): appears when a data source failed. Sections that depend on it may be empty for that reason, not because nothing is outdated.

When a run fails after the issue was found, the workflow comments on the issue with the run link; the issue body then still holds the previous week's report.

## Acting on the report

Read the issue, then hand Claude one group at a time. Each group becomes one pull request that must pass CI before a person merges it.

| Group | Command to give Claude |
|---|---|
| CVE | `/ak:cook upgrade the packages in the 🔴 section of issue #148, one PR` |
| Minor/patch | `/ak:cook upgrade the packages in the 🟢 section of issue #148, one PR` |
| Major | `/ak:cook upgrade <package> to <version> per the dependency report; read its release notes first` (one package per PR) |
| Licence | Open a separate replacement plan; do not upgrade these packages |

After the PR is merged, run the workflow manually to refresh the issue.

## Maintaining the workflow

- Code: `scripts/dependency-report/` (`build-report.mjs` entry point, `lib/` collectors and renderer, `report-issue.sh` issue upsert). Tests: `node --test scripts/dependency-report/tests/*.test.mjs`; the workflow runs them before building the report.
- Local dry run (writes nothing to GitHub):

  ```bash
  mkdir -p /tmp/depreport
  dotnet restore src/AqtCoreFW.slnx
  dotnet list src/AqtCoreFW.slnx package --vulnerable --include-transitive --format json > /tmp/depreport/nuget-vulnerable.json
  (cd web && pnpm install --frozen-lockfile --ignore-scripts && pnpm outdated --format json > /tmp/depreport/pnpm-outdated.json; pnpm audit --json > /tmp/depreport/pnpm-audit.json)
  node scripts/dependency-report/build-report.mjs --repo-root . --inputs /tmp/depreport --out /tmp/depreport/issue-body.md
  ```

- Add a package to the licence watchlist by adding an entry to `license-watchlist.json` (`match`, optional `matchPrefix`, `kind` `commercial` with `commercialFrom`, or `watch` with a `note`). Check the vendor's licence page before adding or removing an entry.
- Permissions: the workflow uses only `GITHUB_TOKEN` with `contents: read` and `issues: write`. It creates the `dependency-report` label and pins the issue on first run; if pinning is refused, it logs a warning and the issue can be pinned by hand once.
- The report issue is recognised by the label plus the hidden marker `<!-- dependency-report:managed -->` at the top of its body. Other issues that reuse the label are ignored. If two marked issues are open, the next run keeps the oldest and closes the others with a comment.
- The workflow does not cover the Bitbucket `corebasev2` mirror or Jenkins; upgrades land on GitHub `main` first and reach Bitbucket the usual way.
