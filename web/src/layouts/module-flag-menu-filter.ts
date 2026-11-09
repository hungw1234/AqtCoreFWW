// Menu-side companion of ModuleFlagGate: strips menu items that route into deferred
// modules turned OFF in this deployment (Features:{Name}:Enabled=false server-side).
// Applied as a POST-filter over the built Ant Menu item tree so it covers both menu
// sources — the API-driven menuTree AND the hardcoded fallback — in one place, without
// tagging every entry. Route-level enforcement is ModuleFlagGate; API-level is the 404
// from the unmapped controllers; this filter is purely "don't advertise dead doors".

import type { ItemType, MenuItemType } from 'antd/es/menu/interface';
import type { ModuleFlagsDto } from '@/features/auth/module-flags-api';

// Route-prefix → owning deferred module. Keys are matched with startsWith against the
// menu item key (which is the route path for navigable items). '/copilot' belongs to
// the AI module family (reserved future AI feature) — hidden together with AI.
const MODULE_ROUTE_PREFIXES: ReadonlyArray<[prefix: string, module: keyof ModuleFlagsDto]> = [
  ['/ai/', 'ai'],
  ['/copilot', 'ai'],
  ['/admin/ai', 'ai'],
  ['/chat', 'collaboration'],
  ['/business-process-runs', 'businessProcess'],
  ['/admin/business-processes', 'businessProcess'],
  ['/reports', 'reporting'],
  ['/admin/rules', 'rules'],
];

function isKeyEnabled(key: string, flags: ModuleFlagsDto): boolean {
  for (const [prefix, module] of MODULE_ROUTE_PREFIXES) {
    if (key.startsWith(prefix) && !flags[module]) return false;
  }
  return true;
}

/**
 * Recursively removes items routing into disabled modules; prunes groups/submenus that
 * end up empty. Identity when every flag is true (the default posture).
 */
export function filterItemsByModuleFlags(
  items: ItemType<MenuItemType>[],
  flags: ModuleFlagsDto,
): ItemType<MenuItemType>[] {
  return items.flatMap((item): ItemType<MenuItemType>[] => {
    if (!item || item.key == null) return [item];
    if (!isKeyEnabled(String(item.key), flags)) return [];

    if ('children' in item && Array.isArray(item.children)) {
      const children = filterItemsByModuleFlags(
        item.children as ItemType<MenuItemType>[], flags);
      // A group/submenu whose every child was removed advertises nothing — drop it.
      if (children.length === 0) return [];
      return [{ ...item, children } as ItemType<MenuItemType>];
    }
    return [item];
  });
}
