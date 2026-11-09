// Profile page reconcile to Tabs — Phase 6 Slice A.
// Covers: both tabs render, "Thông tin" tab keeps the change-password flow, switching to the
// "Chữ ký số" tab mounts the signature management component (sourced from /me/signature-overview,
// NOT OIDC claims).

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

vi.mock('@/features/auth', () => ({
  useAuth: () => ({ user: { profile: { name: 'Nguyễn Văn A', email: 'a@x.vn', role: ['User'] } } }),
}));

vi.mock('../profile-api', () => ({
  useChangePassword: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

// Stub the signature tab — its own behaviour is covered by sibling component tests; here we only
// assert the tab mounts it.
vi.mock('../signatures/signature-management-tab', () => ({
  SignatureManagementTab: () => <div>signature-tab-mounted</div>,
}));

import { ProfilePage } from '../profile-page';

describe('ProfilePage (Tabs reconcile)', () => {
  it('renders both the info and signatures tabs', () => {
    render(<ProfilePage />);
    expect(screen.getByText('profile.tab.info')).toBeInTheDocument();
    expect(screen.getByText('profile.tab.signatures')).toBeInTheDocument();
  });

  it('keeps the change-password flow on the info tab', () => {
    render(<ProfilePage />);
    // Default active tab is "info" — the change-password trigger must still be present.
    expect(screen.getByText('profile.changePassword')).toBeInTheDocument();
  });

  it('mounts the signature management component when the signatures tab is selected', () => {
    render(<ProfilePage />);
    fireEvent.click(screen.getByText('profile.tab.signatures'));
    expect(screen.getByText('signature-tab-mounted')).toBeInTheDocument();
  });
});
