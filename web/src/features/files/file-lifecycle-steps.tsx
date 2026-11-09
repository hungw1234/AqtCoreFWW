// file-lifecycle-steps.tsx — visual stepper of an attachment's upload → virus-scan → done lifecycle.
// Shared by the form File-field widget and the Files-page uploader so both surfaces render the same
// stages, letting a user see which steps an upload passes through (not just the current state).

import { Steps } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FileStatus } from './file-types';

export type FilePhase = 'uploading' | 'scanning' | 'clean' | 'rejected' | 'timeout';

/**
 * Derive the lifecycle phase from the upload-mutation + scan-poll state.
 * Returns null before an upload starts (nothing to render). A Quarantined status or a still-loading
 * scan poll both map to 'scanning' — both mean "uploaded, awaiting a terminal scan verdict".
 * `timedOut` (poll gave up before a terminal verdict) maps to 'timeout' so a stuck scan stops
 * looking like it is still in progress; it is checked after the real terminal verdicts so a late
 * Available/Rejected still wins.
 */
export function deriveFilePhase(
  isUploading: boolean,
  hasUploaded: boolean,
  scanStatus: FileStatus | undefined,
  timedOut = false,
): FilePhase | null {
  if (isUploading) return 'uploading';
  if (!hasUploaded) return null;
  if (scanStatus === 'Available') return 'clean';
  if (scanStatus === 'Rejected') return 'rejected';
  if (timedOut) return 'timeout';
  return 'scanning';
}

// Map each phase to the antd Steps cursor: which step is current + that step's status colour.
// 'error' applies to the current step (scan), so a rejected/timed-out file shows a red Quét-virus
// step (antd Steps has no 'warning' status; the timeout phase is disambiguated by the caller's
// adjacent retry message).
const PHASE_TO_STEP: Record<FilePhase, { current: number; status: 'process' | 'finish' | 'error' }> = {
  uploading: { current: 0, status: 'process' },
  scanning: { current: 1, status: 'process' },
  clean: { current: 2, status: 'finish' },
  rejected: { current: 1, status: 'error' },
  timeout: { current: 1, status: 'error' },
};

/** Three-step lifecycle indicator: Tải lên → Quét virus → Hoàn tất. Renders nothing when phase is null. */
export function FileLifecycleSteps({ phase }: { phase: FilePhase | null }) {
  const { t } = useTranslation();
  if (!phase) return null;
  const { current, status } = PHASE_TO_STEP[phase];
  return (
    <Steps
      size="small"
      current={current}
      status={status}
      items={[
        { title: t('files.lifecycle.upload', { defaultValue: 'Tải lên' }) },
        { title: t('files.lifecycle.scan', { defaultValue: 'Quét virus' }) },
        { title: t('files.lifecycle.done', { defaultValue: 'Hoàn tất' }) },
      ]}
    />
  );
}
