// Render the collected data as the Markdown body of the pinned "Dependency report" issue.
// Output is Vietnamese because the report is read by the team; package ids stay verbatim.
import { compareVersions, majorOf } from './version-utils.mjs';

export const REPORT_MARKER = '<!-- dependency-report:managed -->';
export const MAX_BODY_CHARS = 60000; // GitHub caps issue bodies at 65536 characters.

const esc = (v) => String(v ?? '').replace(/\|/g, '\\|');
const code = (v) => (v ? `\`${esc(v)}\`` : '-');

function table(headers, rows, limit) {
  if (!rows.length) return '_Không có._\n';
  const shown = rows.slice(0, limit);
  const lines = [`| ${headers.join(' | ')} |`, `|${headers.map(() => '---').join('|')}|`];
  for (const row of shown) lines.push(`| ${row.join(' | ')} |`);
  if (rows.length > shown.length) lines.push(`\n_...và ${rows.length - shown.length} dòng nữa (xem artifact của run)._`);
  return `${lines.join('\n')}\n`;
}

/** "src/Directory.Packages.props" -> "src"; other manifest paths stay as they are. */
const shortFile = (f) => String(f).replace(/\/Directory\.Packages\.props$/, '');

/** Merge rows that share id + version (+ latest) so a package declared in both props files shows once. */
export function mergeByPackage(rows, keyFields = ['id', 'version', 'latest', 'latestSameMajor', 'status']) {
  const map = new Map();
  for (const row of rows) {
    const key = keyFields.map((k) => row[k] ?? '').join('|');
    const existing = map.get(key);
    if (existing) existing.files.push(shortFile(row.file));
    else map.set(key, { ...row, files: [shortFile(row.file)] });
  }
  return [...map.values()];
}

/** One row per vulnerable web package: worst severity, every advisory link, the highest patched floor. */
export function groupAdvisoriesByPackage(advisories) {
  const rank = { critical: 4, high: 3, moderate: 2, low: 1, info: 0 };
  const map = new Map();
  for (const a of advisories) {
    const g = map.get(a.id) ?? { id: a.id, severity: a.severity, urls: [], patched: new Set(), examplePath: a.examplePath };
    if ((rank[a.severity] ?? 0) > (rank[g.severity] ?? 0)) g.severity = a.severity;
    g.urls.push(a.url);
    g.patched.add(a.patched);
    map.set(a.id, g);
  }
  return [...map.values()]
    .map((g) => ({ ...g, urls: [...new Set(g.urls)], patched: highestFloorPerMajor([...g.patched]) }))
    .sort((a, b) => (rank[b.severity] ?? 0) - (rank[a.severity] ?? 0) || b.urls.length - a.urls.length);
}

/** Reduce ">=1.1.16", ">=1.1.20", ">=5.0.7" to the highest floor per major: ">=1.1.20", ">=5.0.7". */
export function highestFloorPerMajor(ranges) {
  const floors = new Map();
  const others = [];
  for (const r of ranges) {
    const m = /^>=\s*(\S+)$/.exec(r ?? '');
    const major = m ? majorOf(m[1]) : null;
    if (major === null) {
      others.push(r);
      continue;
    }
    const prev = floors.get(major);
    if (!prev || compareVersions(m[1], prev) > 0) floors.set(major, m[1]);
  }
  return [...[...floors.entries()].sort((a, b) => a[0] - b[0]).map(([, v]) => `>=${v}`), ...new Set(others)];
}

const STATUS_LABEL = {
  'commercial-in-use': '🚨 Đang dùng bản thương mại',
  frozen: '⛔ Kẹt ở bản miễn phí cuối, sẽ hết bản vá',
  watch: '👀 Theo dõi điều kiện license',
};

function renderLicenses(licenses) {
  if (!licenses.length) return '_Không có gói nào trong danh sách theo dõi license._\n';
  const groups = new Map();
  for (const l of mergeByPackage(licenses)) {
    const key = `${l.status}|${l.licenseUrl}|${l.version}`;
    const g = groups.get(key) ?? { ...l, ids: [], fileSet: new Set() };
    g.ids.push(l.id);
    l.files.forEach((f) => g.fileSet.add(f));
    groups.set(key, g);
  }
  return [...groups.values()]
    .map((l) => {
      const head = `- **${l.ids.join(', ')}** ${code(l.version)} (${[...l.fileSet].join(', ')}): ${STATUS_LABEL[l.status]}`;
      const from = l.commercialFrom ? ` Bản thương mại từ ${code(l.commercialFrom)}, mới nhất ${code(l.latest)}.` : '';
      const repl = l.replacements.length ? `\n  - Phương án thay: ${l.replacements.join('; ')}` : '';
      const note = l.note ? `\n  - ${l.note}` : '';
      return `${head}.${from} [Điều khoản](${l.licenseUrl})${repl}${note}`;
    })
    .join('\n')
    .concat('\n');
}

