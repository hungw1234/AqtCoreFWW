import { CenteredSpin } from '@/shared/components/centered-spin';

// RouteLoadingSpinner: fallback shown while lazy route chunks are loading
export function RouteLoadingSpinner() {
  return (
    <CenteredSpin
      tip="Loading..."
      size="large"
      style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100%', minHeight: 200 }}
    />
  );
}
