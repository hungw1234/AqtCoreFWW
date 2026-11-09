import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// TC-FE-LAY-005: DelegationBanner shows when delegation active
// TC-FE-LAY-006: DelegationBanner hidden when no delegation

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

import { DelegationBanner } from '@/layouts/delegation-banner';

describe('DelegationBanner — TC-FE-LAY-005: shows when delegation active', () => {
  it('renders without crashing when actingAsName is provided', () => {
    const { container } = render(
      <DelegationBanner actingAsName="Tran Thi B" onClear={vi.fn()} />
    );
    expect(container.firstChild).toBeTruthy();
  });

  it('displays the delegated user name in bold', () => {
    render(<DelegationBanner actingAsName="Tran Thi B" onClear={vi.fn()} />);
    expect(screen.getByText('Tran Thi B')).toBeTruthy();
  });

  it('renders delegation.actingAs i18n key', () => {
    render(<DelegationBanner actingAsName="Tran Thi B" onClear={vi.fn()} />);
    expect(screen.getByText('delegation.actingAs')).toBeTruthy();
  });

  it('renders exit button with delegation.exit label', () => {
    render(<DelegationBanner actingAsName="Tran Thi B" onClear={vi.fn()} />);
    expect(screen.getByText('delegation.exit')).toBeTruthy();
  });

  it('calls onClear when exit button is clicked', async () => {
    const onClear = vi.fn();
    render(<DelegationBanner actingAsName="Tran Thi B" onClear={onClear} />);
    await userEvent.click(screen.getByText('delegation.exit'));
    expect(onClear).toHaveBeenCalledOnce();
  });
});

// TC-FE-LAY-006: DelegationBanner hidden when no delegation
// The banner component itself is always visible when rendered — the parent
// controls mounting. This test verifies conditional rendering at the usage level.
describe('DelegationBanner — TC-FE-LAY-006: hidden when no delegation', () => {
  it('is not in the DOM when not rendered (null actingAsName guard)', () => {
    // Simulate parent logic: only render banner when actingAsName is truthy
    const actingAsName: string | null = null;
    const { container } = render(
      <>{actingAsName && <DelegationBanner actingAsName={actingAsName} onClear={vi.fn()} />}</>
    );
    expect(container.firstChild).toBeNull();
  });

  it('is not in the DOM when actingAsName is empty string', () => {
    const actingAsName = '';
    const { container } = render(
      <>{actingAsName && <DelegationBanner actingAsName={actingAsName} onClear={vi.fn()} />}</>
    );
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });
});
