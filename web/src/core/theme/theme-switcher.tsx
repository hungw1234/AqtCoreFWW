import { Switch } from 'antd';
import { SunOutlined, MoonOutlined } from '@ant-design/icons';
import { useThemeMode } from './use-theme-mode';

// ThemeSwitcher: toggles between light and dark Ant Design algorithm
// Persists choice to localStorage under key 'theme_mode'
export function ThemeSwitcher() {
  const { isDark, toggle } = useThemeMode();

  return (
    <Switch
      checked={isDark}
      onChange={toggle}
      checkedChildren={<MoonOutlined />}
      unCheckedChildren={<SunOutlined />}
      aria-label="Toggle dark mode"
    />
  );
}
