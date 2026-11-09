import { useState, useRef, useEffect, useCallback } from 'react';
import { Layout, List, Typography, Input, Button, Space, Badge, Empty, Spin } from 'antd';
import { appMessage } from '@/core/api/notification-bridge';
import {
  SendOutlined, UserOutlined, TeamOutlined, PlusOutlined, SearchOutlined, CloseCircleFilled,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useAuth } from '@/features/auth/use-auth';
import {
  useConversations, useChatMessages, useSendMessage, useCreateConversation, useMarkAsRead,
  useMessageReadReceipts, readerNames,
  type ConversationDto, type ChatMessageDto,
} from './chat-api';
import { useUserSearch, type UserSearchResult } from './user-search-api';
import { useChatSignalR } from './use-chat-signalr';
import { CreateConversationModal } from './create-conversation-modal';

const { Sider, Content } = Layout;
const { Text, Title, Paragraph } = Typography;
const { TextArea } = Input;

// ─── Helpers ─────────────────────────────────────────────────────────────────

/** Display name for conversation: Group → name, Direct → other participant */
function convDisplayName(conv: ConversationDto): string {
  if (conv.type === 'Direct' && conv.participantNames?.length)
    return conv.participantNames[0];
  return conv.name || conv.participantNames?.join(', ') || '';
}

/** Format time: today → HH:mm, yesterday → i18n key, older → DD/MM */
function formatTime(iso: string | undefined, yesterday: string): string {
  if (!iso) return '';
  const d = dayjs(iso);
  const now = dayjs();
  if (d.isSame(now, 'day')) return d.format('HH:mm');
  if (d.isSame(now.subtract(1, 'day'), 'day')) return yesterday;
  return d.format('DD/MM');
}

// ─── Conversation sidebar (Telegram-style) ───────────────────────────────────

interface ConversationListProps {
  conversations: ConversationDto[] | undefined;
  isLoading: boolean;
  selected: string | null;
  onSelect: (id: string) => void;
  onContactClick: (userId: string) => void;
  hasNextPage?: boolean;
  isFetchingNextPage?: boolean;
  onLoadMore?: () => void;
}

function ConversationList({ conversations, isLoading, selected, onSelect, onContactClick, hasNextPage, isFetchingNextPage, onLoadMore }: ConversationListProps) {
  const { t } = useTranslation();
  const [searchText, setSearchText] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  const debounceRef = useRef<ReturnType<typeof setTimeout>>(null);
  const yesterdayLabel = t('chat.yesterday', 'Hôm qua');

  // Cleanup debounce timer on unmount
  useEffect(() => () => { if (debounceRef.current) clearTimeout(debounceRef.current); }, []);

  const isSearchMode = debouncedQuery.length >= 2;
  const { data: contacts, isLoading: isSearching } = useUserSearch(debouncedQuery, isSearchMode);

  const handleSearchChange = (value: string) => {
    setSearchText(value);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => setDebouncedQuery(value), 300);
  };

  const clearSearch = () => {
    setSearchText('');
    setDebouncedQuery('');
  };

  return (
    <>
      {/* Search bar */}
      <div style={{ padding: '8px 12px' }}>
        <Input
          prefix={<SearchOutlined style={{ color: '#bfbfbf' }} />}
          suffix={searchText ? <CloseCircleFilled style={{ color: '#bfbfbf', cursor: 'pointer' }} onClick={clearSearch} /> : null}
          placeholder={t('chat.searchPlaceholder', 'Tìm kiếm...')}
          value={searchText}
          onChange={(e) => handleSearchChange(e.target.value)}
          allowClear={false}
          size="small"
        />
      </div>

      {/* Search mode → contacts list */}
      {isSearchMode ? (
        isSearching ? <Spin style={{ display: 'block', margin: '24px auto' }} /> :
        !contacts?.length ? <Empty description={t('chat.noUsersFound')} style={{ marginTop: 40 }} /> :
        <List<UserSearchResult>
          dataSource={contacts}
          renderItem={(user) => (
            <List.Item
              key={user.id}
              onClick={() => { onContactClick(user.id); clearSearch(); }}
              style={{ cursor: 'pointer', padding: '8px 16px' }}
            >
              <List.Item.Meta
                avatar={<UserOutlined style={{ fontSize: 18, color: '#1677ff' }} />}
                title={<Text ellipsis style={{ maxWidth: 170 }}>{user.fullName}</Text>}
                description={user.departmentCode && <Text type="secondary" style={{ fontSize: 12 }}>{user.departmentCode}</Text>}
              />
            </List.Item>
          )}
        />
      ) : (
        /* Default mode → conversations list */
        isLoading ? <Spin style={{ display: 'block', margin: '24px auto' }} /> :
        !conversations?.length ?
          <Empty description={t('chat.searchContacts', 'Tìm kiếm đồng nghiệp để bắt đầu nhắn tin')} style={{ marginTop: 40 }} /> :
        <>
          <List<ConversationDto>
            dataSource={conversations}
            renderItem={(conv) => {
              const subtitle = conv.lastMessagePreview
                ? (conv.lastMessageSenderName ? `${conv.lastMessageSenderName}: ${conv.lastMessagePreview}` : conv.lastMessagePreview)
                : t('chat.noMessages');
              return (
                <List.Item
                  key={conv.id}
                  onClick={() => onSelect(conv.id)}
                  style={{
                    cursor: 'pointer', padding: '10px 16px',
                    background: selected === conv.id ? '#e6f4ff' : 'transparent',
                    borderLeft: selected === conv.id ? '3px solid #1677ff' : '3px solid transparent',
                  }}
                >
                  <List.Item.Meta
                    avatar={
                      <Badge count={conv.unreadCount} size="small" offset={[-2, 2]}>
                        {conv.type === 'Group'
                          ? <TeamOutlined style={{ fontSize: 20, color: '#1677ff' }} />
                          : <UserOutlined style={{ fontSize: 20, color: '#1677ff' }} />}
                      </Badge>
                    }
                    title={
                      <Text strong={conv.unreadCount > 0} ellipsis style={{ maxWidth: 150 }}>
                        {convDisplayName(conv)}
                      </Text>
                    }
                    description={<Text type="secondary" ellipsis style={{ fontSize: 12, maxWidth: 150 }}>{subtitle}</Text>}
                  />
                  <Text type="secondary" style={{ fontSize: 11, flexShrink: 0 }}>
                    {formatTime(conv.lastMessageAt, yesterdayLabel)}
                  </Text>
                </List.Item>
              );
            }}
          />
          {hasNextPage && (
            <div style={{ textAlign: 'center', padding: '8px 0' }}>
              <Button type="link" size="small" loading={isFetchingNextPage} onClick={onLoadMore}>
                {t('chat.loadMore', 'Xem thêm...')}
              </Button>
            </div>
          )}
        </>
      )}
    </>
  );
}

