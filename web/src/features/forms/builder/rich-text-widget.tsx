// rich-text-widget.tsx — lazy-loaded Quill WYSIWYG editor for RichText fields
// F4 fix: DOMPurify sanitization on input
// F6 fix: ErrorBoundary fallback to textarea on chunk load failure
import { Component, lazy, Suspense } from 'react';
import type { ReactNode, ErrorInfo } from 'react';
import { Input, Spin, Button, Space, Typography } from 'antd';
import DOMPurify from 'dompurify';
import 'react-quill-new/dist/quill.snow.css';

const ReactQuill = lazy(() => import('react-quill-new'));

const MODULES = {
  toolbar: [
    ['bold', 'italic', 'underline'],
    [{ list: 'ordered' }, { list: 'bullet' }],
    ['link'],
    ['clean'],
  ],
};

const MAX_CONTENT_LENGTH = 100_000; // ~100KB HTML cap

/** Allowlist matches Quill toolbar capabilities — strips anything else */
const SANITIZE_CONFIG = {
  ALLOWED_TAGS: ['p', 'br', 'strong', 'em', 'u', 'ol', 'ul', 'li', 'a', 'span'],
  ALLOWED_ATTR: ['href', 'target', 'rel', 'class'],
};

/** Sanitize HTML content using DOMPurify before storing */
export function sanitizeRichText(html: string): string {
  return DOMPurify.sanitize(html, SANITIZE_CONFIG);
}

// ---------------------------------------------------------------------------
// ErrorBoundary — falls back to plain textarea if Quill chunk fails to load
// ---------------------------------------------------------------------------

interface ErrorBoundaryState { hasError: boolean }

class RichTextErrorBoundary extends Component<
  { children: ReactNode; value?: string; onChange?: (v: string) => void },
  ErrorBoundaryState
> {
  state: ErrorBoundaryState = { hasError: false };

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { hasError: true };
  }

  componentDidCatch(_: Error, info: ErrorInfo) {
    console.error('RichText chunk load failed:', info);
  }

  render() {
    if (this.state.hasError) {
      return (
        <Space direction="vertical" style={{ width: '100%' }}>
          <Typography.Text type="warning">
            Rich text editor failed to load.
          </Typography.Text>
          <Input.TextArea
            rows={4}
            value={this.props.value}
            onChange={(e) => this.props.onChange?.(e.target.value)}
            placeholder="Using plain text editor as fallback"
          />
          <Button
            size="small"
            onClick={() => this.setState({ hasError: false })}
          >
            Retry
          </Button>
        </Space>
      );
    }
    return this.props.children;
  }
}

// ---------------------------------------------------------------------------
// RichTextWidget — main export
// ---------------------------------------------------------------------------

interface RichTextWidgetProps {
  value?: string;
  onChange?: (html: string) => void;
  disabled?: boolean;
}

export function RichTextWidget({ value, onChange, disabled }: RichTextWidgetProps) {
  return (
    <RichTextErrorBoundary value={value} onChange={onChange}>
      <Suspense fallback={<Spin size="small" />}>
        <ReactQuill
          theme="snow"
          value={value ?? ''}
          onChange={(content) => {
            if (content.length > MAX_CONTENT_LENGTH) return;
            // Sanitize HTML on input — strips XSS vectors before storage
            const clean = sanitizeRichText(content);
            onChange?.(clean);
          }}
          readOnly={disabled}
          modules={MODULES}
        />
      </Suspense>
    </RichTextErrorBoundary>
  );
}
