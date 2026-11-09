import { describe, it, expect } from 'vitest';
import { readerNames, type ReadReceiptDto } from '@/features/collaboration/chat-api';

const ME = '11111111-1111-1111-1111-111111111111';
const ALICE = '22222222-2222-2222-2222-222222222222';
const BOB = '33333333-3333-3333-3333-333333333333';

function receipt(userId: string, fullName?: string): ReadReceiptDto {
  return { userId, fullName, readAt: '2026-06-23T10:00:00Z' };
}

describe('readerNames (chat "seen by")', () => {
  it('returns empty when there are no receipts', () => {
    expect(readerNames(undefined, ME)).toEqual([]);
    expect(readerNames([], ME)).toEqual([]);
  });

  it('excludes the current user (no "seen by yourself")', () => {
    const names = readerNames([receipt(ME, 'Me'), receipt(ALICE, 'Alice')], ME);
    expect(names).toEqual(['Alice']);
  });

  it('lists other readers by full name', () => {
    const names = readerNames([receipt(ALICE, 'Alice'), receipt(BOB, 'Bob')], ME);
    expect(names).toEqual(['Alice', 'Bob']);
  });

  it('falls back to a short id slice when a name is missing or blank', () => {
    const names = readerNames([receipt(ALICE, '   '), receipt(BOB)], ME);
    expect(names).toEqual([ALICE.slice(0, 8), BOB.slice(0, 8)]);
  });
});