// ─── Message panel ────────────────────────────────────────────────────────────

interface MessagePanelProps {
  conversationId: string;
  conversationTitle: string;
}

function MessagePanel({ conversationId, conversationTitle }: MessagePanelProps) {
  const { t } = useTranslation();
  const { user } = useAuth();
  const currentUserId = (user?.profile as Record<string, string>)?.sub ?? '';

  const [input, setInput] = useState('');
  const [localMessages, setLocalMessages] = useState<ChatMessageDto[]>([]);
  const bottomRef = useRef<HTMLDivElement>(null);

  const { data, isLoading } = useChatMessages(conversationId, { pageSize: 50 });
  const sendMutation = useSendMessage(conversationId);
  const markAsRead = useMarkAsRead();

  const apiMessages = data ?? [];
  const allMessages = [
    ...apiMessages,
    ...localMessages.filter((lm) => !apiMessages.find((am) => am.id === lm.id)),
  ].sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());

  // Mark last message as read when messages load or conversation changes
  const lastMessageId = allMessages[allMessages.length - 1]?.id;
  useEffect(() => {
    if (lastMessageId && conversationId) {
      markAsRead.mutate({ messageId: lastMessageId, conversationId });
    }
  }, [lastMessageId, conversationId]); // eslint-disable-line react-hooks/exhaustive-deps

  // "Seen by" — receipts accrue on the newest message (the only one the client marks read), so we
  // query just that one. Exclude the current user; show the others' names under the last message.
  const { data: readReceipts } = useMessageReadReceipts(lastMessageId);
  const seenByNames = readerNames(readReceipts, currentUserId);
  const seenByText = seenByNames.length > 0 ? t('chat.seenBy', { names: seenByNames.join(', ') }) : '';

  const handleRealTimeMessage = useCallback((msg: ChatMessageDto) => {
    setLocalMessages((prev) => prev.find((m) => m.id === msg.id) ? prev : [...prev, msg]);
  }, []);

  useChatSignalR({ conversationId, onMessage: handleRealTimeMessage });

  useEffect(() => { bottomRef.current?.scrollIntoView({ behavior: 'smooth' }); }, [allMessages.length]);
  useEffect(() => { setLocalMessages([]); }, [conversationId]);

  async function handleSend() {
    const text = input.trim();
    if (!text) return;
    setInput('');
    try { await sendMutation.mutateAsync({ content: text }); }
    catch { appMessage.error(t('chat.sendError')); setInput(text); }
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') { e.preventDefault(); handleSend(); }
  }

  if (isLoading) return <Spin style={{ display: 'block', margin: '40px auto' }} />;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      {/* Header */}
      <div style={{ padding: '10px 16px', borderBottom: '1px solid #f0f0f0' }}>
        <Text strong style={{ fontSize: 15 }}>{conversationTitle}</Text>
      </div>
      {/* Messages */}
      <div style={{ flex: 1, overflowY: 'auto', padding: '16px 12px' }}>
        {allMessages.length === 0 && <Empty description={t('chat.noMessages')} style={{ marginTop: 60 }} />}
        {allMessages.map((msg) => {
          const isMine = msg.senderId === currentUserId;
          return (
            <div key={msg.id} style={{ display: 'flex', flexDirection: isMine ? 'row-reverse' : 'row', gap: 8, marginBottom: 10 }}>
              <div style={{ maxWidth: '68%' }}>
                {!isMine && (
                  <Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 2 }}>
                    {msg.senderName}
                  </Text>
                )}
                <div style={{
                  padding: '8px 12px',
                  borderRadius: isMine ? '12px 4px 12px 12px' : '4px 12px 12px 12px',
                  background: isMine ? '#1677ff' : '#f0f0f0',
                  color: isMine ? '#fff' : 'inherit',
                  fontSize: 14, whiteSpace: 'pre-wrap', wordBreak: 'break-word',
                }}>{msg.content}</div>
                <Text type="secondary" style={{ fontSize: 11, display: 'block', textAlign: isMine ? 'right' : 'left', marginTop: 2 }}>
                  {dayjs(msg.createdAt).format('HH:mm')}
                </Text>
                {msg.id === lastMessageId && seenByText && (
                  <Text type="secondary" style={{ fontSize: 11, fontStyle: 'italic', display: 'block', textAlign: isMine ? 'right' : 'left', marginTop: 1 }}>
                    {seenByText}
                  </Text>
                )}
              </div>
            </div>
          );
        })}
        <div ref={bottomRef} />
      </div>
      {/* Input */}
      <div style={{ borderTop: '1px solid #f0f0f0', padding: '8px 12px' }}>
        <Space.Compact style={{ width: '100%' }}>
          <TextArea
            value={input} onChange={(e) => setInput(e.target.value)} onKeyDown={handleKeyDown}
            placeholder={t('chat.inputPlaceholder')} autoSize={{ minRows: 1, maxRows: 4 }} style={{ resize: 'none' }}
          />
          <Button type="primary" icon={<SendOutlined />} onClick={handleSend}
            disabled={!input.trim()} loading={sendMutation.isPending} style={{ height: 'auto' }} />
        </Space.Compact>
        <Text type="secondary" style={{ fontSize: 11, marginTop: 4, display: 'block' }}>{t('chat.sendHint')}</Text>
      </div>
    </div>
  );
}

