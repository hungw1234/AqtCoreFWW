// Parallel branch status panel — shows per-branch approval progress with inline resolve actions

import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Card, Steps, Button, Space, Input, Popconfirm, Typography, Spin } from 'antd';
import { appMessage } from '@/core/api/notification-bridge';
import {
  ClockCircleFilled,
  CheckCircleFilled,
  CloseCircleFilled,
  WarningFilled,
  ExclamationCircleFilled,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  useBranchStatuses,
  useResolveBranchChild,
  useClaimTask,
  workflowKeys,
  BranchResolution,
  type BranchChildStatusDto,
} from './workflow-api';
import type { NormalizedApiError } from '@/core/api/api-error';

const { Text } = Typography;

interface Props {
  instanceId: string;
  /** Current user ID — resolve buttons only shown when user is the assignee (F3) */
  currentUserId: string;
}

// Map resolutionType → icon + color
function getStepIcon(resolutionType: number | undefined) {
  if (resolutionType === undefined || resolutionType === null) {
    return <ClockCircleFilled style={{ color: '#aaa' }} />;
  }
  switch (resolutionType) {
    case BranchResolution.Approved:
      return <CheckCircleFilled style={{ color: '#52c41a' }} />;
    case BranchResolution.Rejected:
      return <CloseCircleFilled style={{ color: '#ff4d4f' }} />;
    case BranchResolution.Escalated:
      return <WarningFilled style={{ color: '#fa8c16' }} />;
    case BranchResolution.Timeout:
      return <ExclamationCircleFilled style={{ color: '#fa8c16' }} />;
    default:
      return <ClockCircleFilled style={{ color: '#aaa' }} />;
  }
}

// Inline approve/reject row for a pending child
interface ChildActionRowProps {
  child: BranchChildStatusDto;
  instanceId: string;
}

function ChildActionRow({ child, instanceId }: ChildActionRowProps) {
  const { t } = useTranslation();
  const [comment, setComment] = useState('');
  const resolveMutation = useResolveBranchChild(instanceId);

  const handleResolve = async (resolutionType: number) => {
    try {
      await resolveMutation.mutateAsync({
        childStatusId: child.id,
        resolutionType,
        comment: comment.trim() || undefined,
      });
      appMessage.success(
        resolutionType === BranchResolution.Approved
          ? t('branch.approveSuccess', 'Đã phê duyệt')
          : t('branch.rejectSuccess', 'Đã từ chối'),
      );
    } catch {
      appMessage.error(t('common.error', 'Có lỗi xảy ra'));
    }
  };

  return (
    <Space direction="vertical" size={4} style={{ width: '100%', marginTop: 4 }}>
      <Input
        size="small"
        placeholder={t('branch.commentPlaceholder', 'Ghi chú (tuỳ chọn)')}
        value={comment}
        onChange={(e) => setComment(e.target.value)}
        style={{ maxWidth: 280 }}
      />
      <Space size="small">
        <Popconfirm
          title={t('branch.approveConfirm', 'Xác nhận phê duyệt?')}
          onConfirm={() => handleResolve(BranchResolution.Approved)}
          okText={t('common.confirm', 'Xác nhận')}
          cancelText={t('common.cancel', 'Hủy')}
        >
          <Button
            size="small"
            type="primary"
            style={{ backgroundColor: '#52c41a', borderColor: '#52c41a' }}
            loading={resolveMutation.isPending}
          >
            {t('branch.approve', 'Phê duyệt')}
          </Button>
        </Popconfirm>
        <Popconfirm
          title={t('branch.rejectConfirm', 'Xác nhận từ chối?')}
          onConfirm={() => handleResolve(BranchResolution.Rejected)}
          okText={t('common.confirm', 'Xác nhận')}
          cancelText={t('common.cancel', 'Hủy')}
          okButtonProps={{ danger: true }}
        >
          <Button size="small" danger loading={resolveMutation.isPending}>
            {t('branch.reject', 'Từ chối')}
          </Button>
        </Popconfirm>
      </Space>
    </Space>
  );
}

