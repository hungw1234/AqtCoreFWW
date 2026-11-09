// Tests for RichText DOMPurify sanitization (F4)
import { describe, it, expect } from 'vitest';
import { sanitizeRichText } from '../builder/rich-text-widget';

describe('sanitizeRichText', () => {
  it('preserves allowed tags (p, strong, em, u, ol, ul, li, a)', () => {
    const html = '<p><strong>Bold</strong> <em>italic</em> <u>underline</u></p>';
    expect(sanitizeRichText(html)).toBe(html);
  });

  it('preserves links with href', () => {
    const html = '<a href="https://example.com">Link</a>';
    expect(sanitizeRichText(html)).toContain('href="https://example.com"');
  });

  it('strips script tags', () => {
    const html = '<p>Hello</p><script>alert("xss")</script>';
    expect(sanitizeRichText(html)).toBe('<p>Hello</p>');
  });

  it('strips img onerror XSS', () => {
    const html = '<img src=x onerror=alert(1)>';
    expect(sanitizeRichText(html)).toBe('');
  });

  it('strips event handlers from allowed tags', () => {
    const html = '<p onclick="alert(1)">Text</p>';
    expect(sanitizeRichText(html)).toBe('<p>Text</p>');
  });

  it('strips iframe tags', () => {
    const html = '<iframe src="https://evil.com"></iframe><p>Safe</p>';
    expect(sanitizeRichText(html)).toBe('<p>Safe</p>');
  });

  it('handles empty/null-ish input', () => {
    expect(sanitizeRichText('')).toBe('');
  });

  it('preserves ordered and unordered lists', () => {
    const html = '<ol><li>One</li></ol><ul><li>Two</li></ul>';
    expect(sanitizeRichText(html)).toBe(html);
  });
});
