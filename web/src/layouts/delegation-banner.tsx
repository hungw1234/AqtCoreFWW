import { Alert, Button } from 'antd';
import { useTranslation } from 'react-i18next';

interface DelegationBannerProps {
  actingAsName: string;
  onClear: () => void;
}

// DelegationBanner: shown when admin is acting on behalf of another user
export function DelegationBanner({ actingAsName, onClear }: DelegationBannerProps) {
  const { t } = useTranslation();
  return (
    <Alert
      type="warning"
      showIcon
      banner
      message={
        <span>
          {t('delegation.actingAs')} <strong>{actingAsName}</strong>
        </span>
      }
      action={
        <Button size="small" onClick={onClear}>
          {t('delegation.exit')}
        </Button>
      }
      style={{ borderRadius: 0 }}
    />
  );
}
