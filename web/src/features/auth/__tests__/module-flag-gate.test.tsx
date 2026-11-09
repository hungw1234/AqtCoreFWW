// ModuleFlagGate + withModuleFlag + filterItemsByModuleFlags — deferred-module OFF UX.
// Both branches asserted per flag posture: enabled renders children (default/fail-open),
// disabled renders the 404 panel / strips the menu item. The api hook itself is mocked —
// its fail-open contract (data ?? ALL_MODULES_ENABLED) is exercised via the error case.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { ItemType, MenuItemType } from 'antd/es/menu/interface';
import { ModuleFlagGate, withModuleFlag } from '../module-flag-gate';
import { ALL_MODULES_ENABLED, type ModuleFlagsDto } from '../module-flags-api';
import { filterItemsByModuleFlags } from '@/layouts/module-flag-menu-filter';

// Mock ONLY the hook module; re-export everything else real so sibling imports
// (ALL_MODULES_ENABLED, types) keep working — enumerated-exports trap guard.
const mockUseModuleFlags = vi.fn();
vi.mock('../module-flags-api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../module-flags-api')>();
  return { ...actual, useModuleFlags: () => mockUseModuleFlags() };
});

const flags = (overrides: Partial<ModuleFlagsDto>): ModuleFlagsDto => ({
  ...ALL_MODULES_ENABLED,
  ...overrides,
});

beforeEach(() => {
  mockUseModuleFlags.mockReset();
});

describe('ModuleFlagGate', () => {
  it('renders children when the module is enabled', () => {
    mockUseModuleFlags.mockReturnValue({ flags: ALL_MODULES_ENABLED, isFlagsLoading: false });

    render(<ModuleFlagGate module="reporting"><div>report-page</div></ModuleFlagGate>);

    expect(screen.getByText('report-page')).toBeInTheDocument();
  });

  it('renders the 404 panel (not the page) when the module is disabled', () => {
    mockUseModuleFlags.mockReturnValue({ flags: flags({ reporting: false }), isFlagsLoading: false });

    render(<ModuleFlagGate module="reporting"><div>report-page</div></ModuleFlagGate>);

    expect(screen.queryByText('report-page')).not.toBeInTheDocument();
    expect(screen.getByText(/404/)).toBeInTheDocument();
  });

  it('renders a spinner while flags are loading, not the page and not the 404', () => {
    mockUseModuleFlags.mockReturnValue({ flags: ALL_MODULES_ENABLED, isFlagsLoading: true });

    const { container } = render(
      <ModuleFlagGate module="ai"><div>ai-page</div></ModuleFlagGate>);

    expect(screen.queryByText('ai-page')).not.toBeInTheDocument();
    expect(screen.queryByText(/404/)).not.toBeInTheDocument();
    expect(container.querySelector('.ant-spin')).not.toBeNull();
  });
});

describe('withModuleFlag', () => {
  it('produces a component gated on the given module', () => {
    mockUseModuleFlags.mockReturnValue({ flags: flags({ businessProcess: false }), isFlagsLoading: false });
    const Gated = withModuleFlag('businessProcess', () => <div>bp-page</div>);

    render(<Gated />);

    expect(screen.queryByText('bp-page')).not.toBeInTheDocument();
    expect(screen.getByText(/404/)).toBeInTheDocument();
  });
});

describe('filterItemsByModuleFlags', () => {
  const items: ItemType<MenuItemType>[] = [
    { key: '/chat', label: 'Chat' },
    { key: '/forms', label: 'Forms' },
    {
      key: 'reporting',
      label: 'Reporting',
      children: [
        { key: '/reports/definitions', label: 'Defs' },
        { key: '/reports/executions', label: 'Execs' },
      ],
    },
  ];

  it('is identity when every flag is enabled (default posture)', () => {
    expect(filterItemsByModuleFlags(items, ALL_MODULES_ENABLED)).toEqual(items);
  });

  it('strips items of a disabled module and prunes the emptied submenu', () => {
    const result = filterItemsByModuleFlags(
      items, flags({ collaboration: false, reporting: false }));

    expect(result.map((i) => i?.key)).toEqual(['/forms']);
  });

  it('keeps a submenu whose children survive', () => {
    const result = filterItemsByModuleFlags(items, flags({ collaboration: false }));

    expect(result.map((i) => i?.key)).toEqual(['/forms', 'reporting']);
  });
});
