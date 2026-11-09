/**
 * pades-orchestrator-steps.tsx — pure derivation of the 4-step progress indicator for the PAdES orchestrator card.
 *
 * Split out from the card so the per-step status machine (wait → process → finish | error) is unit-testable in
 * isolation, independent of antd rendering. Each step's status is derived from the LIVE sub-status of its phase
 * (upload / virus-scan / sign+OTP / download) rather than a single coarse "current index", so the UI can show which
 * step is actively running, which are done, and which failed — including a mid-run error that aborts the chain.
 */

import { LoadingOutlined } from '@ant-design/icons';
import type { StepsProps } from 'antd';
import type { SigningRequestStatus } from './signature-types';

type StepItem = NonNullable<StepsProps['items']>[number];
type StepStatus = 'wait' | 'process' | 'finish' | 'error';

/** Minimal translate signature the card already satisfies (key + fallback → string). */
export type Translate = (key: string, fallback: string) => string;

/** Live inputs the card feeds in each render — all already-derived primitives, no hooks. */
export interface PadesStepInputs {
  /** The upload mutation is in flight. */
  uploading: boolean;
  /** Files PublicId — set once the upload succeeds. */
  fileId: string | null;
  /** Virus-scan status: 'Available' (passed) | 'Rejected' (failed) | anything else = still scanning. */
  scanStatus: string | undefined;
  /** The PAdES orchestration hook is in flight (stage + createSigningRequest; spans the whole OTP block in Sync). */
  padesLoading: boolean;
  /** Signing-request public id — set once createSigningRequest resolves (immediately in Async; post-OTP in Sync). */
  requestId: string | undefined;
  /** Polled signing-request lifecycle status. */
  signStatus: SigningRequestStatus | undefined;
  /** The signed PDF blob has been fetched and is ready to view/download. */
  signedReady: boolean;
  /** Any error surfaced this run (upload / scan / sign / download). */
  hasError: boolean;
}

/**
 * Per-card label overrides for the sign step (step 2). The status machine is identical across providers,
 * but the human wording differs by ceremony: remote PAdES confirms via a phone OTP, USB DocFlow via a
 * token PIN. Omit to keep the default remote/OTP wording.
 */
export interface SignStepLabels {
  title: string;
  process: string;
  finish: string;
  error: string;
}

function withProcessIcon(status: StepStatus): Pick<StepItem, 'status' | 'icon'> {
  // antd renders the right glyph for wait/finish/error; only the active step gets the spinner.
  return status === 'process' ? { status, icon: <LoadingOutlined /> } : { status };
}

/** Build the 4 antd Step items with per-step status + live description. */
export function buildPadesSteps(i: PadesStepInputs, t: Translate, signLabels?: SignStepLabels): StepItem[] {
  const scanDone = i.scanStatus === 'Available';
  const signCompleted = i.signStatus === 'Completed';
  const signFailed = i.signStatus === 'Failed' || i.signStatus === 'Expired';

  // ── Step 0: upload ──
  let upload: StepStatus;
  if (i.hasError && !i.fileId) upload = 'error';
  else if (i.uploading) upload = 'process';
  else if (i.fileId) upload = 'finish';
  else upload = 'wait';

  // ── Step 1: virus scan ──
  let scan: StepStatus;
  if (!i.fileId) scan = 'wait';
  else if (i.scanStatus === 'Rejected') scan = 'error';
  else if (scanDone) scan = 'finish';
  else scan = 'process';

  // ── Step 2: sign + OTP ──
  let sign: StepStatus;
  if (!scanDone) sign = 'wait';
  else if (signCompleted) sign = 'finish';
  else if (signFailed || (i.hasError && !i.signedReady)) sign = 'error';
  else sign = 'process'; // staging / createSigningRequest / polling / OTP wait

  // ── Step 3: assemble + download ──
  let done: StepStatus;
  if (!signCompleted) done = 'wait';
  else if (i.signedReady) done = 'finish';
  else if (i.hasError) done = 'error';
  else done = 'process'; // signed server-side, fetching the bytes

  const desc = (status: StepStatus, process: string, finish: string, error: string): string => {
    if (status === 'process') return process;
    if (status === 'finish') return finish;
    if (status === 'error') return error;
    return '';
  };

  return [
    {
      title: t('digitalSignature.padesOrchStepUpload', 'Tải lên'),
      description: desc(
        upload,
        t('digitalSignature.padesOrchUploadProc', 'Đang tải lên…'),
        t('digitalSignature.padesOrchUploadDone', 'Đã tải lên'),
        t('digitalSignature.padesOrchUploadErr', 'Tải lên thất bại'),
      ),
      ...withProcessIcon(upload),
    },
    {
      title: t('digitalSignature.padesOrchStepScan', 'Quét virus'),
      description: desc(
        scan,
        t('digitalSignature.padesOrchScanProc', 'Đang quét…'),
        t('digitalSignature.padesOrchScanDone', 'An toàn'),
        t('digitalSignature.padesOrchScanErr', 'Bị từ chối'),
      ),
      ...withProcessIcon(scan),
    },
    {
      title: signLabels?.title ?? t('digitalSignature.padesOrchStepOtp', 'Ký + OTP'),
      description: desc(
        sign,
        signLabels?.process ?? t('digitalSignature.padesOrchOtpProc', 'Chờ xác nhận OTP trên điện thoại…'),
        signLabels?.finish ?? t('digitalSignature.padesOrchOtpDone', 'Đã ký'),
        signLabels?.error ?? t('digitalSignature.padesOrchOtpErr', 'Ký thất bại / hết hạn'),
      ),
      ...withProcessIcon(sign),
    },
    {
      title: t('digitalSignature.padesOrchStepDone', 'Hoàn tất'),
      description: desc(
        done,
        t('digitalSignature.padesOrchDoneProc', 'Đang lấy PDF đã ký…'),
        t('digitalSignature.padesOrchDoneReady', 'Sẵn sàng tải xuống'),
        t('digitalSignature.padesOrchDoneErr', 'Tải PDF thất bại'),
      ),
      ...withProcessIcon(done),
    },
  ];
}
