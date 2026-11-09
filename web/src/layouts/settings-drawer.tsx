import { Drawer, Switch, Divider, Typography } from 'antd';
import { useThemeMode } from '@/core/theme/use-theme-mode';

const { Text } = Typography;

interface SettingsDrawerProps {
  open: boolean;
  onClose: () => void;
  collapsed: boolean;
  onCollapsedChange: (collapsed: boolean) => void;
  popupSubMenus: boolean;
  onPopupSubMenusChange: (popup: boolean) => void;
}

// Settings panel — theme and layout options (Nyasha pattern)
export function SettingsDrawer({ open, onClose, collapsed, onCollapsedChange, popupSubMenus, onPopupSubMenusChange }: SettingsDrawerProps) {
  const { isDark, toggle } = useThemeMode();

  return (
    <Drawer
      title="Cài đặt"
      placement="right"
      width={280}
      onClose={onClose}
      open={open}
      styles={{ body: { padding: '16px 24px' } }}
    >
      <SettingRow label="Chế độ tối" checked={isDark} onChange={toggle} />
      <Divider style={{ margin: '12px 0' }} />
      <SettingRow label="Menu con dạng popup" checked={popupSubMenus} onChange={onPopupSubMenusChange} />
      <Divider style={{ margin: '12px 0' }} />
      <SettingRow label="Thu gọn thanh bên" checked={collapsed} onChange={onCollapsedChange} />
    </Drawer>
  );
}

// Reusable setting row with label + switch
function SettingRow({ label, checked, onChange }: {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <div style={{
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      padding: '10px 0',
    }}>
      <Text>{label}</Text>
      <Switch checked={checked} onChange={onChange} size="small" />
    </div>
  );
}
