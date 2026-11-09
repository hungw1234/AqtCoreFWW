// Unit tests for the dependency report builder. Run: node --test scripts/dependency-report/tests/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { compareVersions, pickLatest, classifyUpdate, compatKey } from '../lib/version-utils.mjs';
import { parsePackageVersions, findVersionMismatches, parseVulnerableJson, resolveNugetUpdates } from '../lib/collect-nuget.mjs';
import { parsePnpmOutdated, parsePnpmAudit, resolveWebUpdates } from '../lib/collect-web.mjs';
import { resolveUpdates } from '../lib/resolve-updates.mjs';
import { extractPins, evaluateSdk } from '../lib/collect-pins.mjs';
import { evaluateLicenses } from '../lib/evaluate-licenses.mjs';
import { renderReport, renderReportWithinLimit, groupAdvisoriesByPackage, highestFloorPerMajor, mergeByPackage, REPORT_MARKER, MAX_BODY_CHARS } from '../lib/render-report.mjs';

test('compareVersions handles 4-part NuGet versions, prereleases and range prefixes', () => {
  assert.equal(compareVersions('1.8.1.2', '1.8.1'), 1);
  assert.equal(compareVersions('1.15.1-beta.1', '1.15.1'), -1);
  assert.equal(compareVersions('^5.7.0', '5.10.0'), -1);
  assert.equal(compareVersions('10.0.3', '10.0.3'), 0);
});

test('pickLatest ignores prereleases unless the current version is one, and finds the same-major latest', () => {
  const available = ['12.5.0', '12.6.1', '13.0.0', '14.0.0-preview.1', '14.2.0'];
  assert.deepEqual(pickLatest('12.5.0', available), { latest: '14.2.0', latestSameMajor: '12.6.1' });
  assert.equal(pickLatest('1.15.1-beta.1', ['1.15.1-beta.1', '1.16.0-beta.1']).latest, '1.16.0-beta.1');
  assert.deepEqual(classifyUpdate('12.5.0', '14.2.0', '12.6.1'), { hasMajor: true, hasMinor: true });
  assert.deepEqual(classifyUpdate('14.2.0', '14.2.0', '14.2.0'), { hasMajor: false, hasMinor: false });
});

test('parsePackageVersions and findVersionMismatches flag a package pinned differently in src and tests', () => {
  const src = parsePackageVersions('<PackageVersion Include="Testcontainers.MsSql" Version="4.4.0" />\n<PackageVersion Include="Dapper" Version="2.1.66"/>');
  const tests = parsePackageVersions('<PackageVersion Include="Testcontainers.MsSql" Version="3.10.0" />\n<PackageVersion Include="Dapper" Version="2.1.66" />');
  assert.equal(src.length, 2);
  const mismatches = findVersionMismatches([
    { file: 'src/Directory.Packages.props', packages: src },
    { file: 'tests/Directory.Packages.props', packages: tests },
  ]);
  assert.deepEqual(mismatches.map((m) => m.id), ['Testcontainers.MsSql']);
});

test('parseVulnerableJson aggregates per package version and keeps the worst severity', () => {
  const json = {
    projects: [
      { path: '/r/A.csproj', frameworks: [{ transitivePackages: [{ id: 'X', resolvedVersion: '1.0.0', vulnerabilities: [{ severity: 'Moderate', advisoryurl: 'u1' }] }] }] },
      { path: '/r/B.csproj', frameworks: [{ topLevelPackages: [{ id: 'X', resolvedVersion: '1.0.0', vulnerabilities: [{ severity: 'High', advisoryurl: 'u2' }] }] }] },
      { path: '/r/C.csproj' },
    ],
  };
  const [row] = parseVulnerableJson(json);
  assert.equal(row.severity, 'high');
  assert.deepEqual(row.projects.sort(), ['A', 'B']);
  assert.deepEqual(row.advisories.sort(), ['u1', 'u2']);
});

test('resolveNugetUpdates records nuget.org failures instead of throwing (fetch double, no network)', async () => {
  const fakeFetch = async (url) => {
    if (url.includes('/registration5-gz-semver2/')) return { status: 200, ok: true, json: async () => ({ listed: true }) };
    if (url.includes('/missing/')) return { status: 404, ok: false };
    if (url.includes('/broken/')) throw new Error('network down');
    return { status: 200, ok: true, json: async () => ({ versions: ['1.0.0', '1.1.0', '2.0.0'] }) };
  };
  const rows = await resolveNugetUpdates(
    [{ id: 'Good', version: '1.0.0' }, { id: 'Missing', version: '1.0.0' }, { id: 'Broken', version: '1.0.0' }],
    { fetchImpl: fakeFetch },
  );
  const byId = Object.fromEntries(rows.map((r) => [r.id, r]));
  assert.equal(byId.Good.latest, '2.0.0');
  assert.equal(byId.Good.latestSameMajor, '1.1.0');
  assert.match(byId.Missing.error, /nuget\.org/);
  assert.equal(byId.Broken.error, 'network down');
});

