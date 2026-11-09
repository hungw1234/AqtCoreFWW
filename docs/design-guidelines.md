# Design Guidelines — Institutional Modern (2026-03-31)

AqtCoreFW UI implements **Institutional Modern** design system: professional government aesthetic combining semantic tokens, action blue interactivity, Navy identity, and accessibility-first patterns.

Source of truth: `web/src/app/theme.ts` + Ant Design 5.x token system.

---

## Color Palette (GOV_COLORS Semantic System)

### Identity & Interactivity (v2.30)
```ts
// web/src/app/theme.ts — GOV_COLORS
navy:          '#1B3A5C'  // Primary brand — buttons, sidebar, headings
actionBlue:    '#2563EB'  // Links, interactive elements (replaces red for actions)
navyDeep:      '#0F172A'  // Sidebar background, dark contexts
navyLight:     '#2A5080'  // Hover states, active menu items
```

### Status & Semantic
```ts
success:       '#16A34A'  // Positive outcomes, approved states
warning:       '#D97706'  // Cautions, pending states
error:         '#DC2626'  // Errors, destructive actions
info:          '#2563EB'  // Informational highlights
```

### Backgrounds & Surfaces
```ts
bgLayout:      '#F8FAFC'  // Page background (cool neutral)
bgCard:        '#FFFFFF'  // Card/modal backgrounds
bgMuted:       '#F1F5F9'  // Secondary backgrounds
textPrimary:   '#0F172A'  // Body text, headings (high contrast)
textSecondary: '#64748B'  // Labels, captions (5:1 contrast on light)
textMuted:     '#94A3B8'  // Disabled text, timestamps
borderColor:   '#E2E8F0'  // Input borders, dividers
gold:          '#F2A900'  // Branding only (logo, sidebar active) — NOT for text
```

### Dark Mode (Distinct Card Surface)
```ts
bgLayout:      '#0B1120'  // Dark background
bgCard:        '#131D2E'  // Card surface DISTINCT from background
bgMuted:       '#0F1928'  // Secondary backgrounds
textPrimary:   '#F1F5F9'  // High contrast text
textSecondary: '#94A3B8'  // Secondary text
borderColor:   '#1E3048'  // Dark borders
```