// Claim button for a pooled, unclaimed child — any pool-eligible viewer may take it.
// Eligibility is enforced server-side on Claim (403 → toast); the button shows optimistically
// because computing per-viewer pool-eligibility in the list projection would be costly.
interface ChildClaimRowProps {
  taskId: number;
  instanceId: string;
}

function ChildClaimRow({ taskId, instanceId }: ChildClaimRowProps) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  // Suppress the global 403 interstitial: for optimistic-show Claim, a 403 means "not pool-eligible",
  // a routine outcome we surface with a specific toast below (not a navigation-worthy auth failure).
  const claimMutation = useClaimTask({ suppressAuthInterstitial: true });

  const handleClaim = async () => {
    try {
      await claimMutation.mutateAsync(taskId);
      qc.invalidateQueries({ queryKey: ['workflow-branch-status', instanceId] });
      qc.invalidateQueries({ queryKey: workflowKeys.instance(instanceId) });
      appMessage.success(t('branch.claimSuccess', 'Đã nhận task'));
    } catch (err) {
      // The interceptor rejects with a NormalizedApiError (flat .status). 403 = not pool-eligible
      // (e.g. the initiator or an observer who can view but not claim); anything else = transient.
      const status = (err as NormalizedApiError | undefined)?.status;
      appMessage.error(
        status === 403
          ? t('branch.claimForbidden', 'Bạn không đủ điều kiện nhận task này')
          : t('common.error', 'Có lỗi xảy ra'),
      );
    }
  };

  return (
    <Button
      size="small"
      type="primary"
      loading={claimMutation.isPending}
      onClick={handleClaim}
      style={{ marginTop: 4 }}
    >
      {t('branch.claim', 'Nhận task')}
    </Button>
  );
}

// Main panel — renders one Card per parallel branch
export function ParallelBranchStatusPanel({ instanceId, currentUserId }: Props) {
  const { t } = useTranslation();

  const { data: branches, isLoading } = useBranchStatuses(instanceId);

  // Empty state — render nothing per spec
  if (!isLoading && (!branches || branches.length === 0)) return null;

  if (isLoading) {
    return <Spin size="small" style={{ display: 'block', margin: '16px auto' }} />;
  }

  return (
    <Space direction="vertical" style={{ width: '100%' }} size={12}>
      {branches!.map((branch) => {
        const stepItems = branch.childStatuses.map((child, idx) => {
          const isPending = child.resolutionType === undefined || child.resolutionType === null;
          // F3: Only show resolve buttons to the assigned approver
          const isAssignee = isPending && child.assigneeId === currentUserId;
          // Pooled + unclaimed + task staged → offer Claim (eligibility enforced server-side).
          const isClaimable = isPending && !child.assigneeId && child.taskId != null;
          return {
            key: child.id,
            title: (
              <Text style={{ fontSize: 13 }}>
                {t('branch.approver', 'Người phê duyệt')} {idx + 1}
              </Text>
            ),
            icon: getStepIcon(child.resolutionType),
            description: isAssignee ? (
              <ChildActionRow
                child={child}
                instanceId={instanceId}
              />
            ) : isClaimable ? (
              <ChildClaimRow taskId={child.taskId!} instanceId={instanceId} />
            ) : isPending ? (
              <Text type="secondary" style={{ fontSize: 12 }}>
                {t('branch.awaitingApproval', 'Đang chờ phê duyệt')}
              </Text>
            ) : (
              child.comment && (
                <Text type="secondary" style={{ fontSize: 12 }}>
                  {child.comment}
                </Text>
              )
            ),
          };
        });

        return (
          <Card
            key={branch.id}
            size="small"
            title={
              <Space>
                <Text strong>{t('branch.branchLabel', 'Nhánh phê duyệt')}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>
                  {branch.status}
                </Text>
              </Space>
            }
          >
            <Steps
              direction="vertical"
              size="small"
              items={stepItems}
              style={{ marginTop: 4 }}
            />
          </Card>
        );
      })}
    </Space>
  );
}
