using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Asserts the VGCA doc-flow signed-PDF upload (the signing commit) emits exactly one audit row,
/// attributed to the signing tenant. The upload previously ran in a DbContext-direct controller outside
/// the MediatR pipeline and produced no audit row (ADR-004 gap); it now dispatches
/// <c>CompleteDocFlowUploadCommand</c>, which audits the Completed transition exactly once.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class DocFlowUploadAuditLogTests : IClassFixture<WebAppFixture>
{
    private static readonly Guid _userId = Guid.Parse("00000000-0000-0000-cafe-000000000001");
    private readonly WebAppFixture _app;

    public DocFlowUploadAuditLogTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task Upload_EmitsOneAuditRow_AttributedToSigningTenant()
    {
        var docId    = Guid.NewGuid();
        var nonce    = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        long recordId = await SeedDownloadedRecordAsync(docId, nonce, tenantId);

        var auditDb = GetAuditDb();
        (await CountRowsAsync(auditDb, recordId)).Should().Be(0);

        var jwt = GetJwtService().IssueJwt(_userId, docId, nonce, tenantId);
        var client = _app.CreateClient();

        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4\n%signed-by-audit-test\n");
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "uploadfile", "signed.pdf");

        var response = await client.PostAsync(
            $"/api/signing/docflow/file?token={Uri.EscapeDataString(jwt)}", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await GetRowsAsync(GetAuditDb(), recordId);
        rows.Should().ContainSingle("the signing commit must emit exactly one audit row");
        rows[0].Category.Should().Be(AuditCategory.AppAccess);
        rows[0].TenantId.Should().Be(tenantId,
            "the anonymous doc-flow upload must attribute the audit row to the signing tenant");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Task<int> CountRowsAsync(AuditDbContext db, long recordId) =>
        db.AuditLogEntries
            .Where(e => e.ResourceType == "SigningDocFlowRecord" && e.ResourceId == recordId.ToString())
            .CountAsync();

    private static Task<List<AuditLogEntry>> GetRowsAsync(AuditDbContext db, long recordId) =>
        db.AuditLogEntries
            .Where(e => e.ResourceType == "SigningDocFlowRecord" && e.ResourceId == recordId.ToString())
            .ToListAsync();

    private async Task<long> SeedDownloadedRecordAsync(Guid docId, Guid nonce, Guid tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var record = SigningDocFlowRecord.Initiate(
            tenantId:           tenantId,
            docId:              docId,
            signType:           DocFlowSignType.Issued,
            originalStorageKey: $"{tenantId}/{docId}/original.pdf",
            jwtNonce:           nonce,
            initiatedBy:        _userId);
        record.MarkDownloaded(); // upload is only valid from the Downloaded state
        db.SigningDocFlowRecords.Add(record);
        await db.SaveChangesAsync();

        // Slot 1 (download) already consumed; slot 2 remains for the upload.
        var nonceRow = SigningDocFlowNonce.Create(nonce, tenantId, record.Id, ttlSeconds: 300);
        nonceRow.TryUse();
        db.SigningDocFlowNonces.Add(nonceRow);
        await db.SaveChangesAsync();

        return record.Id;
    }

    private IDocFlowJwtService GetJwtService()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IDocFlowJwtService>();
    }

    private AuditDbContext GetAuditDb()
    {
        var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    }
}
