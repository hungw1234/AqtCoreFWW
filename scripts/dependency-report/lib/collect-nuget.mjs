// NuGet side of the dependency report.
// Reads the Central Package Management files directly (so transitive pins such as
// MessagePack are covered and each package is attributed to src/ or tests/), asks
// the public NuGet flat-container API for published versions, and folds in the
// `dotnet list package --vulnerable --include-transitive --format json` output.
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fetchJson, resolveUpdates } from './resolve-updates.mjs';

const FLAT_CONTAINER = 'https://api.nuget.org/v3-flatcontainer';
const REGISTRATION = 'https://api.nuget.org/v3/registration5-gz-semver2';
const SEVERITY_RANK = { critical: 4, high: 3, moderate: 2, low: 1 };

/** Extract <PackageVersion Include="X" Version="Y" /> entries from a Directory.Packages.props body. */
export function parsePackageVersions(xml) {
  const entries = [];
  const re = /<PackageVersion\s+([^>]*?)\/?>/g;
  for (const match of xml.matchAll(re)) {
    const id = match[1].match(/Include="([^"]+)"/)?.[1];
    const version = match[1].match(/Version="([^"]+)"/)?.[1];
    if (id && version) entries.push({ id, version });
  }
  return entries;
}

/** Packages declared in more than one props file with different versions. */
export function findVersionMismatches(propsFiles) {
  const byId = new Map();
  for (const file of propsFiles) {
    for (const pkg of file.packages) {
      const list = byId.get(pkg.id) ?? [];
      list.push({ file: file.file, version: pkg.version });
      byId.set(pkg.id, list);
    }
  }
  return [...byId.entries()]
    .filter(([, list]) => new Set(list.map((x) => x.version)).size > 1)
    .map(([id, declarations]) => ({ id, declarations }))
    .sort((a, b) => a.id.localeCompare(b.id));
}

/** Aggregate vulnerable packages from `dotnet list package --vulnerable --format json`. */
export function parseVulnerableJson(json) {
  if (!Array.isArray(json?.projects)) throw new Error('nuget-vulnerable.json thiếu mảng projects');
  const found = new Map();
  for (const project of json?.projects ?? []) {
    for (const framework of project.frameworks ?? []) {
      for (const kind of ['topLevelPackages', 'transitivePackages']) {
        for (const pkg of framework[kind] ?? []) {
          if (!pkg.vulnerabilities?.length) continue;
          const key = `${pkg.id}@${pkg.resolvedVersion}`;
          const entry = found.get(key) ?? {
            id: pkg.id,
            version: pkg.resolvedVersion,
            transitive: kind === 'transitivePackages',
            severity: 'low',
            advisories: new Set(),
            projects: new Set(),
          };
          for (const vuln of pkg.vulnerabilities) {
            const sev = String(vuln.severity ?? '').toLowerCase();
            if ((SEVERITY_RANK[sev] ?? 0) > (SEVERITY_RANK[entry.severity] ?? 0)) entry.severity = sev;
            if (vuln.advisoryurl) entry.advisories.add(vuln.advisoryurl);
          }
          entry.projects.add(path.basename(project.path ?? '', '.csproj'));
          found.set(key, entry);
        }
      }
    }
  }
  return [...found.values()]
    .map((e) => ({ ...e, advisories: [...e.advisories], projects: [...e.projects] }))
    .sort((a, b) => (SEVERITY_RANK[b.severity] ?? 0) - (SEVERITY_RANK[a.severity] ?? 0) || a.id.localeCompare(b.id));
}

/** Published versions of a package from the nuget.org flat-container index (includes unlisted ones). */
async function fetchNugetVersions(id, fetchImpl) {
  const { notFound, body } = await fetchJson(`${FLAT_CONTAINER}/${id.toLowerCase()}/index.json`, { fetchImpl });
  if (notFound) return { versions: [], error: 'không có trên nuget.org' };
  return { versions: body.versions ?? [] };
}

/** False when nuget.org marks the version unlisted (the author pulled it); a missing leaf counts as listed. */
async function isListedOnNuget(id, version, fetchImpl) {
  try {
    const leaf = `${REGISTRATION}/${id.toLowerCase()}/${version.toLowerCase()}.json`;
    const { notFound, body } = await fetchJson(leaf, { fetchImpl });
    return notFound || body.listed !== false;
  } catch {
    return true;
  }
}

/** Resolve latest versions for every package, skipping unlisted versions, with a small concurrency cap. */
export function resolveNugetUpdates(packages, { fetchImpl = fetch, concurrency = 8 } = {}) {
  return resolveUpdates(packages, (id) => fetchNugetVersions(id, fetchImpl), {
    confirm: (id, version) => isListedOnNuget(id, version, fetchImpl),
    concurrency,
  });
}

/** Messages from the root-level `problems` array of `dotnet list package --format json`, if any. */
export function nugetListProblems(json) {
  return (json?.problems ?? []).map((p) => `dotnet list package: ${p.text ?? p.message ?? JSON.stringify(p)}`);
}

/** Load every CPM props file plus dotnet-tools.json as a flat package list tagged with its source file. */
export async function loadNugetManifests(repoRoot, propsPaths, toolsPath) {
  const propsFiles = [];
  for (const rel of propsPaths) {
    const xml = await readFile(path.join(repoRoot, rel), 'utf8');
    propsFiles.push({ file: rel, packages: parsePackageVersions(xml) });
  }
  const packages = propsFiles.flatMap((f) => f.packages.map((p) => ({ ...p, file: f.file })));

  const tools = [];
  if (toolsPath) {
    const json = JSON.parse(await readFile(path.join(repoRoot, toolsPath), 'utf8'));
    for (const [id, tool] of Object.entries(json.tools ?? {})) {
      tools.push({ id, version: tool.version, file: toolsPath });
    }
  }
  return { propsFiles, packages, tools };
}
