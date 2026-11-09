import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';

// TC-FE-ROUTE-001: Self-contained route page components mount without crash
// Route files in web/src/routes/ are thin wrappers around feature pages.
// Feature pages are already covered in smoke-user-pages and smoke-admin-pages.
// This file tests the inline page components defined directly inside route files
// plus shared page-level components (NotFoundPage, CallbackPage).

// Use importOriginal — route files use createRoute/createRootRoute which must be real
vi.mock('react-i18next', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-i18next')>();
  return {
    ...actual,
    useTranslation: () => ({ t: (key: string) => key }),
  };
});

vi.mock('@tanstack/react-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@tanstack/react-router')>();
  return {
    ...actual,
    useNavigate: () => vi.fn(),
    useLocation: () => ({ pathname: '/' }),
    useParams: () => ({}),
    useSearch: () => ({}),
  };
});

vi.mock('@/features/auth/use-auth', () => ({
  useAuth: () => ({
    user: null,
    isAuthenticated: false,
    isLoading: false,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}));

// Mock OIDC userManager used by CallbackPage
vi.mock('@/features/auth/auth-provider', () => ({
  userManager: {
    signinRedirectCallback: vi.fn(() => Promise.resolve({ state: {} })),
  },
  AuthContext: {
    Provider: ({ children }: { children: React.ReactNode }) => children,
  },
}));

// Import pages after mocks are registered
import { NotFoundPage } from '@/shared/components/not-found-page';
import { loginRoute } from '@/routes/login-page';
import { indexRoute } from '@/routes/index-page';
import { CallbackPage } from '@/features/auth/callback-page';

// Extract the inline component from the route definition
const LoginPageComponent = loginRoute.options.component as React.ComponentType;
const IndexPageComponent = indexRoute.options.component as React.ComponentType;

describe('TC-FE-ROUTE-001: Route-level page components mount without crash', () => {
  it('LoginPage renders without crashing', () => {
    const { container } = render(<LoginPageComponent />);
    expect(container.firstChild).toBeTruthy();
  });

  it('LoginPage renders login button text', () => {
    const { container } = render(<LoginPageComponent />);
    expect(container.textContent).toContain('Đăng nhập');
  });

  it('NotFoundPage renders without crashing', () => {
    const { container } = render(<NotFoundPage />);
    expect(container.firstChild).toBeTruthy();
  });

  it('NotFoundPage renders 404 status text', () => {
    const { container } = render(<NotFoundPage />);
    expect(container.textContent).toContain('404');
  });

  it('IndexPage renders without crashing', () => {
    const { container } = render(<IndexPageComponent />);
    expect(container.firstChild).toBeTruthy();
  });

  it('IndexPage renders GOV Admin Dashboard heading', () => {
    const { container } = render(<IndexPageComponent />);
    expect(container.textContent).toContain('GOV Admin Dashboard');
  });

  it('CallbackPage renders spinner without crashing', () => {
    const { container } = render(<CallbackPage />);
    expect(container.firstChild).toBeTruthy();
  });
});
