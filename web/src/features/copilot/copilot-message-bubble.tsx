// copilot-message-bubble — shared message rendering component for user/assistant chat bubbles
import { RobotOutlined, UserOutlined } from '@ant-design/icons';
import type { CopilotMessage } from './copilot-api';

interface MessageBubbleProps {
  msg: CopilotMessage;
}

/** Renders a single user or assistant message with distinct bubble styling */
export function MessageBubble({ msg }: MessageBubbleProps) {
  const isUser = msg.role === 'user';
  return (
    <div
      style={{
        display: 'flex',
        flexDirection: isUser ? 'row-reverse' : 'row',
        alignItems: 'flex-start',
        gap: 8,
        marginBottom: 12,
      }}
    >
      {/* Avatar icon */}
      <div
        style={{
          width: 32,
          height: 32,
          borderRadius: '50%',
          background: isUser ? '#1677ff' : '#52c41a',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          color: '#fff',
          flexShrink: 0,
          fontSize: 16,
        }}
      >
        {isUser ? <UserOutlined /> : <RobotOutlined />}
      </div>

      {/* Bubble */}
      <div
        style={{
          maxWidth: '72%',
          padding: '8px 12px',
          borderRadius: isUser ? '12px 4px 12px 12px' : '4px 12px 12px 12px',
          background: isUser ? '#1677ff' : '#f5f5f5',
          color: isUser ? '#fff' : 'inherit',
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
          fontSize: 14,
          lineHeight: '1.6',
        }}
      >
        {msg.content}
        {/* Streaming cursor — shown while assistant message is still empty */}
        {msg.role === 'assistant' && msg.content === '' && (
          <span
            style={{
              display: 'inline-block',
              width: 8,
              height: 14,
              background: '#999',
              marginLeft: 2,
              verticalAlign: 'middle',
              animation: 'blink 1s step-end infinite',
            }}
          />
        )}
      </div>
    </div>
  );
}
