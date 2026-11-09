using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// Regression guard for FormSubmissionRepository.DeleteAsync(Guid) — the override that resolves the
/// T2 entity by its Guid PublicId (alternate key) before removing it. The generic GenericRepository
/// base issues EF FindAsync, which keys on the long PRIMARY KEY, so a Guid lookup throws the
/// "Guid ... does not match long" key-type mismatch. No production path hard-deletes a FormSubmission
/// today (submissions are soft-deleted via commands), so this is a defensive guard against the latent
/// throw a future delete path would otherwise hit.
///
/// Real EF + Testcontainers SQL Server through the real repository override. Non-vacuous: pre-override
/// the DeleteAsync(Guid) call throws (no row touched); here it must succeed and soft-delete the row.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FormSubmissionRepositoryDeleteByPublicIdTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public FormSubmissionRepositoryDeleteByPublicIdTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task DeleteAsync_ResolvesSubmissionByPublicId_SoftDeletesTheRow()
    {
        // Arrange: seed a template (for the required FK column) then a FormSubmission; capture its Guid PublicId + long Id.
        Guid publicId;
        long internalId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

            var template = FormTemplate.Create(
                gen: gen,
                tenantId: TenantId,
                name: $"Delete-By-PublicId Template {Guid.NewGuid():N}",
                nameVi: "Mẫu kiểm thử xoá theo PublicId",
                code: $"DEL-{Guid.NewGuid():N}"[..12],
                storageMode: StorageMode.Json,
                createdBy: ActorId);
            db.FormTemplates.Add(template);
            await db.SaveChangesAsync();

            var submission = FormSubmission.Create(
                gen: gen,
                tenantId: TenantId,
                formTemplateId: template.Id,
                formTemplateVersion: 1,
                submissionData: "{}",
                submittedBy: ActorId);
            db.FormSubmissions.Add(submission);
            await db.SaveChangesAsync();
            publicId = submission.PublicId;
            internalId = submission.Id;
        }

        // Act: delete addressed by the Guid PublicId (the override must resolve PublicId → long, not FindAsync the PK).
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IFormSubmissionRepository>();
            await repo.DeleteAsync(publicId); // pre-override this throws the Guid-vs-long key mismatch
        }

        // Assert: the row was soft-deleted (IgnoreQueryFilters to see past the IsDeleted filter).
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            var row = await db.FormSubmissions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == internalId);

            row.Should().NotBeNull("the seeded submission row must still exist (soft-delete, not hard-delete)");
            row!.IsDeleted.Should().BeTrue("DeleteAsync(publicId) must resolve by PublicId and soft-delete the row");
        }
    }
}