function updateRows(rows, pick, licenseIds) {
  return mergeByPackage(rows.filter(pick)).map((r) => [
    `${code(r.id)}${licenseIds.has(r.id) ? ' ⚠️ license' : ''}${r.deprecated ? ' (deprecated)' : ''}`,
    esc(r.files.join(', ')),
    code(r.version),
    code(pick === isMajor ? r.latest : r.latestSameMajor),
  ]);
}
const isMajor = (r) => r.hasMajor;
const isMinor = (r) => r.hasMinor;

/** Build the full issue body. `limit` caps rows per table; the caller shrinks it if the body is too long. */
export function renderReport(data, { limit = 60 } = {}) {
  const { meta, nuget, tools, mismatches, nugetVulns, web, audit, sdk, pins, licenses, errors } = data;
  const licenseIds = new Set(licenses.filter((l) => l.status !== 'watch').map((l) => l.id));
  const allUpdates = [...nuget, ...tools, ...web];
  const auditTotals = audit?.totals ?? {};
  const severe = groupAdvisoriesByPackage((audit?.advisories ?? []).filter((a) => ['critical', 'high'].includes(a.severity)));
  const out = [];

  out.push(REPORT_MARKER);
  out.push(`# Dependency report\n`);
  out.push(`Cập nhật: **${meta.generatedAt}** · commit ${code(meta.commit)}${meta.runUrl ? ` · [run](${meta.runUrl})` : ''}\n`);
  out.push('Báo cáo tự động, chỉ đọc: workflow không sửa code, không mở PR. Cách giao việc ở cuối issue.\n');

  if (errors.length) {
    out.push(`## ⚠️ Thu thập lỗi (${errors.length})\n`);
    out.push('Các mục dưới có thể thiếu dữ liệu, đừng coi là "không có gì cần nâng".\n');
    out.push(errors.map((e) => `- ${e}`).join('\n') + '\n');
  }

  out.push('## Tóm tắt\n');
  out.push(
    table(
      ['Mục', 'Số lượng'],
      [
        ['🔴 CVE NuGet (gói)', nugetVulns.length],
        ['🔴 CVE web (critical / high / moderate / low)', `${auditTotals.critical ?? 0} / ${auditTotals.high ?? 0} / ${auditTotals.moderate ?? 0} / ${auditTotals.low ?? 0}`],
        ['🔴 Gói web có CVE critical/high', severe.length],
        ['🟠 Gói license cần thay', new Set(licenses.filter((l) => l.status !== 'watch').map((l) => l.id)).size],
        ['🟣 Lệch version giữa các tệp props', mismatches.length],
        ['🟡 Có bản major mới (gói)', mergeByPackage(allUpdates.filter(isMajor)).length],
        ['🟢 Có bản minor/patch mới (gói)', mergeByPackage(allUpdates.filter(isMinor)).length],
        ['.NET SDK', sdk ? (sdk.outdated ? `${sdk.pinned} → ${sdk.channelLatestSdk}` : `${sdk.pinned} (mới nhất kênh)`) : 'không đọc được'],
      ],
      limit,
    ),
  );

  out.push('## 🔴 CVE (ưu tiên 1)\n');
  out.push('### NuGet (`dotnet list package --vulnerable --include-transitive`)\n');
  out.push(table(['Gói', 'Version', 'Mức', 'Transitive', 'Advisory', 'Số project'], nugetVulns.map((v) => [code(v.id), code(v.version), v.severity, v.transitive ? 'có' : 'không', v.advisories.map((u, i) => `[${i + 1}](${u})`).join(' '), v.projects.length]), limit));
  out.push('### web (`pnpm audit`), critical và high\n');
  out.push(table(['Gói', 'Mức cao nhất', 'Advisory', 'Bản vá cần', 'Kéo vào qua'], severe.map((g) => [code(g.id), g.severity, g.urls.map((u, i) => `[${i + 1}](${u})`).join(' '), g.patched.map(code).join(', '), code(g.examplePath)]), limit));
  out.push('_Mức moderate và low chỉ đếm ở phần tóm tắt; chạy `pnpm audit` trong `web/` để xem chi tiết._\n');

  out.push('## 🟠 License đổi sang thương mại (cần thay thế)\n');
  out.push(renderLicenses(licenses));

  out.push('## 🟣 Lệch version giữa các tệp props\n');
  out.push(table(['Gói', 'Khai báo'], mismatches.map((m) => [code(m.id), m.declarations.map((d) => `${esc(d.file)}: ${code(d.version)}`).join('<br>')]), limit));

  out.push('## 🟡 Major\n');
  out.push(table(['Gói', 'Tệp', 'Đang dùng', 'Mới nhất'], updateRows(allUpdates, isMajor, licenseIds), limit));
  if (sdk && sdk.newestSupportedChannel && sdk.newestSupportedChannel !== sdk.channel) {
    out.push(`\n.NET: đang ở kênh ${code(sdk.channel)}, kênh mới nhất còn hỗ trợ là ${code(sdk.newestSupportedChannel)}.\n`);
  }

  out.push('## 🟢 Minor / patch\n');
  out.push(table(['Gói', 'Tệp', 'Đang dùng', 'Mới nhất cùng major'], updateRows(allUpdates, isMinor, licenseIds), limit));
  if (sdk) {
    const sec = sdk.channelSecurityRelease ? ' Bản mới nhất của kênh **có bản vá bảo mật**.' : '';
    out.push(`\n.NET SDK (\`src/global.json\`): ghim ${code(sdk.pinned)} (rollForward ${code(sdk.rollForward)}), mới nhất kênh ${code(sdk.channel)} là SDK ${code(sdk.channelLatestSdk)} / runtime ${code(sdk.channelLatestRuntime)}.${sec}\n`);
  }

  const deprecated = web.filter((r) => r.deprecated);
  if (deprecated.length) {
    out.push('## ⚪ Gói web bị đánh dấu deprecated\n');
    out.push(deprecated.map((r) => `- ${code(r.id)} ${code(r.version)}${r.dev ? ' (dev)' : ''}`).join('\n') + '\n');
  }

  out.push('## 📌 Docker image và GitHub Actions đang ghim (chỉ liệt kê)\n');
  out.push('<details><summary>Docker image (' + pins.images.length + ')</summary>\n\n');
  out.push(table(['Image', 'Dùng ở'], pins.images.map((p) => [code(p.ref), p.locations.map(esc).join('<br>')]), limit));
  out.push('</details>\n\n<details><summary>GitHub Actions (' + pins.actions.length + ')</summary>\n\n');
  out.push(table(['Action', 'Dùng ở'], pins.actions.map((p) => [code(p.ref), p.locations.map(esc).join('<br>')]), limit));
  out.push('</details>\n');

  out.push('## Giao việc cho Claude\n');
  out.push([
    'Đọc issue này rồi giao một lệnh, mỗi nhóm một PR, CI phải xanh:',
    `- CVE: \`/ak:cook nâng các gói mục 🔴 trong issue #${meta.issueNumber ?? 'N'}, một PR\``,
    `- Minor/patch: \`/ak:cook nâng các gói mục 🟢 trong issue #${meta.issueNumber ?? 'N'}, một PR\``,
    '- Major: mỗi gói một PR, đọc release notes trước: `/ak:cook nâng <gói> lên <version> theo issue dependency report`',
    '- License: mở plan thay thế riêng, không nâng gói trong mục 🟠.',
    '',
    'Hướng dẫn đầy đủ: `docs/ops/dependency-report-runbook.md`.',
  ].join('\n') + '\n');

  return out.join('\n');
}

/** Render, shrinking per-table row limits until the body fits GitHub's size cap. */
export function renderReportWithinLimit(data) {
  for (const limit of [60, 30, 15, 5]) {
    const body = renderReport(data, { limit });
    if (body.length <= MAX_BODY_CHARS) return body;
  }
  return renderReport(data, { limit: 5 }).slice(0, MAX_BODY_CHARS - 200) + '\n\n_Báo cáo bị cắt do vượt giới hạn độ dài issue._\n';
}