/** Minimal fetch double: maps URL substrings to JSON bodies; "404" yields a not-found response. */
function fakeFetch(routes) {
  return async (url) => {
    const hit = Object.entries(routes).find(([key]) => url.includes(key));
    if (!hit) throw new Error(`unexpected url ${url}`);
    if (hit[1] === 404) return { status: 404, ok: false };
    return { status: 200, ok: true, json: async () => hit[1] };
  };
}

test('compatKey treats 0.x minor bumps as breaking and prereleases compare numerically', () => {
  assert.equal(compatKey('2.3.4'), '2');
  assert.equal(compatKey('0.4.26'), '0.4');
  assert.equal(compatKey('0.0.3'), '0.0.3');
  assert.deepEqual(classifyUpdate('0.4.26', '0.5.7', '0.4.26'), { hasMajor: true, hasMinor: false });
  assert.equal(compareVersions('1.0.0-beta.10', '1.0.0-beta.9'), 1);
  assert.equal(compareVersions('1.0.0-beta', '1.0.0-beta.1'), -1);
  assert.equal(compareVersions('1.0.0-1', '1.0.0-alpha'), -1);
});

test('parsePnpmOutdated keeps installed packages and rejects pnpm error payloads', () => {
  const rows = parsePnpmOutdated({
    vite: { current: '6.0.1', latest: '7.1.0', wanted: '6.4.4', dependencyType: 'devDependencies' },
    '@types/dompurify': { current: '3.2.0', latest: '3.2.0', wanted: '3.2.0', isDeprecated: true },
    'not-installed': { latest: '1.0.0' },
  });
  assert.deepEqual(rows.map((r) => r.id), ['@types/dompurify', 'vite']);
  assert.equal(rows[0].deprecated, true);
  assert.equal(rows[1].dev, true);
  assert.throws(() => parsePnpmOutdated({ error: { code: 'ERR_PNPM_X', summary: 'boom' } }), /boom/);
});

test('resolveWebUpdates offers the same-major release for an exact pin, skips deprecated and post-latest versions', async () => {
  const fetchImpl = fakeFetch({
    'pdfjs-dist': {
      'dist-tags': { latest: '6.5.438' },
      versions: { '5.4.296': {}, '5.7.284': {}, '5.8.0': { deprecated: 'broken' }, '6.5.438': {}, '7.0.0': {} },
    },
    '@scope%2fpkg': 404,
  });
  const rows = await resolveWebUpdates(
    [{ id: 'pdfjs-dist', version: '5.4.296' }, { id: '@scope/pkg', version: '1.0.0' }],
    { fetchImpl },
  );
  assert.deepEqual(
    [rows[0].latest, rows[0].latestSameMajor, rows[0].hasMajor, rows[0].hasMinor],
    ['6.5.438', '5.7.284', true, true],
  );
  assert.match(rows[1].error, /npm registry/);
});

test('resolveUpdates drops versions rejected by confirm (unlisted) and repicks', async () => {
  const rows = await resolveUpdates(
    [{ id: 'X', version: '1.0.0' }],
    async () => ({ versions: ['1.0.0', '1.1.0', '1.2.0', '2.0.0'] }),
    { confirm: async (_id, v) => v !== '1.2.0' && v !== '2.0.0' },
  );
  assert.deepEqual([rows[0].latest, rows[0].latestSameMajor, rows[0].hasMajor], ['1.1.0', '1.1.0', false]);
});

test('parsePnpmAudit and parseVulnerableJson refuse payloads that would read as "nothing found"', () => {
  assert.throws(() => parsePnpmAudit({ error: { code: 'ERR_PNPM_AUDIT_BAD_RESPONSE' } }), /AUDIT_BAD_RESPONSE/);
  assert.throws(() => parsePnpmAudit({ advisories: {} }), /metadata/);
  assert.throws(() => parseVulnerableJson({}), /projects/);
});

test('web advisories group per package with deduplicated links and the highest patched floor per major', () => {
  const { advisories } = parsePnpmAudit({
    metadata: { vulnerabilities: { high: 2, critical: 1 } },
    advisories: {
      1: { module_name: 'brace-expansion', severity: 'high', url: 'a', patched_versions: '>=1.1.16', findings: [{ paths: ['.>eslint'] }] },
      2: { module_name: 'brace-expansion', severity: 'high', url: 'a', patched_versions: '>=5.0.7', findings: [] },
      3: { module_name: 'brace-expansion', severity: 'critical', url: 'b', patched_versions: '>=1.1.20', findings: [] },
    },
  });
  const [group] = groupAdvisoriesByPackage(advisories);
  assert.equal(group.severity, 'critical');
  assert.deepEqual([...group.urls].sort(), ['a', 'b']);
  assert.deepEqual(group.patched, ['>=1.1.20', '>=5.0.7']);
  assert.deepEqual(highestFloorPerMajor(['<0.0.0']), ['<0.0.0']);
});

