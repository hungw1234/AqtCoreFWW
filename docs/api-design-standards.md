# API Design Standards
**Version:** 1.0 | **Date:** 2026-03-05 | **Framework:** AqtCoreFW GOV .NET

Mandatory API conventions for all modules in AqtCoreFW. All controllers MUST follow these standards.

---

## 1. URL Structure
```
/api/v{version}/{resource}              # collection
/api/v{version}/{resource}/{id}         # single resource
/api/v{version}/{resource}/{id}/{sub}   # sub-resource
/api/v{version}/admin/{resource}        # admin-only
/api/v{version}/public/{resource}       # anonymous public
```

## 2. Versioning
- Package: `Asp.Versioning.Mvc`
- Attribute: `[ApiVersion("1.0")]` on all controllers
- Breaking changes: add new ApiVersion, mark old `Deprecated = true`
- Default version: 1.0

## 3. Response Envelope — ApiResponse<T>
All endpoints return `ApiResponse<T>`:
```json
{
  "success": true,
  "data": { ... },
  "meta": {
    "requestId": "00-abc-def-00",
    "timestamp": "2024-01-15T08:30:00Z",
    "pagination": { "page": 1, "pageSize": 20, "total": 150, "totalPages": 8 }
  },
  "errors": null
}
```
Exception: file download endpoints return raw binary.

## 4. Pagination

### Offset (default — for pageable screens)
```
GET /api/v1/forms/templates?page=1&pageSize=20
```
- `pageSize` max: 100
- Response meta includes: page, pageSize, total, totalPages

### Cursor (opt-in — for large datasets)
```
GET /api/v1/audit/logs?cursor=eyJpZCI6MTAwfQ&limit=50
```
- `limit` max: 200
- Response meta includes: cursor.next, cursor.hasMore

## 5. Filtering & Sorting
```
GET /api/v1/forms/templates?status=pending,processing&createdAfter=2024-01-01T00:00:00Z&sort=-createdAt,name
```
- Multiple filter values: comma-separated = OR
- Multiple sort fields: comma-separated, `-` prefix = DESC
- Exact string match: `?name=exact:Nguyen Van A`
- All date params: ISO 8601 RFC 3339 UTC

## 6. Bulk Operations
```
POST /api/v1/{resource}/bulk
Body: { "operations": [{op, id?, data?}], "stopOnError": true }
```
- Atomic by default (stopOnError = true)
- Non-atomic mode: all ops attempted, partial success reported

## 7. Error Format — RFC 9457
```json
{
  "type": "https://errors.aqt.gov.vn/{error-slug}",
  "title": "Human readable",
  "status": 422,
  "detail": "English detail",
  "detail_vi": "Chi tiết tiếng Việt",
  "errorCode": "GOV_XXX_NNN",
  "instance": "/api/v1/forms/templates",
  "traceId": "00-abc-def-00",
  "errors": [{ "field": "x", "message": "y", "code": "GOV_VAL_001" }]
}
```

## 8. Standard Headers
| Header | Direction | Notes |
|--------|-----------|-------|
| `X-Request-Id` | Both | Auto-generated if absent |
| `X-Idempotency-Key` | Request | UUID, POST operations |
| `X-RateLimit-*` | Response | Limit/Remaining/Reset |
| `Accept-Language` | Request | `vi` default, `en` supported |
| `ETag` | Response | For conditional GET |
| `Sunset` | Response | Deprecated API versions |
| `Location` | Response | 201 Created |

## 9. HTTP Status Codes
| Code | Meaning |
|------|---------|
| 200 | Success with body |
| 201 | Created (+ Location header) |
| 204 | Success no body |
| 304 | Not Modified |
| 400 | Malformed request |
| 401 | Not authenticated |
| 403 | Not authorized |
| 404 | Not found |
| 409 | Conflict / optimistic lock |
| 410 | Gone (RTBF applied) |
| 422 | Validation/domain error |
| 429 | Rate limited |
| 503 | Service unavailable |

## 10. Date/Time
- Format: ISO 8601 RFC 3339 UTC — `2024-01-15T08:30:00Z`
- All API inputs/outputs in UTC
- Timezone conversion: FE responsibility

## 11. Naming Conventions
- URLs: kebab-case — `/api/v1/case-types`
- JSON keys: camelCase — `{ "createdAt": "..." }`
- Enum values in JSON: PascalCase — `"status": "InProgress"`
- Boolean fields: `is` prefix — `isActive`, `isDeleted`
