// useCopilotChat — shared chat logic for full-page and sidebar copilot views
import { useState, useRef, useEffect, useCallback } from 'react';
import { useAiModelProfiles, streamCopilotMessage, type CopilotMessage } from './copilot-api';

// Unique ID generator for local messages
let msgCounter = 0;
function nextId() {
  return `msg-${++msgCounter}-${Date.now()}`;
}

export interface UseCopilotChatOptions {
  /** Optionally supply a sessionId to reuse across mounts (e.g. from CopilotProvider) */
  sessionId?: string;
}

export interface UseCopilotChatReturn {
  messages: CopilotMessage[];
  input: string;
  setInput: (v: string) => void;
  streaming: boolean;
  streamError: string | null;
  selectedModel: string | undefined;
  setSelectedModel: (id: string) => void;
  models: import('./copilot-api').AiModelProfile[] | undefined;
  modelsLoading: boolean;
  sessionId: string;
  handleSend: () => void;
  handleClear: () => void;
  handleKeyDown: (e: React.KeyboardEvent) => void;
  bottomRef: React.RefObject<HTMLDivElement | null>;
}

/** Hook encapsulating all copilot chat state and streaming logic */
export function useCopilotChat(options?: UseCopilotChatOptions): UseCopilotChatReturn {

  // Use provided sessionId or generate a stable one per hook instance
  const [sessionId] = useState(() => options?.sessionId ?? crypto.randomUUID());
  const [messages, setMessages] = useState<CopilotMessage[]>([]);
  const [input, setInput] = useState('');
  const [selectedModel, setSelectedModel] = useState<string | undefined>(undefined);
  const [streaming, setStreaming] = useState(false);
  const [streamError, setStreamError] = useState<string | null>(null);

  const bottomRef = useRef<HTMLDivElement>(null);
  const cancelRef = useRef<(() => void) | null>(null);

  const { data: models, isLoading: modelsLoading } = useAiModelProfiles();

  // Set default model once profiles load
  useEffect(() => {
    if (models && !selectedModel) {
      const def = models.find((m) => m.isDefault) ?? models[0];
      if (def) setSelectedModel(def.id);
    }
  }, [models, selectedModel]);

  // Auto-scroll to latest message
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  // Append streaming token to last assistant message
  const appendToken = useCallback((token: string) => {
    setMessages((prev) => {
      const last = prev[prev.length - 1];
      if (!last || last.role !== 'assistant') return prev;
      return [...prev.slice(0, -1), { ...last, content: last.content + token }];
    });
  }, []);

  function handleSend() {
    const text = input.trim();
    if (!text || streaming) return;

    setInput('');
    setStreamError(null);

    const userMsg: CopilotMessage = {
      id: nextId(),
      role: 'user',
      content: text,
      createdAt: new Date().toISOString(),
    };
    const assistantMsg: CopilotMessage = {
      id: nextId(),
      role: 'assistant',
      content: '',
      createdAt: new Date().toISOString(),
    };

    setMessages((prev) => [...prev, userMsg, assistantMsg]);
    setStreaming(true);

    cancelRef.current = streamCopilotMessage(
      { sessionId, message: text, modelProfileId: selectedModel },
      appendToken,
      () => setStreaming(false),
      (err) => {
        setStreaming(false);
        setStreamError(err.message);
        // Remove empty assistant placeholder on error
        setMessages((prev) => {
          const last = prev[prev.length - 1];
          if (last?.role === 'assistant' && last.content === '') return prev.slice(0, -1);
          return prev;
        });
      },
    );
  }

  function handleClear() {
    if (cancelRef.current) cancelRef.current();
    setMessages([]);
    setStreaming(false);
    setStreamError(null);
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
      e.preventDefault();
      handleSend();
    }
  }

  return {
    messages,
    input,
    setInput,
    streaming,
    streamError,
    selectedModel,
    setSelectedModel,
    models,
    modelsLoading,
    sessionId,
    handleSend,
    handleClear,
    handleKeyDown,
    bottomRef,
  };
}
