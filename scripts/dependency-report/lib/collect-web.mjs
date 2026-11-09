// Frontend (web/, pnpm) side of the dependency report.
// `pnpm outdated --format json` supplies which packages are behind and their installed version;
// the published versions come from the npm registry and are classified with the same shared
// logic as NuGet (pnpm's `wanted` is not used: for exact pins it always equals the current version).
import { fetchJson, resolveUpdates } from './resolve-updates.mjs';
import { compareVersions, isPrerelease } from './version-utils.mjs';

const NPM_REGISTRY = 'https://registry.npmjs.org';
const SEVERITY_RANK = { critical: 4, high: 3, moderate: 2, low: 1, info: 0 };

/** Fail loudly when a pnpm command printed an error object instead of its normal payload. */
function assertNoPnpmError(json, label) {
  if (!json || typeof json !== 'object' || Array.isArray(json)) throw new Error(`${label}: không phải JSON object`);
  if (json.error) throw new Error(`${label}: ${json.error.summary ?? json.error.code ?? JSON.stringify(json.error)}`);
}

/** Packages pnpm reports as outdated or deprecated, before registry resolution. */
export function parsePnpmOutdated(json) {
  assertNoPnpmError(json, 'pnpm outdated');
  return Object.entries(json)
    .filter(([, info]) => info && typeof info === 'object' && info.current)
    .map(([id, info]) => ({
      id,
      version: info.current,
      deprecated: Boolean(info.isDeprecated),
      dev: info.dependencyType === 'devDependencies',
      file: 'web/package.json',
    }))
    .sort((a, b) => a.id.localeCompare(b.id));
}

/**
 * Published, non-deprecated versions of an npm package. Versions above the `latest` dist-tag are
 * dropped (they are next/beta channels or backport oddities), unless the project is already on a prerelease.
 */
async function fetchNpmVersions(id, current, fetchImpl) {
  const url = `${NPM_REGISTRY}/${id.replace('/', '%2f')}`;
  const { notFound, body } = await fetchJson(url, { fetchImpl, headers: { Accept: 'application/vnd.npm.install-v1+json' } });
  if (notFound) return { versions: [], error: 'không có trên npm registry' };
  const latestTag = body['dist-tags']?.latest;
  const versions = Object.entries(body.versions ?? {})
    .filter(([, meta]) => !meta?.deprecated)
    .map(([v]) => v)
    .filter((v) => isPrerelease(current) || !latestTag || compareVersions(v, latestTag) <= 0);
  return { versions };
}

/** Resolve latest and latest-compatible versions for the pnpm rows from the npm registry. */
export function resolveWebUpdates(rows, { fetchImpl = fetch, concurrency = 8 } = {}) {
  const currentById = new Map(rows.map((r) => [r.id, r.version]));
  return resolveUpdates(rows, (id) => fetchNpmVersions(id, currentById.get(id), fetchImpl), { concurrency });
}

/** Summarise `pnpm audit --json`: severity totals plus one row per advisory, worst first. */
export function parsePnpmAudit(json) {
  assertNoPnpmError(json, 'pnpm audit');
  if (!json.metadata?.vulnerabilities) throw new Error('pnpm audit: thiếu metadata.vulnerabilities');
  const totals = json.metadata.vulnerabilities;
  const advisories = Object.values(json.advisories ?? {}).map((adv) => {
    const paths = (adv.findings ?? []).flatMap((f) => f.paths ?? []);
    return {
      id: adv.module_name,
      severity: adv.severity,
      title: adv.title,
      vulnerable: adv.vulnerable_versions,
      patched: adv.patched_versions,
      url: adv.url,
      cves: adv.cves ?? [],
      pathCount: paths.length,
      examplePath: paths[0] ?? '',
    };
  });
  advisories.sort(
    (a, b) => (SEVERITY_RANK[b.severity] ?? 0) - (SEVERITY_RANK[a.severity] ?? 0) || a.id.localeCompare(b.id),
  );
  return { totals, advisories };
}