test('evaluateLicenses distinguishes commercial-in-use, frozen and watch, matching prefixes', () => {
  const watchlist = {
    packages: [
      { match: 'MediatR', kind: 'commercial', commercialFrom: '13.0.0' },
      { match: 'MassTransit', matchPrefix: true, kind: 'commercial', commercialFrom: '9.0.0' },
      { match: 'FluentAssertions', kind: 'commercial', commercialFrom: '8.0.0' },
      { match: 'QuestPDF', kind: 'watch' },
    ],
  };
  const rows = [
    { id: 'MediatR', version: '12.5.0', file: 'src' },
    { id: 'MassTransit.RabbitMQ', version: '8.3.6', file: 'src' },
    { id: 'MassTransitLike', version: '1.0.0', file: 'src' },
    { id: 'FluentAssertions', version: '8.2.0', file: 'tests' },
    { id: 'QuestPDF', version: '2025.4.0', file: 'src' },
  ];
  const status = Object.fromEntries(evaluateLicenses(watchlist, rows).map((r) => [r.id, r.status]));
  assert.deepEqual(status, { FluentAssertions: 'commercial-in-use', 'MassTransit.RabbitMQ': 'frozen', MediatR: 'frozen', QuestPDF: 'watch' });
});

test('extractPins skips multi-stage references and local actions', () => {
  const docker = extractPins('web/Dockerfile', 'FROM node:22-alpine AS build\nFROM build AS test\nFROM nginx:1.27-alpine AS serve');
  assert.deepEqual(docker.images.map((i) => i.ref), ['node:22-alpine', 'nginx:1.27-alpine']);
  const wf = extractPins('.github/workflows/ci.yml', '      - uses: actions/checkout@v4\n      - uses: ./.github/actions/local\n    image: redis:7-alpine');
  assert.deepEqual(wf.actions.map((a) => a.ref), ['actions/checkout@v4']);
  assert.deepEqual(wf.images.map((i) => i.ref), ['redis:7-alpine']);
});

test('evaluateSdk compares the global.json pin with its channel', () => {
  const index = {
    'releases-index': [
      { 'channel-version': '11.0', 'latest-sdk': '11.0.100-rc.3', 'support-phase': 'go-live' },
      { 'channel-version': '10.0', 'latest-sdk': '10.0.403', 'latest-runtime': '10.0.14', security: true, 'support-phase': 'active' },
    ],
  };
  const sdk = evaluateSdk({ sdk: { version: '10.0.103', rollForward: 'latestMinor' } }, index);
  assert.equal(sdk.outdated, true);
  assert.equal(sdk.channelLatestSdk, '10.0.403');
  assert.equal(sdk.newestSupportedChannel, '10.0');
});

function sampleData(overrides = {}) {
  return {
    meta: { generatedAt: 'now', commit: 'abc', runUrl: '', issueNumber: '7' },
    nuget: [
      { id: 'Dapper', version: '2.1.66', latest: '2.1.89', latestSameMajor: '2.1.89', hasMajor: false, hasMinor: true, file: 'src/Directory.Packages.props' },
      { id: 'Dapper', version: '2.1.66', latest: '2.1.89', latestSameMajor: '2.1.89', hasMajor: false, hasMinor: true, file: 'tests/Directory.Packages.props' },
    ],
    tools: [],
    mismatches: [],
    nugetVulns: [],
    web: [],
    audit: { totals: { critical: 0, high: 0 }, advisories: [] },
    sdk: null,
    pins: { images: [], actions: [] },
    licenses: [],
    errors: [],
    ...overrides,
  };
}

test('renderReport merges duplicate props rows and surfaces collection errors', () => {
  const body = renderReport(sampleData({ errors: ['Đọc pnpm-audit.json: ENOENT'] }));
  assert.ok(body.startsWith(REPORT_MARKER));
  assert.match(body, /Thu thập lỗi \(1\)/);
  assert.match(body, /\| `Dapper` \| src, tests \|/);
  assert.match(body, /issue #7/);
  assert.equal(mergeByPackage(sampleData().nuget).length, 1);
});

test('renderReportWithinLimit keeps the body under the GitHub issue size cap', () => {
  const many = Array.from({ length: 3000 }, (_, i) => ({
    id: `Package.With.A.Rather.Long.Name.${i}`, version: '1.0.0', latest: '2.0.0', latestSameMajor: '1.1.0', hasMajor: true, hasMinor: true, file: 'src/Directory.Packages.props',
  }));
  const body = renderReportWithinLimit(sampleData({ nuget: many }));
  assert.ok(body.length <= MAX_BODY_CHARS);
});
