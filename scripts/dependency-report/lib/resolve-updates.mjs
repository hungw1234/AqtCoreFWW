// Registry-agnostic update resolution shared by the NuGet and npm collectors, so both ecosystems
// classify updates with the same pickLatest/classifyUpdate logic.
import { pickLatest, classifyUpdate } from './version-utils.mjs';

const TIMEOUT_MS = 15000;

/** GET a JSON document with a per-request timeout and up to 3 attempts. Returns { notFound } on 404. */
export async function fetchJson(url, { fetchImpl = fetch, headers = {}, attempts = 3 } = {}) {
  let lastError;
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    try {
      const res = await fetchImpl(url, { headers, signal: AbortSignal.timeout(TIMEOUT_MS) });
      if (res.status === 404) return { notFound: true };
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      return { body: await res.json() };
    } catch (err) {
      lastError = err;
    }
  }
  throw lastError;
}

/** Run `fn` over `items` with at most `limit` calls in flight; results keep the input order. */
export async function mapWithConcurrency(items, limit, fn) {
  const results = new Array(items.length);
  let cursor = 0;
  async function worker() {
    while (cursor < items.length) {
      const index = cursor;
      cursor += 1;
      results[index] = await fn(items[index]);
    }
  }
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker));
  return results;
}

/**
 * Resolve latest versions for each package row.
 * - `fetchVersions(id)` returns { versions: string[], error?: string } and is called once per distinct id.
 * - `confirm(id, version)` (optional) returns false for a version that must not be offered, e.g. an
 *   unlisted NuGet version; the pick is then recomputed without it (bounded retries).
 */
export async function resolveUpdates(packages, fetchVersions, { confirm, concurrency = 8 } = {}) {
  const ids = [...new Set(packages.map((p) => p.id))];
  const fetched = await mapWithConcurrency(ids, concurrency, async (id) => {
    try {
      return await fetchVersions(id);
    } catch (err) {
      return { versions: [], error: err.message };
    }
  });
  const versionsById = new Map(ids.map((id, i) => [id, fetched[i]]));

  return mapWithConcurrency(packages, concurrency, async (pkg) => {
    const { versions = [], error } = versionsById.get(pkg.id) ?? {};
    let pool = versions;
    let pick = pickLatest(pkg.version, pool);
    for (let round = 0; confirm && round < 3; round += 1) {
      const rejected = [];
      for (const candidate of new Set([pick.latest, pick.latestSameMajor])) {
        if (candidate && candidate !== pkg.version && !(await confirm(pkg.id, candidate))) rejected.push(candidate);
      }
      if (!rejected.length) break;
      pool = pool.filter((v) => !rejected.includes(v));
      pick = pickLatest(pkg.version, pool);
    }
    return { ...pkg, ...pick, error, ...classifyUpdate(pkg.version, pick.latest, pick.latestSameMajor) };
  });
}
