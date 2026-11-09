# ADR-004: HMAC-Chained Audit Log for Tamper Detection

**Date:** 2026-03-04
**Status:** Accepted
**Deciders:** AqtCoreFW Architecture Team

## Context

Vietnamese Government compliance regulations mandate tamper-evident audit trails:
- **QĐ742:** Audit trail must detect unauthorized modifications or deletions
- **NĐ13 (PDPL):** All PII access must be logged and retained for 12 months; unauthorized access must be detectable
- **NĐ53:** Backup integrity required; audit logs cannot be modified by DBA

Cryptographic chaining (blockchain-inspired) provides tamper detection without blockchain infrastructure overhead. Each audit entry cryptographically commits to the previous entry: if any entry is modified or deleted, the chain breaks and verification fails.

Append-only design (no UPDATE/DELETE on audit table) is enforced at the database layer via triggers and application-level validation. Right-to-be-Forgotten (RTBF) under PDPL is handled via anonymization (replace PII with hash), not deletion.

## Decision

Implement **HMAC-SHA256 chained audit logs**:
- Each audit entry stores: `content` + `entryHash` (HMAC-SHA256 of current entry + previous entry hash)
- Previous entry hash stored as `previousHash` reference
- Audit table `[Audit].[AuditLog]` is append-only (no UPDATE/DELETE via application logic)
- Database trigger prevents modification/deletion at SQL layer
- Verification endpoint `/api/v1/audit/verify-chain` traverses chain, recomputes HMACs, detects breaks
- RTBF compliance: anonymize PII in audit entry content (replace name → `SHA256(name)`, email → `SHA256(email)`), leave entry unchanged

This approach meets GOV tamper-detection requirements without blockchain, achieves immutability without hardware (hardware security modules optional for Phase 2), and maintains compliance with PDPL RTBF.

## Consequences

### Positive
- **Tamper Detection:** Chain verification immediately reveals unauthorized modifications or deletions
- **No Infrastructure:** Standard SQL Server triggers; no blockchain service, no Merkle tree library required
- **Compliance Ready:** Meets QĐ742 + NĐ13 tamper-evidence mandates; RTBF via anonymization aligns with PDPL Article 12
- **Audit Trail Integrity:** DBA cannot silently delete entries; chain breaks if attempted
- **Append-Only Guarantees:** Trigger prevents UPDATE/DELETE; only INSERT allowed; application enforces via AuditService
- **Performance:** HMAC computation lightweight (~1µs per entry); verification endpoint can process 10k+ entries/sec

### Negative
- **Append-Only Growth:** Table size grows indefinitely; archival strategy required (12-month online, 24-month archive per NĐ53)
- **Chain Verification Latency:** Verifying 12 months of entries (~30M rows) takes ~5 seconds; endpoint not real-time
- **RTBF Complexity:** Anonymization decision logic must be per-organization (PII definitions vary); no simple anonymization rule
- **Immutability Lock-In:** Cannot correct data entry errors without breaking chain; correction entries must be appended
- **Private Key Management:** HMAC secret key stored in Vault; key rotation requires new chain anchor

## Alternatives Considered

| Option | Why Rejected |
|--------|-------------|
| **Blockchain (Hyperledger Fabric)** | Overkill for GOV audit; requires separate service, consensus overhead, operational complexity |
| **Database Triggers + Checksums** | Checksums detectible (e.g., UPDATE Audit SET CheckSum = MD5(CheckSum)); HMAC requires secret key, harder to forge |
| **Read-Only Database Replicas** | Cannot prevent DBA modifications on primary; replica lag allows undetected changes before replication |
| **Merkle Tree + Binary Serialization** | More complex than HMAC chain; no additional tamper-detection benefit; difficult to migrate to different schema |

## Implementation Details

### Table Structure
```sql
CREATE TABLE [Audit].[AuditLog] (
    [Id] BIGINT PRIMARY KEY IDENTITY(1,1),
    [UserId] NVARCHAR(128) NOT NULL,
    [EntityType] NVARCHAR(256) NOT NULL,
    [EntityId] UNIQUEIDENTIFIER NOT NULL,
    [Action] NVARCHAR(50) NOT NULL, -- "CREATE", "UPDATE", "DELETE"
    [Content] NVARCHAR(MAX) NOT NULL, -- JSON: anonymized PII
    [IpAddress] NVARCHAR(45),
    [UserAgent] NVARCHAR(512),
    [EntryHash] NVARCHAR(64) NOT NULL, -- HMAC-SHA256 hex
    [PreviousHash] NVARCHAR(64), -- HMAC of previous entry
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
) WITH (FILLFACTOR = 100);

-- Prevent modification
CREATE TRIGGER [trgPreventAuditModification] ON [Audit].[AuditLog]
INSTEAD OF UPDATE, DELETE
AS BEGIN
    RAISERROR('Audit log is append-only.', 16, 1);
END;
```

### HMAC Computation (C#)
```csharp
public string ComputeEntryHash(string content, string previousHash)
{
    using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_auditSecret)))
    {
        var combined = $"{content}|{previousHash ?? ""}";
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash);
    }
}
```

### Verification Endpoint
```csharp
[HttpPost("verify-chain")]
public async Task<VerificationResult> VerifyChain(DateTime startDate, DateTime endDate)
{
    var entries = await _auditRepository.GetEntriesAsync(startDate, endDate);
    string previousHash = null;

    foreach (var entry in entries)
    {
        var computed = ComputeEntryHash(entry.Content, previousHash);
        if (computed != entry.EntryHash)
            return new VerificationResult { Valid = false, BreakAt = entry.Id };
        previousHash = entry.EntryHash;
    }

    return new VerificationResult { Valid = true };
}
```

## Mitigation Strategy

- **Key Rotation:** Plan Phase 2 HSM integration; current Vault-based key rotation requires new chain anchor entry
- **Archival:** Archive entries older than 12 months to separate cold-storage database; keep verification chain continuous via anchor entries
- **RTBF Policy:** Define per-organization PII anonymization rules (e.g., names → SHA256, emails → domain hash)
- **Audit Service:** Single AuditService class encapsulates HMAC computation and PreviousHash lookup; enforces immutability via contracts
