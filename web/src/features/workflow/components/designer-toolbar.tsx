// Designer toolbar — action buttons for the workflow visual designer
// Buttons: Add State, Auto Layout, Save, Undo

import { Button, Space, Tooltip } from 'antd';
import {
  PlusOutlined,
  SaveOutlined,
  UndoOutlined,
  ApartmentOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';

interface DesignerToolbarProps {
  onAddState: () => void;
  onAutoLayout: () => void;
  onSave: () => void;
  onUndo: () => void;
  hasChanges: boolean;
  saving: boolean;
}

export function DesignerToolbar({
  onAddState,
  onAutoLayout,
  onSave,
  onUndo,
  hasChanges,
  saving,
}: DesignerToolbarProps) {
  const { t } = useTranslation();

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '8px 0',
        borderBottom: '1px solid #f0f0f0',
        marginBottom: 8,
      }}
    >
      <Space>
        <Tooltip title={t('workflow.designer.addState', 'Thêm bước mới')}>
          <Button icon={<PlusOutlined />} onClick={onAddState} size="small">
            {t('workflow.designer.addState', 'Thêm bước')}
          </Button>
        </Tooltip>
        <Tooltip title={t('workflow.designer.autoLayout', 'Tự động sắp xếp các bước thành lưới')}>
          <Button icon={<ApartmentOutlined />} onClick={onAutoLayout} size="small">
            {t('workflow.designer.autoLayout', 'Tự động sắp xếp')}
          </Button>
        </Tooltip>
      </Space>

      <Space>
        <Tooltip title={t('workflow.designer.undo', 'Hoàn tác thay đổi')}>
          <Button
            icon={<UndoOutlined />}
            onClick={onUndo}
            size="small"
            disabled={!hasChanges}
          >
            {t('workflow.designer.undo', 'Hoàn tác')}
          </Button>
        </Tooltip>
        <Tooltip title={hasChanges ? t('workflow.designer.saveHint', 'Có thay đổi chưa lưu') : ''}>
          <Button
            type={hasChanges ? 'primary' : 'default'}
            icon={<SaveOutlined />}
            onClick={onSave}
            size="small"
            loading={saving}
            disabled={!hasChanges}
          >
            {t('common.save', 'Lưu')}
          </Button>
        </Tooltip>
      </Space>
    </div>
  );
}
