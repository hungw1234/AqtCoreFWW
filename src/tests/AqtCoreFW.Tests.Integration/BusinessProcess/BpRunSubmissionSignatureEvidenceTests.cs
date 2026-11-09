using System.Net;
using System.Text.Json;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.BusinessProcess;

/// <summary>
/// Locks the Dapper SELECT → DTO seam for BP-run submission signature evidence end-to-end:
/// seed a digitally-signed FormSubmission, then read it back through the real HTTP submissions
/// endpoint (so the tenant session context + RLS behave exactly as in production).
///
/// The risk this guards: GetLatestForRunAsync projects SignatureVerdict / SignerSubject /
/// SignatureVerifiedAt by raw SQL — a column dropped from that SELECT maps to null, the badge
/// silently disappears, and the unit tests (which mock the client) can't catch it. If the columns
/// stop flowing, isSigned becomes false / signerName null and this fails.
/// </summary>
[Collection("Integration")]
public class BpRunSubmissionSignatureEvidenceTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task GetSubmissions_SignedSubmission_SurfacesEvidence_WithCnOnly()
    {
        var actorId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();
        var verifiedAt = DateTimeOffset.UtcNow;
        const string signerSubject =
            "OID.0.9.2342.19200300.100.1.1=CCCD:017091004878, CN=ĐINH QUỐC VIỆT, L=PHÚ THỌ, C=VN";

        // --- Seed: published template (+ snapshot) + a signed BP-run submission under DefaultTenantId.
        // GetBpRunSubmissions skips a submission whose pinned version snapshot is missing, so the
        // FormTemplateVersion row is required even though we only assert submission-level evidence.
        using (var scope = Factory.Services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

            var code = "SIGSEAM_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            var template = FormTemplate.Create(
                gen, DefaultTenantId, "Signature seam", "Chữ ký seam", code, StorageMode.Json, actorId);
            ctx.FormTemplates.Add(template);
            await ctx.SaveChangesAsync();

            ctx.FormTemplateVersions.Add(FormTemplateVersionFactory.Snapshot(template, actorId));
            await ctx.SaveChangesAsync();

            var submission = FormSubmission.CreateForBpRun(
                gen, DefaultTenantId, workflowInstanceId, workflowStateId: null,
                template.Id, template.Version, submissionData: "{}", submittedBy: actorId);
            submission.RecordSignatureEvidence(
                verdict: true,
                signerSubject: signerSubject,
                signerSerial: "00ABCDEF",
                verifiedAt: verifiedAt,
                chainEvidenceJson: null,
                signerCertDer: null);
            ctx.FormSubmissions.Add(submission);
            await ctx.SaveChangesAsync();
        }

        // --- Read back through the real endpoint (SystemAdmin client, DefaultTenantId from JWT).
        var resp = await Client.GetAsync(
            $"/api/v1/business-processes/runs/{workflowInstanceId}/submissions");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
        var data = body.GetProperty("data");
        data.GetArrayLength().Should().Be(1, "the seeded signed submission must surface for the run");

        var sub = data[0];
        sub.GetProperty("isSigned").GetBoolean().Should().BeTrue();
        sub.GetProperty("signerName").GetString().Should().Be("ĐINH QUỐC VIỆT"); // CN only — DN/CCCD stay server-side
        sub.GetProperty("signatureVerifiedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }
}
