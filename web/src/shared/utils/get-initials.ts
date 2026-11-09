// Derives a 1-2 letter avatar fallback from a display name.
// Two words → first letter of first + last word; single word → first two letters.
// Shared by the topbar avatar and the profile hero so the fallback stays identical.
export function getInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  if (parts.length >= 2) return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
  return parts[0].slice(0, 2).toUpperCase();
}
