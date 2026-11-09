// create-conversation-modal.tsx — modal to create Direct (1-1) or Group chat
// Auto-detects type: 1 user = Direct, 2+ = Group (shows optional group name)

import { useState, useRef, useCallback } from 'react';
import { Modal, Select, Input, Button, Typography, Space } from 'antd';
import { useTranslation } from 'react-i18next';
import { useUserSearch, type UserSearchResult } from './user-search-api';
import { useCreateConversation } from './chat-api';
import { appMessage } from '@/core/api/notification-bridge';

const { Text } = Typography;

interface CreateConversationModalProps {
  open: boolean;
  onClose: () => void;
  onCreated: (conversationId: string) => void;
}

export function CreateConversationModal({ open, onClose, onCreated }: CreateConversationModalProps) {
  const { t } = useTranslation();
  const [selectedUsers, setSelectedUsers] = useState<{ value: string; label: string }[]>([]);
  const [groupName, setGroupName] = useState('');
  const [searchText, setSearchText] = useState('');
  const debounceRef = useRef<ReturnType<typeof setTimeout>>(null);
  const [debouncedQuery, setDebouncedQuery] = useState('');

  const { data: searchResults, isLoading: isSearching } = useUserSearch(debouncedQuery, open);
  const createMutation = useCreateConversation();

  const isGroup = selectedUsers.length >= 2;

  // Debounced search — 300ms delay
  const handleSearch = useCallback((value: string) => {
    setSearchText(value);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => setDebouncedQuery(value), 300);
  }, []);

  const handleCreate = async () => {
    if (selectedUsers.length === 0) return;
    try {
      const result = await createMutation.mutateAsync({
        type: isGroup ? 'Group' : 'Direct',
        name: isGroup && groupName.trim() ? groupName.trim() : undefined,
        memberUserIds: selectedUsers.map((u) => u.value),
      });
      appMessage.success(t('chat.createSuccess', 'Đã tạo cuộc trò chuyện'));
      // Reset state
      setSelectedUsers([]);
      setGroupName('');
      setSearchText('');
      setDebouncedQuery('');
      onCreated((result as { id: string }).id);
    } catch {
      appMessage.error(t('chat.createError', 'Không thể tạo cuộc trò chuyện'));
    }
  };

  const handleClose = () => {
    setSelectedUsers([]);
    setGroupName('');
    setSearchText('');
    setDebouncedQuery('');
    onClose();
  };

  // Map search results to Select options
  const options = (searchResults ?? []).map((u: UserSearchResult) => ({
    value: u.id,
    label: u.departmentCode ? `${u.fullName} (${u.departmentCode})` : u.fullName,
  }));

  return (
    <Modal
      open={open}
      title={t('chat.newConversation', 'Cuộc trò chuyện mới')}
      onCancel={handleClose}
      footer={[
        <Button key="cancel" onClick={handleClose}>
          {t('common.cancel', 'Hủy')}
        </Button>,
        <Button
          key="create"
          type="primary"
          disabled={selectedUsers.length === 0}
          loading={createMutation.isPending}
          onClick={handleCreate}
        >
          {t('chat.create', 'Tạo')}
        </Button>,
      ]}
      width={500}
      destroyOnHidden
    >
      <Space direction="vertical" style={{ width: '100%' }} size="middle">
        <div>
          <Text strong style={{ display: 'block', marginBottom: 6 }}>
            {t('chat.selectUsers', 'Chọn người tham gia')}
          </Text>
          <Select
            mode="multiple"
            labelInValue
            value={selectedUsers}
            onChange={setSelectedUsers}
            searchValue={searchText}
            onSearch={handleSearch}
            options={options}
            loading={isSearching}
            filterOption={false}
            placeholder={t('chat.searchUsers', 'Tìm kiếm theo tên...')}
            notFoundContent={
              debouncedQuery.length >= 2 && !isSearching
                ? t('chat.noUsersFound', 'Không tìm thấy')
                : debouncedQuery.length > 0
                  ? t('chat.minSearchChars', 'Nhập ít nhất 2 ký tự')
                  : null
            }
            style={{ width: '100%' }}
          />
        </div>

        {isGroup && (
          <div>
            <Text strong style={{ display: 'block', marginBottom: 6 }}>
              {t('chat.groupName', 'Tên nhóm (tùy chọn)')}
            </Text>
            <Input
              value={groupName}
              onChange={(e) => setGroupName(e.target.value)}
              placeholder={t('chat.groupChat', 'Chat nhóm')}
              maxLength={100}
            />
          </div>
        )}

        {selectedUsers.length > 0 && (
          <Text type="secondary" style={{ fontSize: 12 }}>
            {isGroup
              ? t('chat.groupChat', 'Chat nhóm') + ` — ${selectedUsers.length} người`
              : t('chat.directChat', 'Chat riêng')}
          </Text>
        )}
      </Space>
    </Modal>
  );
}
