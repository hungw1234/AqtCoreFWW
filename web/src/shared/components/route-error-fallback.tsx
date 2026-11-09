import { Button, Result } from 'antd';
import { useTranslation } from 'react-i18next';
import type { ErrorComponentProps } from '@tanstack/react-router';

// Per-route error boundary — isolates render crashes to the failing route
// instead of killing the entire app via the global ErrorBoundary.
export function RouteErrorFallback({ error, reset }: ErrorComponentProps) {
  const { t } = useTranslation();
  const msg = error instanceof Error
    ? error.message
    : t('error.boundary.subTitle', { defaultValue: 'Đã xảy ra lỗi không mong muốn.' });

  return (
    <Result
      status="error"
      title={t('error.boundary.title', { defaultValue: 'Đã xảy ra lỗi' })}
      subTitle={msg}
      extra={[
        <Button key="retry" type="primary" onClick={reset}>
          {t('common.reload', { defaultValue: 'Thử lại' })}
        </Button>,
        <Button key="reload" onClick={() => window.location.reload()}>
          {t('error.boundary.reload', { defaultValue: 'Tải lại trang' })}
        </Button>,
      ]}
    />
  );
}
