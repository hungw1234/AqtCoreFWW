#!/usr/bin/env node
// Build the weekly dependency report for AqtCoreFW.
//
// Usage:
//   node scripts/dependency-report/build-report.mjs \
//     --repo-root . --inputs <dir> --out <issue-body.md> [--full-out <full.md>] [--json-out <data.json>]
//
// <dir> may contain the raw tool outputs produced by the workflow:
//   nuget-vulnerable.json  (dotnet list package --vulnerable --include-transitive --format json)
//   pnpm-outdated.json     (pnpm outdated --format json)
//   pnpm-audit.json        (pnpm audit --json)
// A missing or unreadable input is reported in the "Thu thập lỗi" section instead of failing the run,
// so the issue never silently claims "nothing to upgrade". Read-only: no file in the repo is modified.
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { fileURLToPath } from 'node:url';
import { loadNugetManifests, resolveNugetUpdates, findVersionMismatches, parseVulnerableJson, nugetListProblems } from './lib/collect-nuget.mjs';
import { parsePnpmOutdated, parsePnpmAudit, resolveWebUpdates } from './lib/collect-web.mjs';
import { evaluateSdk, fetchReleasesIndex, collectPins } from './lib/collect-pins.mjs';
import { evaluateLicenses } from './lib/evaluate-licenses.mjs';
import { renderReport, renderReportWithinLimit } from './lib/render-report.mjs';

const PROPS_FILES = ['src/Directory.Packages.props', 'tests/Directory.Packages.props'];
const TOOLS_FILE = 'dotnet-tools.json';
const GLOBAL_JSON = 'src/global.json';
const HERE = path.dirname(fileURLToPath(import.meta.url));

const { values: args } = parseArgs({
  options: {
    'repo-root': { type: 'string', default: '.' },
    inputs: { type: 'string', default: '.' },
    out: { type: 'string' },
    'full-out': { type: 'string' },
    'json-out': { type: 'string' },
    'issue-number': { type: 'string' },
  },
});
if (!args.out) {
  console.error('missing --out <file>');
  process.exit(2);
}

const errors = [];
const repoRoot = path.resolve(args['repo-root']);

/** Run one collection step; on failure record a readable error and fall back to `fallback`. */
async function attempt(label, fn, fallback) {
  try {
    return await fn();
  } catch (err) {
    errors.push(`${label}: ${err.message}`);
    return fallback;
  }
}

async function readJsonInput(name) {
  const text = await readFile(path.join(args.inputs, name), 'utf8');
  // Tool outputs can carry warnings before the JSON payload; start at the first brace.
  const start = text.indexOf('{');
  if (start < 0) throw new Error(`${name} không có JSON`);
  return JSON.parse(text.slice(start));
}

const manifests = await attempt('Đọc Directory.Packages.props / dotnet-tools.json', () => loadNugetManifests(repoRoot, PROPS_FILES, TOOLS_FILE), { propsFiles: [], packages: [], tools: [] });
const nuget = await attempt('Tra version trên nuget.org', () => resolveNugetUpdates(manifests.packages), []);
const tools = await attempt('Tra version dotnet tools', () => resolveNugetUpdates(manifests.tools), []);
for (const row of [...nuget, ...tools]) if (row.error) errors.push(`nuget.org ${row.id}: ${row.error}`);

const nugetVulns = await attempt('Đọc nuget-vulnerable.json', async () => {
  const json = await readJsonInput('nuget-vulnerable.json');
  errors.push(...nugetListProblems(json));
  return parseVulnerableJson(json);
}, []);
const webRows = await attempt('Đọc pnpm-outdated.json', async () => parsePnpmOutdated(await readJsonInput('pnpm-outdated.json')), []);
const web = await attempt('Tra version trên npm registry', () => resolveWebUpdates(webRows), []);
for (const row of web) if (row.error) errors.push(`npm registry ${row.id}: ${row.error}`);
const audit = await attempt('Đọc pnpm-audit.json', async () => parsePnpmAudit(await readJsonInput('pnpm-audit.json')), null);
const sdk = await attempt('Đọc .NET release index', async () => evaluateSdk(JSON.parse(await readFile(path.join(repoRoot, GLOBAL_JSON), 'utf8')), await fetchReleasesIndex()), null);
const pins = await attempt('Liệt kê Docker image / Actions', () => collectPins(repoRoot), { images: [], actions: [] });
const watchlist = JSON.parse(await readFile(path.join(HERE, 'license-watchlist.json'), 'utf8'));

const env = process.env;
const data = {
  meta: {
    generatedAt: new Date().toLocaleString('sv-SE', { timeZone: 'Asia/Ho_Chi_Minh' }) + ' (giờ VN)',
    commit: (env.GITHUB_SHA ?? '').slice(0, 9),
    runUrl: env.GITHUB_RUN_ID ? `${env.GITHUB_SERVER_URL}/${env.GITHUB_REPOSITORY}/actions/runs/${env.GITHUB_RUN_ID}` : '',
    issueNumber: args['issue-number'] || null,
  },
  nuget,
  tools,
  mismatches: findVersionMismatches(manifests.propsFiles),
  nugetVulns,
  web,
  audit,
  sdk,
  pins,
  licenses: evaluateLicenses(watchlist, nuget),
  errors,
};

await writeFile(args.out, renderReportWithinLimit(data));
if (args['full-out']) await writeFile(args['full-out'], renderReport(data, { limit: Infinity }));
if (args['json-out']) await writeFile(args['json-out'], JSON.stringify(data, null, 2));

console.log(`dependency report: ${nuget.length} NuGet, ${tools.length} tools, ${web.length} web outdated, ${nugetVulns.length} NuGet CVE, ${audit?.advisories.length ?? 0} web advisories, ${errors.length} collection errors`);
