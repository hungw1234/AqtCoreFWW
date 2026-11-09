# Orval SDK Migration Guide

## Current State
- 14 custom `{feature}-api.ts` files with hand-written TanStack Query hooks
- 32 orval-generated modules in `src/api/generated/` (auto-generated from OpenAPI)
- Both use `apiClient` from `src/core/api/api-client.ts`

## Migration Strategy: Progressive (Feature-by-Feature)

### When to migrate a feature
- When touching the feature's API hooks for a bug fix or new endpoint
- When the custom types diverge from backend (orval types are auto-generated)
- When adding new API calls — prefer orval hooks over hand-writing

### How to migrate one feature

**Example: notifications**

```typescript
// BEFORE (custom hook in notification-api.ts):
export function useNotifications(page = 1, pageSize = 20) {
  return useQuery({
    queryKey: ['notifications', 'list', page],
    queryFn: () => apiClient.get('/notifications', { params: { page, pageSize } }).then(r => r.data),
  });
}

// AFTER (orval hook from generated):
import { useGetApiV1Notifications } from '@/api/generated/notifications';

// Use directly in component — orval handles queryKey + queryFn
const { data, isLoading } = useGetApiV1Notifications({ page, pageSize });
```

### Migration checklist per feature
1. Check orval generated hook matches your endpoint
2. Compare types: custom `{Feature}Dto` vs orval schema types
3. Replace import in page component
4. Verify `queryClient.invalidateQueries` uses correct orval queryKey
5. Remove custom hook from `{feature}-api.ts` after all consumers migrated
6. Delete `{feature}-api.ts` when empty

### Features ready for migration (simple, 1:1 mapping)
- [ ] notifications (3 hooks)
- [ ] master-data (2 hooks)
- [ ] system-params (3 hooks)
- [ ] api-keys (3 hooks)

### Features needing review (complex, custom logic)
- [ ] cases (8 hooks, workflow mutations)
- [ ] forms (4 hooks, nested template structure)
- [ ] audit (3 hooks, multi-tab)
- [ ] reports (4 hooks, polling for execution status)
- [ ] dashboard (1 hook, aggregate DTO)

### Regenerating orval after API changes
```bash
cd web && npm run generate:api
```
This reads from `http://localhost:5001/openapi/v1.json` (Docker backend must be running).
