// Version parsing and classification shared by the NuGet, pnpm and SDK collectors.
// Handles NuGet 4-part versions (1.8.1.2), SemVer prereleases (1.15.1-beta.1)
// and npm range prefixes (^5.7.0, ~1.2.3) without pulling in a semver package.

/** Parse "1.2.3-beta.1" into { parts: [1,2,3], pre: "beta.1" }; returns null when not numeric. */
export function parseVersion(raw) {
  if (typeof raw !== 'string') return null;
  const cleaned = raw.trim().replace(/^[\^~=v]+/, '');
  const match = cleaned.match(/^(\d+(?:\.\d+)*)(?:-([0-9A-Za-z.-]+))?(?:\+.*)?$/);
  if (!match) return null;
  return { parts: match[1].split('.').map(Number), pre: match[2] ?? '' };
}

export function isPrerelease(raw) {
  const v = parseVersion(raw);
  return Boolean(v && v.pre);
}

/** Compare two version strings; unparsable values sort first so they never win "latest". */
export function compareVersions(a, b) {
  const va = parseVersion(a);
  const vb = parseVersion(b);
  if (!va || !vb) return (va ? 1 : 0) - (vb ? 1 : 0);
  const len = Math.max(va.parts.length, vb.parts.length);
  for (let i = 0; i < len; i += 1) {
    const diff = (va.parts[i] ?? 0) - (vb.parts[i] ?? 0);
    if (diff !== 0) return Math.sign(diff);
  }
  // A release outranks any prerelease of the same numeric version.
  if (va.pre === vb.pre) return 0;
  if (!va.pre) return 1;
  if (!vb.pre) return -1;
  return comparePrerelease(va.pre, vb.pre);
}

/** SemVer 2.0 section 11: dot-separated identifiers, numeric ones compared numerically and ranked below text. */
function comparePrerelease(a, b) {
  const pa = a.split('.');
  const pb = b.split('.');
  for (let i = 0; i < Math.max(pa.length, pb.length); i += 1) {
    if (pa[i] === undefined) return -1;
    if (pb[i] === undefined) return 1;
    const na = /^\d+$/.test(pa[i]);
    const nb = /^\d+$/.test(pb[i]);
    if (na && nb) {
      const diff = Number(pa[i]) - Number(pb[i]);
      if (diff !== 0) return Math.sign(diff);
    } else if (na !== nb) {
      return na ? -1 : 1;
    } else if (pa[i] !== pb[i]) {
      return pa[i] < pb[i] ? -1 : 1;
    }
  }
  return 0;
}

export function majorOf(raw) {
  return parseVersion(raw)?.parts[0] ?? null;
}

/**
 * The part of a version that must stay equal for an update to be non-breaking, as caret ranges read it:
 * 2.3.4 -> "2", 0.4.26 -> "0.4", 0.0.3 -> "0.0.3". Used instead of the bare major so a 0.4 -> 0.5 bump
 * is classified as breaking.
 */
export function compatKey(raw) {
  const v = parseVersion(raw);
  if (!v) return null;
  const [major = 0, minor = 0, patch = 0] = v.parts;
  if (major > 0) return String(major);
  if (minor > 0) return `0.${minor}`;
  return `0.0.${patch}`;
}

/**
 * Pick the newest version overall and the newest compatible one (same compatKey, i.e. same major for 1.x+).
 * Prereleases are only considered when the current version is itself a prerelease,
 * mirroring how `dotnet list package --outdated` behaves by default.
 */
export function pickLatest(current, available) {
  const allowPre = isPrerelease(current);
  const candidates = available.filter((v) => parseVersion(v) && (allowPre || !isPrerelease(v)));
  const sorted = [...candidates].sort(compareVersions);
  const latest = sorted.at(-1) ?? null;
  const key = compatKey(current);
  const sameMajor = sorted.filter((v) => compatKey(v) === key).at(-1) ?? null;
  return { latest, latestSameMajor: sameMajor };
}

/**
 * Classify the gap between the current and the available versions.
 * Returns "major" when a newer major exists, "minor-patch" when only same-major
 * updates exist, and "current" otherwise. Both kinds can coexist; callers use
 * latestSameMajor to offer the safe upgrade alongside the major one.
 */
export function classifyUpdate(current, latest, latestSameMajor) {
  const hasMajor = latest && compatKey(latest) !== compatKey(current) && compareVersions(latest, current) > 0;
  const hasMinor = latestSameMajor && compareVersions(latestSameMajor, current) > 0;
  return { hasMajor: Boolean(hasMajor), hasMinor: Boolean(hasMinor) };
}

/** True when `version` is at or above `threshold` (both plain version strings). */
export function atLeast(version, threshold) {
  return compareVersions(version, threshold) >= 0;
}
