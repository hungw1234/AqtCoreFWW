// copilot-sidebar — collapsible AI chat panel rendered as an Ant Design Drawer
// Accessible from any authenticated page via CopilotToggleButton.
// Uses useCopilotChat hook and shares sessionId from CopilotProvider.
import { Drawer, Button, Select, Space, Spin, Alert, Tooltip, Badge } from 'antd';
import { Input } from 'antd';
import { SendOutlined, RobotOutlined, ClearOutlined, CloseOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useCopilot } from './copilot-provider';
import { useCopilotChat } from './use-copilot-chat';
import { MessageBubble } from './copilot-message-bubble';
import { Typography } from 'antd';

const { TextArea } = Input;
const { Paragraph } = Typography;

// ─── CopilotSidebar ───────────────────────────────────────────────────────────

/** Ant Design Drawer containing the full chat UI — overlays page content */
export function CopilotSidebar() {
  const { t } = useTranslation();
  const { isOpen, toggle, sessionId } = useCopilot();

  const {
    messages,
    input,
    setInput,
    streaming,
    streamError,
    selectedModel,
    setSelectedModel,
    models,
    modelsLoading,
    handleSend,
    handleClear,
    handleKeyDown,
    bottomRef,
  } = useCopilotChat({ sessionId });

  return (
    <Drawer
      title={
        <Space>
          <RobotOutlined style={{ color: '#52c41a' }} />
          {t('copilot.sidebar.title')}
        </Space>
      }
      placement="right"
      width={400}
      open={isOpen}
      onClose={toggle}
      closeIcon={<CloseOutlined />}
      styles={{ body: { display: 'flex', flexDirection: 'column', padding: '12px 16px', height: '100%' } }}
      // Render into body so it overlays all content without disrupting layout
      getContainer={document.body}
    >
      {/* Model selector + clear */}
      <Space style={{ marginBottom: 12, justifyContent: 'space-between', display: 'flex', flexWrap: 'wrap' }}>
        <Select
          value={selectedModel}
          onChange={setSelectedModel}
          loading={modelsLoading}
          placeholder={t('copilot.selectModel')}
          style={{ flex: 1, minWidth: 140 }}
          options={models?.map((m) => ({ value: m.id, label: `${m.name} (${m.provider})` })) ?? []}
          size="small"
        />
        <Button
          size="small"
          icon={<ClearOutlined />}
          onClick={handleClear}
          disabled={messages.length === 0}
        >
          {t('copilot.clearBtn')}
        </Button>
      </Space>

      {/* Message list */}
      <div
        style={{
          flex: 1,
          overflowY: 'auto',
          border: '1px solid #f0f0f0',
          borderRadius: 8,
          padding: 12,
          background: '#fafafa',
          marginBottom: 12,
        }}
      >
        {messages.length === 0 && (
          <div style={{ textAlign: 'center', marginTop: 40 }}>
            <RobotOutlined style={{ fontSize: 40, color: '#bbb' }} />
            <Paragraph type="secondary" style={{ marginTop: 10, fontSize: 13 }}>
              {t('copilot.emptyHint')}
            </Paragraph>
          </div>
        )}
        {messages.map((msg) => (
          <MessageBubble key={msg.id} msg={msg} />
        ))}
        {streaming &&
          messages[messages.length - 1]?.role === 'assistant' &&
          messages[messages.length - 1]?.content === '' && (
            <Spin size="small" style={{ marginLeft: 40, marginBottom: 8 }} />
          )}
        {streamError && (
          <Alert type="error" message={streamError} showIcon style={{ marginTop: 8 }} />
        )}
        <div ref={bottomRef} />
      </div>

      {/* Input area */}
      <Space.Compact style={{ width: '100%' }}>
        <TextArea
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder={t('copilot.inputPlaceholder')}
          autoSize={{ minRows: 2, maxRows: 4 }}
          disabled={streaming}
          style={{ resize: 'none' }}
        />
        <Button
          type="primary"
          icon={<SendOutlined />}
          onClick={handleSend}
          disabled={!input.trim() || streaming}
          loading={streaming}
          style={{ height: 'auto', alignSelf: 'flex-end' }}
        />
      </Space.Compact>
      <span style={{ fontSize: 11, color: '#999', marginTop: 4 }}>
        {t('copilot.sendHint')}
      </span>
    </Drawer>
  );
}

// ─── CopilotToggleButton ──────────────────────────────────────────────────────

/** Floating circular button fixed at bottom-right — opens/closes the copilot sidebar */
export function CopilotToggleButton() {
  const { t } = useTranslation();
  const { toggle, isOpen } = useCopilot();

  return (
    <Tooltip title={t('copilot.sidebar.toggle')} placement="left">
      <Badge dot={false}>
        <Button
          type="primary"
          shape="circle"
          size="large"
          icon={<RobotOutlined />}
          onClick={toggle}
          aria-label={t('copilot.sidebar.toggle')}
          aria-expanded={isOpen}
          style={{
            position: 'fixed',
            right: 24,
            bottom: 24,
            zIndex: 1000,
            width: 48,
            height: 48,
            fontSize: 20,
            boxShadow: '0 4px 12px rgba(0,0,0,0.2)',
          }}
        />
      </Badge>
    </Tooltip>
  );
}
