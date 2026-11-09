// Inventory of pinned platform pieces that are listed (not version-compared) in the report:
// .NET SDK from global.json (compared against the official release index), Docker base
// images and service images, and GitHub Actions references.
import { execFileSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { compareVersions } from './version-utils.mjs';

const RELEASES_INDEX = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json';

/** Compare the global.json SDK pin with the newest SDK of its channel and the newest supported channel. */
export function evaluateSdk(globalJson, releasesIndex) {
  const pinned = globalJson?.sdk?.version ?? null;
  const rollForward = globalJson?.sdk?.rollForward ?? 'patch';
  const channels = releasesIndex?.['releases-index'] ?? [];
  const channelOf = (v) => (v ? v.split('.').slice(0, 2).join('.') : null);
  const own = channels.find((c) => c['channel-version'] === channelOf(pinned));
  const supported = channels
    .filter((c) => ['active', 'maintenance'].includes(c['support-phase']))
    .sort((a, b) => compareVersions(b['channel-version'], a['channel-version']))[0];
  return {
    pinned,
    rollForward,
    channel: own?.['channel-version'] ?? channelOf(pinned),
    channelLatestSdk: own?.['latest-sdk'] ?? null,
    channelLatestRuntime: own?.['latest-runtime'] ?? null,
    channelSecurityRelease: Boolean(own?.security),
    channelSupportPhase: own?.['support-phase'] ?? null,
    channelEol: own?.['eol-date'] ?? null,
    newestSupportedChannel: supported?.['channel-version'] ?? null,
    outdated: Boolean(pinned && own && compareVersions(own['latest-sdk'], pinned) > 0),
  };
}

export async function fetchReleasesIndex(fetchImpl = fetch) {
  const res = await fetchImpl(RELEASES_INDEX);
  if (!res.ok) throw new Error(`releases-index HTTP ${res.status}`);
  return res.json();
}

/** Extract image references and action references from one file's text. */
export function extractPins(file, text) {
  const images = [];
  const actions = [];
  const stageNames = new Set();
  const isDockerfile = /(^|\/)Dockerfile[^/]*$/.test(file);
  text.split(/\r?\n/).forEach((line, index) => {
    const at = `${file}:${index + 1}`;
    if (isDockerfile) {
      const from = line.match(/^\s*FROM\s+(?:--platform=\S+\s+)?(\S+)(?:\s+AS\s+(\S+))?/i);
      if (from) {
        if (!stageNames.has(from[1])) images.push({ ref: from[1], at });
        if (from[2]) stageNames.add(from[2]);
      }
      return;
    }
    const image = line.match(/^\s*image:\s*["']?([^"'\s#]+)/);
    if (image && !image[1].includes('${{')) images.push({ ref: image[1], at });
    const uses = line.match(/^\s*-?\s*uses:\s*["']?([^"'\s#]+)/);
    if (uses && !uses[1].startsWith('./')) actions.push({ ref: uses[1], at });
  });
  return { images, actions };
}

/** Group identical references so each image/action appears once with every location it is used. */
function groupByRef(items) {
  const map = new Map();
  for (const { ref, at } of items) map.set(ref, [...(map.get(ref) ?? []), at]);
  return [...map.entries()].map(([ref, locations]) => ({ ref, locations })).sort((a, b) => a.ref.localeCompare(b.ref));
}

/** Scan tracked Dockerfiles, compose files and workflows for image and action pins. */
export async function collectPins(repoRoot) {
  const tracked = execFileSync('git', ['ls-files'], { cwd: repoRoot, encoding: 'utf8' }).split('\n');
  const targets = tracked.filter(
    (f) =>
      /(^|\/)Dockerfile[^/]*$/.test(f) ||
      /(^|\/)(docker-)?compose[^/]*\.ya?ml$/.test(f) ||
      /^\.github\/workflows\/[^/]+\.ya?ml$/.test(f),
  );
  const images = [];
  const actions = [];
  for (const file of targets) {
    const found = extractPins(file, await readFile(path.join(repoRoot, file), 'utf8'));
    images.push(...found.images);
    actions.push(...found.actions);
  }
  return { images: groupByRef(images), actions: groupByRef(actions) };
}
