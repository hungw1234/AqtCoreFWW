# AqtCoreFW Signing Usage Cookbook

**Last updated:** 2026-05-11 | **Quick recipes for common signing scenarios**

Each recipe is copy-paste ready (BE C# + FE TypeScript) and cross-references the integration guide for full API details.

---

## Recipe 1: Sign an Audit Event (Tier-1 Raw Hash)

**Use case:** Log an immutable audit record with cryptographic signature (e.g., "Transaction approved by CFO").

**When to use:** Audit-critical events where offline non-repudiation is required.

### FE TypeScript

```typescript
// web/src/features/digital-signature/audit-signing.ts
import { apiClient } from '@/api/api-client';

export async function signAuditEvent(auditData: {
  action: string;
  resourceType: string;
  resourceId: string;
  actor: string;
}) {
  // 1. Hash audit data server-side for consistency
  const hashResp = await apiClient.post('/api/v1/digital-signature/compute-hash', {
    payload: JSON.stringify(auditData),
    hashAlgorithm: 'SHA256',
  });
  const hash = hashResp.data.hash;

  // 2. Call VGCA USB adapter
  const signResult = await window.vgca_sign_msg({
    payload: new Uint8Array(Buffer.from(hash, 'base64')),
    hashAlg: 'SHA256',
    encode: 'RSA',
    reason: `Sign audit: ${auditData.action}`,
  });

  if (signResult.status !== 0) {
    throw new Error(`VGCA error: ${signResult.status}`);
  }

  // 3. Return to BE for verification + storage
  return {
    payload: hash,
    signature: signResult.signature,
    certBase64: signResult.certBase64,
  };
}
```

### BE C#

```csharp
// src/modules/audit/AqtCoreFW.Audit.Application/Commands/SignAuditEventCommandHandler.cs
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Api;

public sealed class SignAuditEventCommandHandler(
    IDigitalSignatureProvider provider,
    ISignatureProviderResolver providerResolver,
    IAuditLogger auditLogger) : ICommandHandler<SignAuditEventCommand, Result<SignAuditEventResultDto>>
{
    public async Task<Result<SignAuditEventResultDto>> Handle(
        SignAuditEventCommand cmd,
        CancellationToken ct = default)
    {
        // 1. Resolve provider for tenant
        var config = await GetTenantSignatureConfig(cmd.TenantId, ct);
        var provider = providerResolver.Resolve(config.ProviderKey);

        // 2. Verify signature from FE
        var verifyRequest = new VerifyRequest(
            Payload: Convert.FromBase64String(cmd.Payload),
            Signature: Convert.FromBase64String(cmd.SignatureBase64),
            OcspMode: OcspMode.Soft,
            SignerCertDer: Convert.FromBase64String(cmd.CertBase64),
            HashAlg: "SHA256",
            Encode: "RSA",
            Format: SignatureFormat.Raw
        );

        var verifyResult = await provider.VerifyAsync(verifyRequest, ct);

        if (!verifyResult.IsValid)
        {
            return Result.Fail($"Signature invalid: {verifyResult.FailureReason}");
        }

        // 3. Emit audit log with signature
        await auditLogger.LogAsync(new AuditLogRequest(
            Action: "AuditEventSigned",
            ResourceType: "AuditEntry",
            ResourceId: cmd.AuditEntryId.ToString(),
            Details: new
            {
                Provider = provider.ProviderKey,
                CertificateSerialNumber = verifyResult.SigningCertificate?.SerialNumber,
                SignatureFormat = "Raw",
                Outcome = "Success"
            }
        ), ct);

        // 4. Store signature in AuditEventSignature table
        var signature = new AuditEventSignature(
            AuditEntryId: cmd.AuditEntryId,
            SignatureBytes: Convert.FromBase64String(cmd.SignatureBase64),
            CertificateSerialNumber: verifyResult.SigningCertificate?.SerialNumber ?? "",
            SignedAt: DateTime.UtcNow,
            ProviderKey: provider.ProviderKey
        );

        await auditSignatureRepository.AddAsync(signature, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Ok(new SignAuditEventResultDto(
            IsValid: true,
            CertificateSubject: verifyResult.SigningCertificate?.Subject
        ));
    }
}
```

---

## Recipe 2: Sign a Workflow Approval (Tier-1 or Tier-2 JWS)

**Use case:** Digitally sign a workflow transition (e.g., manager approves leave request).

**When to use:** Approval chain where non-repudiation needed across systems.

### Variant A: Tier-1 Raw (Simpler)

```csharp
// Approval command handler
var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
{
    workflowInstanceId = cmd.InstanceId,
    transition = cmd.TransitionName,
    actor = cmd.ActorId,
    timestamp = DateTime.UtcNow.ToUniversalTime()
}));

var hash = SHA256.HashData(payload);

var signRequest = new SignRequest(
    Payload: hash,
    UserId: cmd.ActorId,
    Reason: $"Approve {cmd.TransitionName}",
    ExtraParameters: null,
    HashAlg: "SHA256",
    Encode: "RSA",
    RequestedFormat: SignatureFormat.Raw
);

var signResult = await provider.SignAsync(signRequest, ct);

// Store approval record with signature reference
var approval = new WorkflowApproval(
    InstanceId: cmd.InstanceId,
    ApprovedBy: cmd.ActorId,
    SignatureBytes: signResult.Signature,
    CertificateSerialNumber: signResult.SigningCertificate?.SerialNumber,
    ApprovedAt: DateTime.UtcNow
);

await workflowApprovalRepo.AddAsync(approval, ct);
```

### Variant B: Tier-2 JWS (Better for Cross-System Interop)

```csharp
// Generate JWS payload (self-contained)
var claims = new Dictionary<string, object>
{
    { "sub", cmd.ActorId },
    { "workflowInstanceId", cmd.InstanceId.ToString() },
    { "transition", cmd.TransitionName },
    { "iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
    { "exp", DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds() }
};

var jwtPayload = JsonSerializer.Serialize(claims);
var jwtBytes = Encoding.UTF8.GetBytes(jwtPayload);

// FE calls: await window.vgca_sign_json({
//   jsonBase64Content: btoa(jwtPayload),
//   hashAlg: "SHA256"
// });

// BE verifies returned JWS
var verifyResult = await structuredSignatureService.VerifyJwsAsync(
    jwsCompactString: cmd.JwsCompact,
    ocspMode: OcspMode.Soft,
    ct
);

// Store JWS in approval record; remote systems verify independently
var approval = new WorkflowApproval(
    InstanceId: cmd.InstanceId,
    ApprovedBy: cmd.ActorId,
    JwsCompact: cmd.JwsCompact,  // Self-verifying; send to other systems
    ApprovedAt: DateTime.UtcNow
);
```

---

## Recipe 3: Sign an E-Invoice (Tier-2 XML)

**Use case:** Sign an electronic invoice per VN e-invoice standard (Thông tư 78/2014/TT-BTC).

**When to use:** Revenue/tax reporting where XML signature required by tax authority.

### FE TypeScript

```typescript
// web/src/features/digital-signature/invoice-signing.ts
export async function signInvoice(invoiceXml: string) {
  const xmlBytes = new TextEncoder().encode(invoiceXml);
  const xmlBase64 = btoa(String.fromCharCode(...new Uint8Array(xmlBytes)));

  const result = await window.vgca_sign_xml({
    xmlBase64Content: xmlBase64,
    hashAlg: 'SHA256',
    xmlDsigForm: 'Detached',  // Keep original invoice + signature separate
  });

  if (result.status !== 0) {
    throw new Error(`Sign failed: ${result.status}`);
  }

  return {
    xmlBase64: xmlBase64,
    signatureBase64: result.signature,
    certBase64: result.certBase64,
  };
}
```

### BE C#

```csharp
// src/modules/reporting/AqtCoreFW.Reporting.Application/Commands/SignInvoiceCommandHandler.cs
public sealed class SignInvoiceCommandHandler(
    IStructuredSignatureService structuredSigService,
    IInvoiceRepository invoiceRepo,
    IAuditLogger auditLogger) : ICommandHandler<SignInvoiceCommand>
{
    public async Task<Result> Handle(SignInvoiceCommand cmd, CancellationToken ct)
    {
        // 1. Retrieve invoice
        var invoice = await invoiceRepo.GetByIdAsync(cmd.InvoiceId, ct);
        if (invoice == null)
            return Result.Fail("Invoice not found");

        // 2. Parse signed XML from FE
        var signedXmlBytes = Convert.FromBase64String(cmd.SignedXmlDsigBase64);

        // 3. Verify XMLDSig structure
        var verifyResult = await structuredSigService.VerifyXmlDsigAsync(
            xmlDsigBytes: signedXmlBytes,
            ocspMode: OcspMode.Hard,  // Tax authority requires hard OCSP
            ct
        );

        if (!verifyResult.IsValid)
            return Result.Fail($"XML signature invalid: {verifyResult.FailureReason}");

        // 4. Validate signer certificate OU matches "tax authority approved"
        var subject = verifyResult.SigningCertificate?.Subject ?? "";
        if (!subject.Contains("OU=TaxAuthority"))
            return Result.Fail("Signer not authorized by tax authority");

        // 5. Persist signature
        invoice.ApplyXmlSignature(
            signedXmlBytes,
            verifyResult.SigningCertificate!.SerialNumber
        );

        await invoiceRepo.UpdateAsync(invoice, ct);
        await auditLogger.LogAsync(new AuditLogRequest(
            Action: "InvoiceSigned",
            ResourceType: "Invoice",
            ResourceId: cmd.InvoiceId.ToString(),
            Details: new { Signer = subject }
        ), ct);

        return Result.Ok();
    }
}
```

---

## Recipe 4: Sign a Vietnamese Government Document (Tier-3 Doc-Flow)

**Use case:** Sign official government document (công văn phát hành) with visible stamp showing approval.

**When to use:** e-Government workflow requiring visible signature on PDF.

### FE TypeScript (Full Sequence)

```typescript
// web/src/features/digital-signature/docflow-signing.ts
import { apiClient } from '@/api/api-client';

export async function signGovernmentDocument(params: {
  docId: string;
  docNumber: string;
  issuedDate: Date;
  signType: 'Issued' | 'Approved' | 'Income';
}) {
  // Step 1: Initiate signing session
  const initiateResp = await apiClient.post(
    '/api/signing/docflow/initiate',
    {
      docId: params.docId,
      signType: params.signType,
      originalStorageKey: `docs/${params.docId}.pdf`,
      docNumber: params.docNumber,
      issuedDate: params.issuedDate.toISOString(),
      extra: {
        department: 'Legal Affairs',
        approver: 'Director',
      },
    }
  );

  const { handlerUrl, jwt } = initiateResp.data;

  // Step 2: Call VGCA plugin with handler URL + JWT
  // User enters PIN, drags stamp to position, confirms
  const signResult = await window.vgca_sign_issued({
    handlerUrl: handlerUrl,
    jwt: jwt,
    docNumber: params.docNumber,
    issuedDate: params.issuedDate.toISOString(),
    extra: { approver: 'Director' },
  });

  if (signResult.status !== 0) {
    throw new Error(`VGCA doc-flow error: ${signResult.status}`);
  }

  // Step 3: Poll for completion
  let maxAttempts = 30;
  let signed = false;
  let fileUrl = '';

  while (maxAttempts > 0 && !signed) {
    const confirmResp = await apiClient.post(
      '/api/v1/digital-signature/confirm',
      { transactionId: signResult.transactionId }
    );

    if (confirmResp.data.status === 'Completed') {
      fileUrl = confirmResp.data.fileStorageKey;
      signed = true;
      break;
    }

    if (confirmResp.data.status === 'Failed') {
      throw new Error(`Signing failed: ${confirmResp.data.reason}`);
    }

    // Status = Pending; wait 2s before retry
    await new Promise((resolve) => setTimeout(resolve, 2000));
    maxAttempts--;
  }

  if (!signed) {
    throw new Error('Signing timeout (5 minutes exceeded)');
  }

  return { fileStorageKey: fileUrl };
}
```

### BE Controller (Doc-Flow Initiate)

See integration guide § 6.2 for endpoint details. Framework handles JWT issuance + nonce tracking.

---

## Recipe 5: Switch Providers Per Tenant

**Use case:** Tenant A uses VGCA USB token; Tenant B uses Viettel Mysign.

**When to use:** Multi-tenant SaaS with heterogeneous signing requirements.

### Admin UI TypeScript

```typescript
// web/src/pages/admin/signing-config.tsx
import { Form, Button, Select, Input, Message } from 'antd';

export function SigningConfigPage() {
  const [form] = Form.useForm();
  const [provider, setProvider] = useState<string>('usb-vgca');

  const handleSave = async (values: any) => {
    try {
      // Clear any existing pending transactions for this tenant
      await apiClient.post('/api/v1/admin/signing/abort-pending-transactions', {
        tenantId: values.tenantId,
      });

      // Update provider config
      const resp = await apiClient.post(
        '/api/v1/admin/signing/tenant-config',
        {
          tenantId: values.tenantId,
          providerKey: values.providerKey,
          providerSettings:
            values.providerKey === 'remote-viettel-mysign'
              ? {
                  clientId: values.mysignClientId,
                  clientSecret: values.mysignClientSecret,
                  profileId: values.mysignProfileId,
                }
              : {},
        }
      );

      Message.success('Signing provider updated');
    } catch (err) {
      Message.error(`Update failed: ${err.message}`);
    }
  };

  return (
    <Form form={form} layout="vertical" onFinish={handleSave}>
      <Form.Item label="Provider" name="providerKey" rules={[{ required: true }]}>
        <Select
          options={[
            {
              label: 'VGCA USB Token (Tier-1/2/3)',
              value: 'usb-vgca',
            },
            {
              label: 'Viettel Mysign (Async Mobile)',
              value: 'remote-viettel-mysign',
            },
          ]}
          onChange={setProvider}
        />
      </Form.Item>

      {provider === 'remote-viettel-mysign' && (
        <>
          <Form.Item
            label="Mysign Client ID"
            name="mysignClientId"
            rules={[{ required: true }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            label="Mysign Client Secret"
            name="mysignClientSecret"
            rules={[{ required: true }]}
          >
            <Input type="password" />
          </Form.Item>
          <Form.Item label="Profile ID" name="mysignProfileId">
            <Input />
          </Form.Item>
        </>
      )}

      <Button type="primary" htmlType="submit">
        Save Provider Configuration
      </Button>
    </Form>
  );
}
```

### BE Controller (Tenant Config Update)

```csharp
// src/host/AqtCoreFW.Api/Controllers/Admin/SigningConfigAdminController.cs
[Authorize(Policy = "AdminAccess")]
[Route("api/v1/admin/signing")]
public sealed class SigningConfigAdminController(
    ITenantSignatureConfigRepository configRepo,
    ISignatureProviderResolver providerResolver,
    ISigningPendingTransactionRepository pendingTxRepo,
    IUnitOfWork unitOfWork) : ApiControllerBase
{
    [HttpPost("tenant-config")]
    public async Task<IActionResult> UpdateTenantConfig(
        UpdateTenantSignatureConfigCommand cmd,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();

        // 1. Verify provider exists in DI
        var provider = providerResolver.Resolve(cmd.ProviderKey);

        // 2. Abort pending transactions from old provider
        var pendingTxs = await pendingTxRepo
            .ListByTenantAsync(tenantId, ct);
        foreach (var tx in pendingTxs)
        {
            tx.MarkAsFailed("Provider switched; transaction cancelled");
            await pendingTxRepo.UpdateAsync(tx, ct);
        }

        // 3. Update or create config
        var config = await configRepo.GetByTenantAsync(tenantId, ct)
            ?? new TenantSignatureConfig(
                TenantId: tenantId,
                ProviderKey: cmd.ProviderKey,
                ProviderSettings: cmd.ProviderSettings
            );

        config = config with
        {
            ProviderKey = cmd.ProviderKey,
            ProviderSettings = cmd.ProviderSettings,
            UpdatedAt = DateTime.UtcNow
        };

        await configRepo.UpsertAsync(config, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Ok(new ApiResponse<object>(new { message = "Provider updated" }));
    }

    [HttpPost("abort-pending-transactions")]
    public async Task<IActionResult> AbortPendingTransactions(
        AbortPendingTransactionsRequest req,
        CancellationToken ct = default)
    {
        var pending = await pendingTxRepo
            .ListByTenantAsync(req.TenantId, ct);

        foreach (var tx in pending)
        {
            tx.MarkAsFailed("Provider switch in progress");
            await pendingTxRepo.UpdateAsync(tx, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Ok();
    }
}
```

---

## Recipe 6: Async-Callback Wait Pattern (Viettel async=3)

**Use case:** Long-running mobile approval where user may take minutes; FE subscribes to webhook.

**When to use:** Executive approval workflow; async-callback more efficient than polling.

### BE Controller (Initiate Async-Callback Sign)

```csharp
// src/modules/identity/AqtCoreFW.Identity.Application/Commands/InitiateMysignAsyncSignCommandHandler.cs
public sealed class InitiateMysignAsyncSignCommandHandler(
    ISignatureProviderResolver providerResolver,
    ISigningPendingTransactionRepository pendingTxRepo,
    IUnitOfWork unitOfWork) : ICommandHandler<InitiateMysignAsyncSignCommand, Result<SignResultDto>>
{
    public async Task<Result<SignResultDto>> Handle(
        InitiateMysignAsyncSignCommand cmd,
        CancellationToken ct)
    {
        var provider = providerResolver.Resolve("remote-viettel-mysign");

        var signRequest = new SignRequest(
            Payload: Convert.FromBase64String(cmd.PayloadBase64),
            UserId: cmd.ActorId,
            Reason: cmd.Reason,
            ExtraParameters: new Dictionary<string, string>
            {
                { "async", "3" }  // Async-callback mode
            },
            HashAlg: "SHA256"
        );

        var signResult = await provider.SignAsync(signRequest, ct);

        if (signResult.IsPending)
        {
            // 1. Store pending transaction
            var pendingTx = new SigningPendingTransaction(
                TransactionId: signResult.TransactionId,
                TenantId: cmd.TenantId,
                UserId: cmd.ActorId,
                Provider: "remote-viettel-mysign",
                Status: "Pending",  // 4000
                CreatedAt: DateTime.UtcNow,
                ExpiresAt: DateTime.UtcNow.AddHours(24)
            );

            await pendingTxRepo.AddAsync(pendingTx, ct);
            await unitOfWork.SaveChangesAsync(ct);

            // 2. Return transaction ID to FE for polling
            return Result.Ok(new SignResultDto(
                IsSuccess: false,
                IsPending: true,
                TransactionId: signResult.TransactionId,
                Message: "Awaiting mobile confirmation; check back in /confirm endpoint"
            ));
        }

        // Sync result (shouldn't happen in async=3, but handle gracefully)
        return Result.Ok(new SignResultDto(
            IsSuccess: signResult.IsSuccess,
            Signature: signResult.Signature != null
                ? Convert.ToBase64String(signResult.Signature)
                : null,
            CertificateSubject: signResult.SigningCertificate?.Subject
        ));
    }
}
```

### FE Polling Loop

```typescript
// web/src/features/digital-signature/async-callback-poll.ts
export async function waitForAsyncSignature(
  transactionId: string,
  maxWaitMs: number = 600000  // 10 minutes
): Promise<SignatureResult> {
  const startTime = Date.now();
  const pollIntervalMs = 5000;  // Poll every 5s

  while (Date.now() - startTime < maxWaitMs) {
    const resp = await apiClient.post(
      '/api/v1/digital-signature/confirm',
      { transactionId }
    );

    if (resp.data.status === 'Completed') {
      return {
        signature: resp.data.signature,
        certificateSubject: resp.data.certificateSubject,
      };
    }

    if (resp.data.status === 'Failed') {
      throw new Error(`Signing failed: ${resp.data.reason}`);
    }

    // Status = Pending; Mysign webhook may fire at any moment
    console.log('Awaiting mobile confirmation...');
    await new Promise((resolve) => setTimeout(resolve, pollIntervalMs));
  }

  throw new Error('Signature operation timeout (10 minutes)');
}

// Usage in approval workflow
const handleApproveButton = async () => {
  try {
    const initResp = await apiClient.post(
      '/api/v1/digital-signature/sign',
      {
        payload: base64PayloadHash,
        userId: currentUser.id,
        reason: 'Approve RFP #2026-001',
        extraParameters: { async: '3' },
      }
    );

    if (initResp.data.isPending) {
      // Long-running async sign
      const result = await waitForAsyncSignature(
        initResp.data.transactionId
      );
      setApprovalSignature(result);
      Message.success('Document approved and signed');
    }
  } catch (err) {
    Message.error(err.message);
  }
};
```

### BE Webhook Handler (Mysign Result Callback)

See integration guide § 9.3. Framework handles idempotency + token validation. Your application listens to `SignatureCompletedIntegrationEvent` to trigger downstream workflows (e.g., workflow state transition, notification email).

```csharp
// src/modules/notifications/.../EventHandlers/SignatureCompletedConsumer.cs
public sealed class SignatureCompletedConsumer(
    IWorkflowModuleClient workflowClient) : IConsumer<SignatureCompletedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<SignatureCompletedIntegrationEvent> context)
    {
        var @event = context.Message;

        // Auto-advance workflow after signature completed
        await workflowClient.ExecuteTransitionAsync(
            instanceId: @event.WorkflowInstanceId,
            transitionName: "ApprovalSigned",
            cancellationToken: context.CancellationToken
        );
    }
}
```

---

## Recipe 7: Multi-Sign Batch (Viettel Multiple Documents)

**Use case:** Sign 5 contracts in one API call (each doc gets separate user confirmation on mobile).

**When to use:** Batch approval scenarios where documents are related.

### FE TypeScript

```typescript
// web/src/features/digital-signature/batch-signing.ts
export async function batchSignContracts(
  contracts: Array<{ id: string; hash: string; name: string }>
) {
  const hashes = contracts.map((c) => c.hash);

  const resp = await apiClient.post('/api/v1/digital-signature/sign-batch', {
    payloads: hashes.map((h) => Buffer.from(h, 'base64')),
    userId: currentUser.id,
    reason: `Approve ${contracts.length} contracts`,
    provider: 'remote-viettel-mysign',
    numSignatures: contracts.length,
  });

  return {
    transactionId: resp.data.transactionId,
    signatures: resp.data.signatures,  // indexed by document order
  };
}
```

### BE Handler

```csharp
// src/modules/identity/.../BatchSignCommandHandler.cs
public sealed class BatchSignCommandHandler(
    ISignatureProviderResolver providerResolver) : ICommandHandler<BatchSignCommand>
{
    public async Task<Result<BatchSignResultDto>> Handle(
        BatchSignCommand cmd,
        CancellationToken ct)
    {
        var provider = providerResolver.Resolve("remote-viettel-mysign");

        // Viettel API accepts multiple documents in one call
        var signRequest = new SignRequest(
            Payload: cmd.Payloads[0],  // First hash (or combined blob)
            UserId: cmd.UserId,
            Reason: cmd.Reason,
            ExtraParameters: new Dictionary<string, string>
            {
                { "numSignatures", cmd.Payloads.Count.ToString() },
                { "async", "2" }  // Async-poll for multiple
            }
        );

        var signResult = await provider.SignAsync(signRequest, ct);

        // Results indexed: [0] → doc[0], [1] → doc[1], etc.
        var dto = new BatchSignResultDto(
            TransactionId: signResult.TransactionId,
            Signatures: signResult.Signatures?.Select(s => Convert.ToBase64String(s)).ToList(),
            IsPending: signResult.IsPending
        );

        return Result.Ok(dto);
    }
}
```

---

## Recipe 8: Adding a New VN CA Provider (Extension Point Demo)

**Use case:** Your organization partners with FPT-CA; integrate new signing provider.

**When to use:** Extensibility validation; zero-touch provider registration.

### Step-by-Step

See integration guide § 14 for full extension walkthrough. Key points:

1. **Implement** `IDigitalSignatureProvider`
2. **Register** in `InfrastructureRegistration.cs` via `services.AddKeyedScoped<IDigitalSignatureProvider, FptCaProvider>("remote-fpt-ca")`
3. **Use** in tenant config via admin UI (provider auto-discovered)
4. **Architecture tests** automatically validate the new provider

No changes needed to resolver, request/response contracts, or controllers.

---

## Cross-Recipe: Error Handling Pattern

All recipes should follow this error handling:

```csharp
try
{
    var result = await provider.SignAsync(signRequest, ct);

    if (!result.IsSuccess)
    {
        return Result.Fail(result.FailureReason ?? "Unknown signing error");
    }

    // Continue...
}
catch (NotSupportedException ex)
{
    // Provider doesn't support this capability
    return Result.Fail($"Provider {provider.ProviderKey} does not support {capability}");
}
catch (OperationCanceledException)
{
    return Result.Fail("Signing operation cancelled");
}
catch (Exception ex)
{
    logger.LogError(ex, "Unexpected signing error");
    return Result.Fail("Internal signing error; contact support");
}
```

---

## Quick Reference: Which Recipe?

| Need | Recipe | Provider |
|------|--------|----------|
| Audit event signature | 1 | VGCA/Viettel (Tier-1) |
| Workflow approval chain | 2 | VGCA/Viettel (Tier-1 or JWS) |
| E-invoice (tax report) | 3 | VGCA (Tier-2 XML) |
| Government doc with stamp | 4 | VGCA (Tier-3 doc-flow) |
| Multi-tenant setup | 5 | Either (per-tenant config) |
| Mobile async approval (10min+) | 6 | Viettel (async=3 callback) |
| Batch approve 5+ docs | 7 | Viettel (multi-sign) |
| Add new VN CA | 8 | Custom (D6 extension) |

---

**For full API contract details, see:** `docs/signing/integration-guide.md`

**For compliance evidence:** `docs/compliance/qd-742-dossier.md` (QĐ 742 mục 5.1)

**For dependency map:** `docs/dependency-map.md` (Signing module entities + endpoints)
