import { Outlet, createRootRoute } from '@tanstack/react-router';

// Root route — minimal shell; layout handled per-route (authenticated vs public)
export const rootRoute = createRootRoute({
  component: RootLayout,
});

function RootLayout() {
  return <Outlet />;
}
