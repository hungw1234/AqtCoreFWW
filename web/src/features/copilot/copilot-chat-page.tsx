// CopilotChatPage — standalone full-page AI copilot chat
// Chat logic delegated to useCopilotChat; MessageBubble from shared component.
import { Input, Button, Select, Typography, Space, Spin, Alert } from 'antd';
import { SendOutlined, RobotOutlined, ClearOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useCopilotChat } from './use-copilot-chat';
import { MessageBubble } from './copilot-message-bubble';
import { DeferredFeatureAlert } from '@/shared/components/deferred-feature-alert';

const { Title, Text, Paragraph } = Typography;
const { TextArea } = Input;

/** Full-page copilot chat — accessible at /copilot route */
export function CopilotChatPage() {
  const { t } = useTranslation();

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
  } = useCopilotChat();

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: 'calc(100vh - 120px)', padding: '0 4px' }}>
      <DeferredFeatureAlert feature="Copilot" />
      {/* Header */}
      <Space style={{ marginBottom: 12, justifyContent: 'space-between', display: 'flex' }}>
        <Space>
          <RobotOutlined style={{ fontSize: 20, color: '#52c41a' }} />
          <Title level={4} style={{ margin: 0 }}>{t('copilot.title')}</Title>
        </Space>
        <Space>
          <Select
            value={selectedModel}
            onChange={setSelectedModel}
            loading={modelsLoading}
            placeholder={t('copilot.selectModel')}
            style={{ width: 200 }}
            options={models?.map((m) => ({ value: m.id, label: `${m.name} (${m.provider})` })) ?? []}
          />
          <Button icon={<ClearOutlined />} onClick={handleClear} disabled={messages.length === 0}>
            {t('copilot.clearBtn')}
          </Button>
        </Space>
      </Space>

      {/* Message list */}
      <div
        style={{
          flex: 1,
          overflowY: 'auto',
          border: '1px solid #f0f0f0',
          borderRadius: 8,
          padding: '16px',
          background: '#fafafa',
          marginBottom: 12,
        }}
      >
        {messages.length === 0 && (
          <div style={{ textAlign: 'center', marginTop: 60 }}>
            <RobotOutlined style={{ fontSize: 48, color: '#bbb' }} />
            <Paragraph type="secondary" style={{ marginTop: 12 }}>
              {t('copilot.emptyHint')}
            </Paragraph>
          </div>
        )}
        {messages.map((msg) => (
          <MessageBubble key={msg.id} msg={msg} />
        ))}
        {streaming && messages[messages.length - 1]?.role === 'assistant' && messages[messages.length - 1]?.content === '' && (
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
          autoSize={{ minRows: 2, maxRows: 5 }}
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
        >
          {t('copilot.sendBtn')}
        </Button>
      </Space.Compact>
      <Text type="secondary" style={{ fontSize: 12, marginTop: 4 }}>
        {t('copilot.sendHint')}
      </Text>
    </div>
  );
}
