import { Component, type ReactNode } from 'react';
import { Button, Result, Space } from 'antd';
import { useTranslation } from 'react-i18next';

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
  error: Error | null;
}

// ErrorFallback: functional component so it can use i18n hooks
function ErrorFallback() {
  const { t } = useTranslation();
  return (
    <Result
      status="error"
      title={t('error.boundary.title')}
      subTitle={t('error.boundary.subTitle')}
      extra={
        <Space>
          <Button type="primary" onClick={() => window.location.reload()}>
            {t('error.boundary.reload')}
          </Button>
          <Button onClick={() => (window.location.href = '/')}>
            {t('error.boundary.backHome')}
          </Button>
        </Space>
      }
    />
  );
}

// ErrorBoundary: catches unhandled render errors and shows a recovery UI
export class ErrorBoundary extends Component<Props, State> {
  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  state: State = { hasError: false, error: null };

  componentDidCatch(error: Error, info: { componentStack: string }) {
    // Log to console; in production wire to Sentry or similar
    console.error('[ErrorBoundary]', error, info.componentStack);
  }

  render() {
    if (!this.state.hasError) return this.props.children;
    return <ErrorFallback />;
  }
}
