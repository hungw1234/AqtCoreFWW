// Reusable transition history timeline component

import { Timeline, Typography } from 'antd';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import type { WorkflowInstanceHistoryDto } from '../workflow-types';

const { Text } = Typography;

interface Props {
  history: WorkflowInstanceHistoryDto[];
  states?: Map<string, string>; // stateId → displayName
}

/** Format ms duration as human-readable string */
function formatDuration(ms: number): string {
  if (ms < 60_000) return `${Math.round(ms / 1000)}s`;
  if (ms < 3_600_000) return `${Math.round(ms / 60_000)}m`;
  if (ms < 86_400_000) return `${Math.round(ms / 3_600_000)}h`;
  return `${Math.round(ms / 86_400_000)}d`;
}

export function TransitionTimeline({ history, states }: Props) {
  const { t } = useTranslation();

  if (!history.length) {
    return <Text type="secondary">{t('workflow.instance.noHistory')}</Text>;
  }

  const items = history.map((h, idx) => {
    // Prefer BE-enriched display names; fall back to the optional states map, then the raw id.
    const from = h.fromStateName ?? states?.get(h.fromStateId) ?? h.fromStateId;
    const to = h.toStateName ?? states?.get(h.toStateId) ?? h.toStateId;
    const action = h.actionLabel ?? h.actionName;
    const next = history[idx + 1];
    const durationMs = next
      ? dayjs(next.transitionedAt).diff(dayjs(h.transitionedAt))
      : null;

    return {
      key: h.id,
      label: dayjs(h.transitionedAt).format('DD/MM/YYYY HH:mm'),
      children: (
        <div>
          <Text strong>{action}</Text>
          <div>
            <Text type="secondary">{from}</Text>
            {' → '}
            <Text type="secondary">{to}</Text>
          </div>
          {h.actorName && (
            <div>
              <Text type="secondary" style={{ fontSize: 12 }}>
                {t('workflow.instance.by')}: {h.actorName}
              </Text>
            </div>
          )}
          {h.comment && <div><Text italic type="secondary">{h.comment}</Text></div>}
          {durationMs !== null && (
            <div>
              <Text type="secondary" style={{ fontSize: 11 }}>
                {t('workflow.instance.duration')}: {formatDuration(durationMs)}
              </Text>
            </div>
          )}
        </div>
      ),
    };
  });

  return <Timeline mode="left" items={items} />;
}
