import { Button, Result } from 'antd';
import { useTranslation } from 'react-i18next';

// NotFoundPage: shown for unknown routes (404)
export function NotFoundPage() {
  const { t } = useTranslation();
  return (
    <Result
      status="404"
      title="404"
      subTitle={t('error.notFound.subTitle')}
      extra={
        <Button type="primary" onClick={() => (window.location.href = '/')}>
          {t('error.notFound.backHome')}
        </Button>
      }
    />
  );
}
