// next-handler-card.tsx — shows who handles a BP run next.
// For each ACTIVE (Open/InProgress) task: the assignee name, or — for an unclaimed
// pool task — the candidate principals (role/group/org-unit/permission) resolved to
// display labels CLIENT-SIDE via cached list hooks (mirrors the useActiveBps pattern).

import { useMemo } from 'react';
import { Space, Tag, Typography, Empty, Spin, Button, App } from 'antd';
import { useTranslation } from 'react-i18next';
import { useQueryClient } from '@tanstack/react-query';
import { useTasksByInstance, useClaimTask, useUnclaimTask, workflowKeys } from '@/features/workflow/workflow-api';
import type { WorkflowTaskCandidateDto } from '@/features/workflow/workflow-types';
import { useAuth } from '@/features/auth';
import { useRoles, usePermissions } from '@/features/roles/roles-api';
import { useUserGroups } from '@/features/workflow/hooks/use-user-groups';
import { useWorkflowOrgUnits } from '@/features/workflow/hooks/use-org-units';

const { Text } = Typography;

// Task statuses that still await a handler. Enums serialize as strings (JsonStringEnumConverter):
// 'Open'/'InProgress' are active; 'Done'/'Cancelled' are excluded.
const ACTIVE_STATUSES = new Set(['Open', 'InProgress']);

// CandidatePrincipalType: 0=Permission, 1=Role, 2=Group, 3=OrgUnit.
const TYPE_PERMISSION = 0;
const TYPE_ROLE = 1;
const TYPE_GROUP = 2;
const TYPE_ORGUNIT = 3;

interface NextHandlerCardProps {
  workflowInstanceId: string;
}

