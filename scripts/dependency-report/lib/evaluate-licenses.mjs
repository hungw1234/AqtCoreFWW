// Match the license watchlist against the packages in use and decide each package's status.
import { atLeast } from './version-utils.mjs';

function matches(rule, id) {
  return rule.matchPrefix ? id === rule.match || id.startsWith(`${rule.match}.`) : id === rule.match;
}

/**
 * Returns one row per (rule, package in use):
 *  - "commercial-in-use": the version in use is already under the commercial licence;
 *  - "frozen": still on the last free major, but the newer majors are commercial, so
 *    security patches will stop; must be replaced;
 *  - "watch": free under conditions that need periodic re-checking.
 */
export function evaluateLicenses(watchlist, nugetRows) {
  const results = [];
  for (const rule of watchlist?.packages ?? []) {
    const inUse = nugetRows.filter((row) => matches(rule, row.id));
    for (const row of inUse) {
      let status = 'watch';
      if (rule.kind === 'commercial') {
        status = atLeast(row.version, rule.commercialFrom) ? 'commercial-in-use' : 'frozen';
      }
      results.push({
        id: row.id,
        file: row.file,
        version: row.version,
        latest: row.latest,
        status,
        commercialFrom: rule.commercialFrom ?? null,
        licenseUrl: rule.licenseUrl,
        replacements: rule.replacements ?? [],
        note: rule.note ?? '',
      });
    }
  }
  const order = { 'commercial-in-use': 0, frozen: 1, watch: 2 };
  return results.sort((a, b) => order[a.status] - order[b.status] || a.id.localeCompare(b.id));
}
