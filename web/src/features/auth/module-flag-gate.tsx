import { Spin } from 'antd';
import type { ComponentType, FunctionComponent, ReactNode } from 'react';
import { useModuleFlags, type DeferredModuleName } from './module-flags-api';

interface ModuleFlagGateProps {
  children: ReactNode;
  /** Which deferred module this page belongs to (see ModuleFlagsDto). */
  module: DeferredModuleName;
}

// ModuleFlagGate: hides pages of deferred modules that are OFF in this deployment.
// Companion to RouteGuard (auth/permissions); this one answers "does this deployment
// ship the module at all". Renders a 404-style panel — not 403 — because a disabled
// module's API routes are genuinely unmapped server-side; no amount of permission
// would make the page work. Fail-open on fetch errors (see module-flags-api).
export function ModuleFlagGate({ children, module }: ModuleFlagGateProps) {
  const { flags, isFlagsLoading } = useModuleFlags();

  if (isFlagsLoading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '50vh' }}>
        <Spin size="large" />
      </div>
    );
  }

  if (!flags[module]) {
    return (
      <div style={{ padding: 48, textAlign: 'center' }}>
        <h2>404 — Tính năng không khả dụng</h2>
        <p>Chức năng này không được kích hoạt trong phiên bản triển khai hiện tại.</p>
      </div>
    );
  }

  return <>{children}</>;
}

/**
 * Route-file convenience: wraps a route component (incl. lazyRouteComponent results) in a
 * ModuleFlagGate so each deferred-module route changes exactly one line:
 *   component: withModuleFlag('reporting', lazyRouteComponent(...))
 */
export function withModuleFlag(
  module: DeferredModuleName,
  Component: ComponentType,
): FunctionComponent {
  return function ModuleFlagGatedRoute() {
    return (
      <ModuleFlagGate module={module}>
        <Component />
      </ModuleFlagGate>
    );
  };
}
