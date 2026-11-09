// Smoke tests for Phase 5 shared components
// TC-FE-P5-SEARCHBAR-001:  SearchBar renders
// TC-FE-P5-FACET-001:      FacetSidebar renders
// TC-FE-P5-STATUSBADGE-001: StatusBadge renders correct colors

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';

vi.mock('@/core/hooks/use-debounced-value', () => ({
  useDebouncedValue: (val: string) => val,
}));

import { SearchBar } from '@/shared/components/search-bar';
import { FacetSidebar, type FacetGroup } from '@/shared/components/facet-sidebar';
import { StatusBadge } from '@/shared/components/status-badge';

// ──────────────────────────────────────────────────────────────────────────────

describe('SearchBar — TC-FE-P5-SEARCHBAR-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<SearchBar onSearch={vi.fn()} />);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders input element', () => {
    const { container } = render(<SearchBar onSearch={vi.fn()} placeholder="Search..." />);
    expect(container.querySelector('input')).toBeTruthy();
  });

  it('renders with custom placeholder', () => {
    render(<SearchBar onSearch={vi.fn()} placeholder="Find something" />);
    expect(screen.getByPlaceholderText('Find something')).toBeTruthy();
  });
});

describe('FacetSidebar — TC-FE-P5-FACET-001', () => {
  const facets: FacetGroup[] = [
    {
      key: 'entityType',
      label: 'Type',
      buckets: [
        { value: 'case', label: 'Case', count: 10 },
        { value: 'file', label: 'File', count: 5 },
      ],
    },
  ];

  it('renders without crashing', () => {
    const { container } = render(
      <FacetSidebar facets={facets} selected={{}} onChange={vi.fn()} />,
    );
    expect(container.firstChild).toBeTruthy();
  });

  it('renders facet group label', () => {
    render(<FacetSidebar facets={facets} selected={{}} onChange={vi.fn()} />);
    expect(screen.getByText('Type')).toBeTruthy();
  });

  it('renders bucket labels', () => {
    render(<FacetSidebar facets={facets} selected={{}} onChange={vi.fn()} />);
    expect(screen.getByText('Case')).toBeTruthy();
    expect(screen.getByText('File')).toBeTruthy();
  });

  it('renders bucket counts', () => {
    render(<FacetSidebar facets={facets} selected={{}} onChange={vi.fn()} />);
    expect(screen.getByText('(10)')).toBeTruthy();
    expect(screen.getByText('(5)')).toBeTruthy();
  });

  it('returns null for empty facets', () => {
    const { container } = render(
      <FacetSidebar facets={[]} selected={{}} onChange={vi.fn()} />,
    );
    expect(container.firstChild).toBeNull();
  });
});

describe('StatusBadge — TC-FE-P5-STATUSBADGE-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<StatusBadge status="active" />);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders capitalized status text by default', () => {
    render(<StatusBadge status="active" />);
    expect(screen.getByText('Active')).toBeTruthy();
  });

  it('renders custom label when provided', () => {
    render(<StatusBadge status="active" label="Enabled" />);
    expect(screen.getByText('Enabled')).toBeTruthy();
  });

  it('renders approved status', () => {
    render(<StatusBadge status="approved" />);
    expect(screen.getByText('Approved')).toBeTruthy();
  });

  it('renders rejected status', () => {
    render(<StatusBadge status="rejected" />);
    expect(screen.getByText('Rejected')).toBeTruthy();
  });

  it('handles unknown status without crashing', () => {
    const { container } = render(<StatusBadge status="someUnknownStatus" />);
    expect(container.firstChild).toBeTruthy();
  });
});
