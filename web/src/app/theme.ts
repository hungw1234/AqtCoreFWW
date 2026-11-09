import type { ThemeConfig } from 'antd';

// Color palette — Nyasha One inspired, clean modern blue primary
export const GOV_COLORS = {
  // --- Primary ---
  navy: '#007BFF',
  navyDeep: '#0056B3',
  navyLight: '#3395FF',

  // --- Interactive ---
  actionBlue: '#007BFF',

  // --- Backgrounds ---
  bgLayout: '#F7F7F9',
  bgCard: '#FFFFFF',
  bgMuted: '#F1F3F5',

  // --- Text ---
  textPrimary: '#212529',
  textSecondary: '#6C757D',
  textMuted: '#ADB5BD',

  // --- Borders ---
  borderColor: '#E9ECEF',

  // --- Accent (unified with primary) ---
  gold: '#007BFF',
  goldText: '#0056B3',

  // --- Semantic Status ---
  success: '#28A745',
  warning: '#FFC107',
  error: '#DC3545',
  info: '#007BFF',

  white: '#FFFFFF',
} as const;

// Dark mode tokens — card surface DISTINCT from background
export const GOV_DARK_COLORS = {
  navy: '#1A6FDB',
  navyDeep: '#0D1B2A',
  navyLight: '#3395FF',
  bgLayout: '#0D1B2A',
  bgCard: '#142234',
  bgElevated: '#1C2F44',
  bgMuted: '#111D2E',
  textPrimary: '#F1F5F9',
  textSecondary: '#94A3B8',
  borderColor: '#1E3A55',
} as const;

// Layout dimensions — Nyasha proportions
export const LAYOUT = {
  headerHeight: 60,
  siderWidth: 256,
  siderCollapsedWidth: 72,
  contentPadding: 24,
  contentPaddingH: 24,
  contentMaxWidth: 1440,
  pageGap: 24,
} as const;

// Elevation shadow system — flatter, Nyasha-style
export const ELEVATION = {
  1: '0 1px 2px rgba(0,0,0,.06)',
  2: '0 2px 6px rgba(0,0,0,.08)',
  3: '0 4px 12px rgba(0,0,0,.10)',
  hover: '0 4px 10px rgba(0,0,0,.10)',
} as const;

// Ant Design theme config — Nyasha-inspired clean modern
export const govTheme: ThemeConfig = {
  token: {
    colorPrimary: GOV_COLORS.navy,
    colorError: GOV_COLORS.error,
    colorWarning: GOV_COLORS.warning,
    colorSuccess: GOV_COLORS.success,
    colorInfo: GOV_COLORS.info,
    colorLink: GOV_COLORS.actionBlue,
    colorBgLayout: GOV_COLORS.bgLayout,
    colorBgContainer: GOV_COLORS.bgCard,
    colorText: GOV_COLORS.textPrimary,
    colorTextSecondary: GOV_COLORS.textSecondary,
    colorTextTertiary: GOV_COLORS.textMuted,
    colorBorder: GOV_COLORS.borderColor,
    borderRadius: 8,
    borderRadiusLG: 12,
    borderRadiusSM: 6,
    fontFamily: "'Inter', 'Noto Sans', 'Segoe UI', sans-serif",
    fontFamilyCode: "'JetBrains Mono', 'Fira Code', monospace",
    fontSize: 14,
    lineHeight: 1.5714,
  },
  components: {
    Layout: {
      headerBg: GOV_COLORS.white,
      siderBg: GOV_COLORS.white,
      headerHeight: LAYOUT.headerHeight,
    },
    Menu: {
      itemBg: 'transparent',
      subMenuItemBg: 'transparent',
      itemSelectedBg: 'rgba(0, 123, 255, 0.08)',
      itemHoverBg: 'rgba(0, 123, 255, 0.04)',
      itemSelectedColor: '#007BFF',
      itemColor: '#495057',
      iconSize: 16,
      activeBarBorderWidth: 0,
    },
    Card: {
      borderRadiusLG: 12,
      boxShadowTertiary: ELEVATION[1],
      paddingLG: 20,
    },
    Table: {
      headerBg: '#F8F9FA',
      rowHoverBg: 'rgba(0, 123, 255, 0.04)',
      borderRadius: 0,
      headerBorderRadius: 0,
      headerColor: '#495057',
      headerSplitColor: 'transparent',
    },
    Button: {
      primaryColor: GOV_COLORS.white,
      borderRadius: 6,
      controlHeight: 36,
      controlHeightSM: 28,
    },
    Input: {
      controlHeight: 36,
      borderRadius: 6,
    },
    Select: {
      controlHeight: 36,
      borderRadius: 6,
    },
    Tag: {
      borderRadiusSM: 4,
    },
  },
};