**Rules:**
- Use `actionBlue` (#2563EB) for all interactive elements (links, primary buttons)
- Restrict `gold` to branding only (logo, sidebar active state) — never as text
- Semantic colors (success/warning/error/info) provide clear status indication
- Dark mode has DISTINCT card (`#131D2E`) from background (`#0B1120`) for visual separation
- Prefer token names over raw hex; ensures consistency across updates

---

## Typography (Inter + Noto Sans)

| Property     | Value                                                        |
|-------------|--------------------------------------------------------------|
| Font family | `'Inter', 'Noto Sans', 'Segoe UI', sans-serif`              |
| Base size   | 14px (Ant token `fontSize`)                                 |
| Line height | 1.5714 (22px at 14px base)                                   |
| Heading 1   | Ant `Title level={1}` — 38px, font-weight 700                |
| Heading 2   | Ant `Title level={2}` — 30px, font-weight 600                |
| Heading 3   | Ant `Title level={3}` — 24px, font-weight 600                |
| Heading 4   | Ant `Title level={4}` — 20px, font-weight 600 (admin pages)  |
| Body        | `Text` — 14px, font-weight 400                               |
| Secondary   | `Text type="secondary"` — 14px, `#64748B`, font-weight 400   |

**Guidelines:**
- Page headers: `<AdminPageHeader>` component (replaces manual `Title level={4}`)
- Section titles in content: `<Title level={4}>` for subsections
- Use `<Typography.Paragraph>` for multi-line descriptions; `<Text>` for inline
- Do not set custom `fontSize` via inline styles — use Ant tokens
- Inter for UI text (clarity); Noto Sans fallback for Vietnamese character rendering in reports/PDFs

---

## Spacing Scale (LAYOUT Tokens)

Follows 8px grid via Ant Design + custom `LAYOUT` constants:

| Token               | Value | Use case                          |
|-------------------|-------|-----------------------------------|
| `LAYOUT.headerHeight` | 56px  | Fixed topbar height               |
| `LAYOUT.siderWidth`   | 260px | Desktop sidebar width             |
| `LAYOUT.siderCollapsedWidth` | 80px | Tablet sidebar (icons only) |
| `LAYOUT.contentPadding` | 24px | Vertical spacing                  |
| `LAYOUT.contentPaddingH` | 32px | Horizontal page margins           |
| `LAYOUT.contentMaxWidth` | 1440px | Maximum content width         |
| `LAYOUT.pageGap`   | 24px  | Spacing between page sections     |

**Ant Grid tokens:**
- xs: 4px (tight labels), sm: 8px (form gaps), md: 16px (section gaps), lg: 24px (page margins), xl: 32px (separators)

**Rules:**
- Use `--page-gap` CSS var (24px) for spacing between major sections
- AdminPageHeader auto-sets `marginBottom: var(--page-gap)`
- Content area padding: `24px 32px` (vert × horiz)
- Use `<Space size={...}>` for inline grouping; avoid custom `margin`

---

## Border Radius (v2.30)

| Context     | Value | Config key       | Use case                |
|------------|-------|------------------|------------------------|
| Default     | 8px   | `borderRadius`   | Buttons, inputs, tags   |
| Large       | 12px  | `borderRadiusLG` | Cards, modals, tables   |
| Small       | 6px   | `borderRadiusSM` | Compact components      |

---

## Admin Page Component Patterns (v2.30)

All admin pages follow a consistent structure using new components:

```tsx
import { AdminPageHeader, AdminContentCard, AdminTableToolbar } from '@/shared/components';

export function MyAdminPage() {
  return (
    <>
      <AdminPageHeader
        title="Page Title"
        description="Brief description"
        icon={<SomeIcon />}
        actions={<Button>Create</Button>}
        stats={{ total: 100, filtered: 25, label: 'items' }}
      />
      
      <AdminContentCard>
        <AdminTableToolbar
          search={{ placeholder: 'Search...' }}
          filters={[{ label: 'Status', options: [...] }]}
        />
        <Table dataSource={data} ... />
      </AdminContentCard>
    </>
  );
}
```

### AdminPageHeader
- Consistent header card with icon + title + description + actions + stats
- Auto-applies elevation shadow and bottom margin (24px)
- Icon colored in `GOV_COLORS.navy`, title level 4

### AdminContentCard
- Card wrapper for table/content with elevation and border radius
- Variant `borderless`, elevation shadow, 12px border radius
- Use inside AdminPageHeader flow

### AdminTableToolbar
- Search bar + filter chips + bulk actions
- Consistent styling across all admin pages

### Tables
- Always provide `rowKey`
- Use `size="small"` for admin tables (data-dense)
- Set `scroll={{ x: <min-width> }}` to prevent overflow (standard: 700–900px)
- Wrap in `AdminContentCard` for consistent styling

### Buttons (ActionBlue Theme)
- Primary: `type="primary"` — actionBlue background (#2563EB)
- Destructive: `danger` prop — error red (#DC2626)
- Ghost/cancel: default `type="default"`
- Never override `background` or `borderColor` — use semantic props

### Status Indicators
- Use `colorSuccess` (green), `colorWarning` (amber), `colorError` (red), `colorInfo` (blue)
- Prefer semantic colors over gold for status badges
- Gold restricted to sidebar active state only

---

## Dark Mode

Dark mode is toggled via `ThemeModeProvider` in `web/src/app/providers.tsx`.

When `isDark === true`, Ant's `darkAlgorithm` is applied alongside `govTheme`.

**Known limitation:** GOV brand colors (navy sidebar, red accent) may shift in dark mode because `govTheme` only defines light-mode tokens. Until dark-specific overrides are added, dark mode is best-effort.

Implementation pattern:
```tsx
// In providers.tsx — algorithm applied conditionally
algorithm: isDark ? [theme.darkAlgorithm, theme.compactAlgorithm] : undefined
```

---

## Responsive Breakpoints

Follows Ant Design Grid breakpoints:

| Name | Min width | Use case                    |
|------|-----------|-----------------------------|
| xs   | 0         | Mobile portrait             |
| sm   | 576px     | Mobile landscape            |
| md   | 768px     | Tablet — sidebar collapses  |
| lg   | 992px     | Desktop minimum             |
| xl   | 1200px    | Wide desktop                |
| xxl  | 1600px    | Ultra-wide                  |

**Sidebar:** collapses at `md` breakpoint (`breakpoint="md"` on Ant `Sider`).

**Content max-width:** No explicit cap currently. Recommended max: 1440px with `margin: 0 auto`.

**Grid patterns in use:**
- KPI cards: `xs={24} sm={12} xl={6}` (4-column on desktop, 2 on tablet, stacked on mobile)
- Detail pages: `xs={24} lg={16}` (constrained width)
- Master-detail: `xs={24} lg={8}` / `lg={16}` split

---

## i18n Requirements

- All user-facing strings MUST use `t('key')` from `useTranslation()`
- Translation files: `web/src/core/i18n/locales/vi.json` and `en.json`
- Always add keys to both files simultaneously
- Key naming: `page.<pageName>.<element>` for page strings, `common.<verb>` for shared actions
- Date format: `DD/MM/YYYY` (Vietnamese standard) via `dayjs().format(...)`
- Numbers: use `Intl.NumberFormat('vi-VN')` for large numerics

---

## Accessibility Checklist

- WCAG AA minimum contrast: 4.5:1 for normal text, 3:1 for large text
- Interactive elements: `aria-label` required when visible label absent
- Landmark roles: `role="main"`, `role="banner"`, `role="navigation"` on layout containers
- Keyboard: all actions reachable via Tab + Enter/Space
- Do not use color alone to convey state (pair with icon or text)
