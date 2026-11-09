// TC-FE-CPW-001: change-password-page renders 3 form fields
// TC-FE-CPW-002: confirmPassword mismatch shows error
// TC-FE-CPW-003: success navigates to '/'

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';

// ─── Mocks ────────────────────────────────────────────────────────────────────

const mockNavigate = vi.fn();

// Return key as-is — consistent with all other tests in this project
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

vi.mock('@tanstack/react-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@tanstack/react-router')>();
  return {
    ...actual,
    useNavigate: () => mockNavigate,
    useSearch: () => ({ reason: undefined }),
  };
});

// The page calls appMessage.success/error on submit. Without stubbing appMessage,
// the success path throws (appMessage undefined) BEFORE navigate({ to: '/' }) — the
// TC-FE-CPW-003 navigate assertion then sees 0 calls.
vi.mock('@/core/api/notification-bridge', () => ({
  appMessage: { success: vi.fn(), error: vi.fn() },
  getNotificationInstance: () => null,
  setNotificationInstance: vi.fn(),
}));

const mockPost = vi.fn();
vi.mock('@/core/api', () => ({
  apiClient: { post: (...args: unknown[]) => mockPost(...args) },
}));

import { changePasswordRoute } from '@/routes/change-password-page';

const ChangePasswordPage = changePasswordRoute.options.component as React.ComponentType;

// ─── TC-FE-CPW-001: Renders 3 fields ─────────────────────────────────────────

describe('ChangePasswordPage — TC-FE-CPW-001: renders 3 form fields', () => {
  it('renders without crashing', () => {
    const { container } = render(<ChangePasswordPage />);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders currentPassword label (i18n key)', () => {
    render(<ChangePasswordPage />);
    // t('auth.currentPassword') → 'auth.currentPassword' (key passthrough)
    expect(screen.getByText('auth.currentPassword')).toBeTruthy();
  });

  it('renders newPassword label (i18n key)', () => {
    render(<ChangePasswordPage />);
    expect(screen.getByText('auth.newPassword')).toBeTruthy();
  });

  it('renders confirmPassword label (i18n key)', () => {
    render(<ChangePasswordPage />);
    expect(screen.getByText('auth.confirmPassword')).toBeTruthy();
  });

  it('renders submit button (i18n key)', () => {
    render(<ChangePasswordPage />);
    expect(screen.getByText('auth.changePasswordSubmit')).toBeTruthy();
  });

  it('renders 3 password inputs', () => {
    render(<ChangePasswordPage />);
    const inputs = document.querySelectorAll('input[type="password"]');
    expect(inputs.length).toBe(3);
  });
});

// ─── TC-FE-CPW-002: confirmPassword mismatch ──────────────────────────────────

describe('ChangePasswordPage — TC-FE-CPW-002: confirmPassword mismatch error', () => {
  beforeEach(() => {
    mockPost.mockReset();
    mockNavigate.mockReset();
  });

  it('shows mismatch error when confirmPassword differs from newPassword', async () => {
    render(<ChangePasswordPage />);

    const passwordInputs = document.querySelectorAll('input[type="password"]');
    const [currentInput, newInput, confirmInput] = Array.from(passwordInputs);

    fireEvent.change(currentInput, { target: { value: 'OldPass123!x' } });
    fireEvent.change(newInput, { target: { value: 'NewPass123456!' } });
    fireEvent.change(confirmInput, { target: { value: 'DifferentPass!' } });

    // Submit via button (t key returned as-is)
    const submitBtn = screen.getByText('auth.changePasswordSubmit');
    fireEvent.click(submitBtn);

    await waitFor(() => {
      // t('auth.passwordMismatch') → 'auth.passwordMismatch'
      expect(screen.getByText('auth.passwordMismatch')).toBeTruthy();
    });

    expect(mockPost).not.toHaveBeenCalled();
  });
});

// ─── TC-FE-CPW-003: Success navigates to '/' ─────────────────────────────────

describe('ChangePasswordPage — TC-FE-CPW-003: success navigates to root', () => {
  beforeEach(() => {
    mockPost.mockResolvedValue({ data: null });
    mockNavigate.mockReset();
  });

  it('calls navigate({ to: "/" }) after successful password change', async () => {
    render(<ChangePasswordPage />);

    const passwordInputs = document.querySelectorAll('input[type="password"]');
    const [currentInput, newInput, confirmInput] = Array.from(passwordInputs);

    fireEvent.change(currentInput, { target: { value: 'OldPass123!x' } });
    fireEvent.change(newInput, { target: { value: 'NewPass123456!' } });
    fireEvent.change(confirmInput, { target: { value: 'NewPass123456!' } });

    const submitBtn = screen.getByText('auth.changePasswordSubmit');
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(mockPost).toHaveBeenCalledWith('/account/change-password', {
        currentPassword: 'OldPass123!x',
        newPassword: 'NewPass123456!',
      });
    });

    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith({ to: '/' });
    });
  });
});
