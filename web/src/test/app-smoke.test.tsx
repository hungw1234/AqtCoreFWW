import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { govTheme, GOV_COLORS, LAYOUT } from '@/app/theme';

// Smoke test — verifies theme tokens and basic rendering contract
describe('GOV theme tokens', () => {
  it('has correct blue primary color', () => {
    expect(govTheme.token?.colorPrimary).toBe('#007BFF');
  });

  it('has correct error color', () => {
    expect(govTheme.token?.colorError).toBe('#DC3545');
  });

  it('has border radius 8', () => {
    expect(govTheme.token?.borderRadius).toBe(8);
  });

  it('exports GOV_COLORS with semantic tokens', () => {
    expect(GOV_COLORS.navy).toBe('#007BFF');
    expect(GOV_COLORS.error).toBe('#DC3545');
    expect(GOV_COLORS.success).toBe('#28A745');
    expect(GOV_COLORS.actionBlue).toBe('#007BFF');
    expect(GOV_COLORS.bgLayout).toBe('#F7F7F9');
  });

  it('exports LAYOUT dimensions', () => {
    expect(LAYOUT.headerHeight).toBe(60);
    expect(LAYOUT.siderWidth).toBe(256); // was 220; Nyasha One redesign (22df356d)
    expect(LAYOUT.contentMaxWidth).toBe(1440);
  });
});

// Minimal render test — ensures React renders without crashing
describe('render sanity', () => {
  it('renders a div with text content', () => {
    render(<div data-testid="hello">GOV Admin</div>);
    expect(screen.getByTestId('hello')).toHaveTextContent('GOV Admin');
  });
});
