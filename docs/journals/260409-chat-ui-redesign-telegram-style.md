# Chat UI Redesign: Telegram-Style Sidebar

**Date**: 2026-04-09 14:30  
**Severity**: Medium  
**Component**: Chat UI (BE + FE)  
**Status**: Resolved  

## What Happened

Three-phase redesign of conversation list UI to match Telegram-style sidebar: conversations with unread badges, contact search, and inline message previews. `/cook --auto` delivered full implementation + review in 20 minutes.

## The Brutal Truth

The pressure to ship fast collided with cross-module data complexity. Initial SQL approach (direct JOIN to identity schema) violated architectural boundaries. Switched mid-implementation to IModuleClient pattern — added latency but kept modules decoupled. Worth it, but revealed we need better patterns for bulk user resolution across service boundaries.

## Technical Details

**BE enrichment (ConversationDto):**
- LastMessagePreview, SenderName, UnreadCount, ParticipantNames
- OUTER APPLY + correlated subquery for unread count (perf risk — no index on read_at yet)
- IModuleClient bulk name resolution: 500 ID chunks, max 5000/call

**FE alignment:**
- PaginatedResult type mismatch fixed: BE returns flat array, FE expected paginated wrapper
- Time formatting: today HH:mm → yesterday (i18n) → DD/MM
- Contact search filters current user (RT-8)

**Code review caught 6 issues (all fixed):**
- Pre-existing SQL OFFSET bug in cursor pagination
- Hardcoded Vietnamese "Hôm qua" — replaced with i18n
- Dead code in handleContactClick
- SenderName null in SendMessageCommandHandler
- Timer cleanup on unmount missing (added)

## Root Cause Analysis

Naming debt: "last message" concept appeared in 3 queries (GetConversations, GetMessages, SearchMessages) with no shared abstraction. Each reimplemented unread count logic. IModuleClient call count exploded.

## Lessons Learned

1. **Cross-module queries need patterns.** Direct schema joins feel faster but break later. Batch resolution API saved us here.
2. **Type alignment is brittle.** BE returned flat array; FE DTO expected paginated wrapper. Caught by code review, not tests. Need integration tests.
3. **Unread count logic is sneaky.** Reappears everywhere — worth extracting to DB view or cached query.

## Next Steps

- **Critical:** MarkAsRead FE integration (unread counts grow forever without POST /messages/{id}/read)
- **High:** Conversation pagination (load more) — currently capped at 20
- **Deferred:** Auto-scroll UX (yanks users mid-history read on new messages)
- **Future:** Index on read_at + partition unread query for scaling

**Build:** Clean (0 errors/warnings)  
**Tests:** 60/60 pass (38 domain + 22 application)  
**Files:** 16 changed (11 code + 5 docs)