// ─── Main chat page ───────────────────────────────────────────────────────────

export function ChatPage() {
  const { t } = useTranslation();
  const [selectedConvId, setSelectedConvId] = useState<string | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const { data, isLoading, hasNextPage, fetchNextPage, isFetchingNextPage } = useConversations();
  const conversations = data?.pages.flat();
  const createMutation = useCreateConversation();

  // Contact click → BE create endpoint returns existing conversation for Direct if duplicate
  const handleContactClick = async (userId: string) => {
    try {
      const result = await createMutation.mutateAsync({
        type: 'Direct', memberUserIds: [userId],
      });
      setSelectedConvId((result as { id: string }).id);
    } catch {
      appMessage.error(t('chat.createError'));
    }
  };

  const selectedConv = conversations?.find((c) => c.id === selectedConvId);
  const conversationTitle = selectedConv ? convDisplayName(selectedConv) : '';

  return (
    <div style={{ padding: '0 4px' }}>
      <Title level={4} style={{ marginBottom: 12 }}>{t('chat.title')}</Title>
      <Layout style={{
        height: 'calc(100vh - 160px)', border: '1px solid #f0f0f0',
        borderRadius: 8, overflow: 'hidden', background: '#fff',
      }}>
        <Sider width={280} style={{ background: '#fff', borderRight: '1px solid #f0f0f0', overflowY: 'auto' }}>
          <div style={{
            padding: '10px 12px', borderBottom: '1px solid #f0f0f0',
            display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          }}>
            <Text strong>{t('chat.conversationsLabel')}</Text>
            <Button type="text" icon={<PlusOutlined />} size="small"
              onClick={() => setShowCreateModal(true)}
              title={t('chat.newGroup', 'Tạo nhóm mới')} />
          </div>
          <ConversationList
            conversations={conversations} isLoading={isLoading}
            selected={selectedConvId} onSelect={setSelectedConvId}
            onContactClick={handleContactClick}
            hasNextPage={hasNextPage} isFetchingNextPage={isFetchingNextPage}
            onLoadMore={() => fetchNextPage()}
          />
        </Sider>
        <Content style={{ background: '#fff', overflow: 'hidden' }}>
          {selectedConvId ? (
            <MessagePanel conversationId={selectedConvId} conversationTitle={conversationTitle} />
          ) : (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
              <Paragraph type="secondary">{t('chat.selectConversation')}</Paragraph>
            </div>
          )}
        </Content>
      </Layout>
      <CreateConversationModal
        open={showCreateModal}
        onClose={() => setShowCreateModal(false)}
        onCreated={(id) => { setSelectedConvId(id); setShowCreateModal(false); }}
      />
    </div>
  );
}
