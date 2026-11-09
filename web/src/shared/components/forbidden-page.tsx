import { Button, Result } from 'antd';
import { useTranslation } from 'react-i18next';

// ForbiddenPage: shown when user lacks permission to access a resource (403)
export function ForbiddenPage() {
  const { t } = useTranslation();
  return (
    <Result
      status="403"
      title="403"
      subTitle={t('error.forbidden.subTitle')}
      extra={
        <Button onClick={() => window.history.back()}>
          {t('error.forbidden.back')}
        </Button>
      }
    />
  );
}