export function NextHandlerCard({ workflowInstanceId }: NextHandlerCardProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { user } = useAuth();
  const currentUserId = (user?.profile?.sub ?? '').toLowerCase();
  const qc = useQueryClient();
  const claim = useClaimTask();
  const unclaim = useUnclaimTask();
  const { data: tasks, isLoading } = useTasksByInstance(workflowInstanceId);

  // Refresh the three views that depend on task ownership after a claim/unclaim flips it.
  const refreshTaskViews = () => {
    qc.invalidateQueries({ queryKey: workflowKeys.tasksByInstance(workflowInstanceId) });
    qc.invalidateQueries({ queryKey: workflowKeys.instance(workflowInstanceId) });
    qc.invalidateQueries({ queryKey: workflowKeys.availableTransitions(workflowInstanceId) });
  };

  const handleClaim = (taskId: string) => {
    claim.mutate(taskId, {
      onSuccess: () => {
        message.success(t('page.businessProcessRun.nextHandler.claimed', { defaultValue: 'Đã nhận việc' }));
        refreshTaskViews();
      },
      onError: () =>
        message.error(t('page.businessProcessRun.nextHandler.claimFailed', { defaultValue: 'Nhận việc thất bại' })),
    });
  };

  const handleUnclaim = (taskId: string) => {
    unclaim.mutate(taskId, {
      onSuccess: () => {
        message.success(t('page.businessProcessRun.nextHandler.unclaimed', { defaultValue: 'Đã trả việc' }));
        refreshTaskViews();
      },
      onError: () =>
        message.error(t('page.businessProcessRun.nextHandler.unclaimFailed', { defaultValue: 'Trả việc thất bại' })),
    });
  };

  // Bounded, cached lookup lists — fetched once, reused across renders. These are purely
  // cosmetic (candidate ref → display label); they hit admin/MFA-gated endpoints this
  // operational view's audience (e.g. GovOfficer) may not be authorised for. `background`
  // makes a 403 degrade to the raw ref (resolveLabel fallback) instead of hijacking the
  // page to a forbidden toast or the 2FA step-up screen.
  const { data: roles } = useRoles({ background: true });
  const { data: permissions } = usePermissions({ background: true });
  const { data: groups } = useUserGroups({ background: true });
  const { data: orgUnits } = useWorkflowOrgUnits({ background: true });

  // canonical-ref (UPPER) → display label maps. Role ref = NormalizedName; others = code.
  const roleMap = useMemo(
    () => new Map((roles ?? []).map((r) => [r.name.trim().toUpperCase(), r.name])),
    [roles],
  );
  const permMap = useMemo(
    () => new Map((permissions ?? []).map((p) => [p.code.trim().toUpperCase(), p.name])),
    [permissions],
  );
  const groupMap = useMemo(
    () => new Map((groups ?? []).map((g) => [g.code.trim().toUpperCase(), g.name])),
    [groups],
  );
  const orgUnitMap = useMemo(
    () => new Map((orgUnits ?? []).map((o) => [o.code.trim().toUpperCase(), o.name])),
    [orgUnits],
  );

  if (isLoading) {
    return <Spin />;
  }

  const activeTasks = (tasks ?? []).filter((tk) => ACTIVE_STATUSES.has(tk.status));

  if (activeTasks.length === 0) {
    return (
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description={t('page.businessProcessRun.nextHandler.none', {
          defaultValue: 'Không có bước đang chờ xử lý',
        })}
      />
    );
  }

  const typeLabel = (principalType: number): string => {
    switch (principalType) {
      case TYPE_PERMISSION:
        return t('page.businessProcessRun.nextHandler.permission', { defaultValue: 'Quyền' });
      case TYPE_ROLE:
        return t('page.businessProcessRun.nextHandler.role', { defaultValue: 'Vai trò' });
      case TYPE_GROUP:
        return t('page.businessProcessRun.nextHandler.group', { defaultValue: 'Nhóm' });
      case TYPE_ORGUNIT:
        return t('page.businessProcessRun.nextHandler.orgUnit', { defaultValue: 'Đơn vị' });
      default:
        return t('page.businessProcessRun.nextHandler.principal', { defaultValue: 'Đối tượng' });
    }
  };

  // Resolve a candidate ref to a display label; fall back to the raw ref if not in the cached list.
  const resolveLabel = (c: WorkflowTaskCandidateDto): string => {
    const key = c.principalRefId.trim().toUpperCase();
    const map =
      c.principalType === TYPE_PERMISSION
        ? permMap
        : c.principalType === TYPE_ROLE
          ? roleMap
          : c.principalType === TYPE_GROUP
            ? groupMap
            : c.principalType === TYPE_ORGUNIT
              ? orgUnitMap
              : undefined;
    return map?.get(key) ?? c.principalRefId;
  };

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      {activeTasks.map((tk) => (
        <div key={tk.id}>
          <Text strong>{tk.title}</Text>
          <div style={{ marginTop: 4 }}>
            {tk.assigneeId ? (
              tk.assigneeId.toLowerCase() === currentUserId ? (
                // Claimed by me — offer to release it back to the pool (reserve was optional;
                // acting via a transition auto-claims, so an explicit claim can be undone here).
                <Space size={[8, 4]} wrap align="center">
                  <Tag color="green">
                    {t('page.businessProcessRun.nextHandler.claimedByMe', { defaultValue: 'Bạn đã nhận' })}
                  </Tag>
                  <Button size="small" loading={unclaim.isPending} onClick={() => handleUnclaim(tk.id)}>
                    {t('page.businessProcessRun.nextHandler.unclaim', { defaultValue: 'Trả việc' })}
                  </Button>
                </Space>
              ) : (
                <Tag color="blue">
                  {tk.assigneeName ??
                    t('page.businessProcessRun.nextHandler.assigned', { defaultValue: 'Đã giao' })}
                </Tag>
              )
            ) : (
              <Space direction="vertical" size={4} style={{ width: '100%' }}>
                <Space size={[4, 4]} wrap>
                  <Tag color="orange">
                    {t('page.businessProcessRun.nextHandler.pending', { defaultValue: 'Chờ nhận' })}
                  </Tag>
                  {tk.candidates.length === 0 ? (
                    <Text type="secondary">—</Text>
                  ) : (
                    tk.candidates.map((c, i) => (
                      <Tag key={`${tk.id}-${i}`}>
                        {typeLabel(c.principalType)}: {resolveLabel(c)}
                      </Tag>
                    ))
                  )}
                </Space>
                {tk.eligibleUsers.length > 0 && (
                  <div>
                    <Text type="secondary">
                      {t('page.businessProcessRun.nextHandler.eligibleUsers', { defaultValue: 'Người có thể nhận' })}:
                    </Text>{' '}
                    <Text>{tk.eligibleUsers.map((u) => u.fullName ?? u.userId).join(', ')}</Text>
                    {tk.eligibleUserCount > tk.eligibleUsers.length && (
                      <Text type="secondary">
                        {' '}
                        {t('page.businessProcessRun.nextHandler.andMore', {
                          defaultValue: '… còn {{n}} người',
                          n: tk.eligibleUserCount - tk.eligibleUsers.length,
                        })}
                      </Text>
                    )}
                    <Text type="secondary">
                      {' '}
                      ({t('page.businessProcessRun.nextHandler.total', { defaultValue: 'tổng {{n}}', n: tk.eligibleUserCount })})
                    </Text>
                  </div>
                )}
                {tk.hasUnresolvedPermissionCandidate && (
                  <Text type="warning" style={{ fontSize: 12 }}>
                    {t('page.businessProcessRun.nextHandler.permissionNotListed', {
                      defaultValue: 'Ngoài ra còn người có quyền phù hợp (chưa liệt kê)',
                    })}
                  </Text>
                )}
                {tk.eligibleUsers.some((u) => u.userId.toLowerCase() === currentUserId) && (
                  <Button
                    size="small"
                    type="primary"
                    loading={claim.isPending}
                    onClick={() => handleClaim(tk.id)}
                  >
                    {t('page.businessProcessRun.nextHandler.claim', { defaultValue: 'Nhận việc' })}
                  </Button>
                )}
              </Space>
            )}
          </div>
        </div>
      ))}
    </Space>
  );
}
