// EnrollmentList — Phase 4 additions: SubjectType tag (Personal/Organization) + displayName.
// Covers: status badge per CertApprovalStatus; Organization tag + displayName render; empty state.

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { SignatureEnrollmentStatusDto } from '../signature-types';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

import { EnrollmentList } from '../enrollment-list';

function makeEnrollment(overrides: Partial<SignatureEnrollmentStatusDto>): SignatureEnrollmentStatusDto {
  return {
    publicId: crypto.randomUUID(),
    kind: 'RemoteViettelMysign',
    approvalStatus: 'Pending',
    isActive: false,
    serialNumber: 'ABC123',
    issuer: 'CN=Test CA',
    subject: null,
    enrolledAt: '2026-06-30T03:00:00Z',
    rejectionReason: null,
    subjectType: 'Personal',
    displayName: null,
    ...overrides,
  };
}

describe('EnrollmentList', () => {
  it('renders a status badge per approval status', () => {
    render(
      <EnrollmentList
        enrollments={[
          makeEnrollment({ approvalStatus: 'Pending', kind: 'RemoteViettelMysign' }),
          makeEnrollment({ approvalStatus: 'Approved', kind: 'UsbTokenVgca' }),
          makeEnrollment({ approvalStatus: 'Rejected', rejectionReason: 'Cert expired' }),
        ]}
      />,
    );
    expect(screen.getByText('profile.signatures.status.pending')).toBeInTheDocument();
    expect(screen.getByText('profile.signatures.status.approved')).toBeInTheDocument();
    expect(screen.getByText('profile.signatures.status.rejected')).toBeInTheDocument();
    // Kind labels resolve through the i18n map (two rows are Remote, one is USB).
    expect(screen.getAllByText('profile.signatures.kind.remote')).toHaveLength(2);
    expect(screen.getByText('profile.signatures.kind.usb')).toBeInTheDocument();
  });

  it('renders Organization tag and display name for org certificates', () => {
    render(
      <EnrollmentList
        enrollments={[
          makeEnrollment({
            subjectType: 'Organization',
            displayName: 'Công ty TNHH ABC',
            approvalStatus: 'Pending',
          }),
          makeEnrollment({
            subjectType: 'Personal',
            displayName: 'Nguyễn Văn A',
            approvalStatus: 'Approved',
          }),
        ]}
      />,
    );
    // Subject type tags are rendered via i18n key.
    expect(screen.getByText('profile.signatures.subjectType.organization')).toBeInTheDocument();
    expect(screen.getByText('profile.signatures.subjectType.personal')).toBeInTheDocument();
    // Display names are shown alongside the tags.
    expect(screen.getByText('Công ty TNHH ABC')).toBeInTheDocument();
    expect(screen.getByText('Nguyễn Văn A')).toBeInTheDocument();
  });

  it('does not render a display name when it is null', () => {
    render(
      <EnrollmentList
        enrollments={[makeEnrollment({ subjectType: 'Personal', displayName: null })]}
      />,
    );
    expect(screen.getByText('profile.signatures.subjectType.personal')).toBeInTheDocument();
    // No extra text node for a null display name.
    expect(screen.queryByText('null')).not.toBeInTheDocument();
  });

  it('shows the empty state when there are no enrollments', () => {
    render(<EnrollmentList enrollments={[]} />);
    expect(screen.getByText('profile.signatures.enrollmentsEmpty')).toBeInTheDocument();
  });
});
