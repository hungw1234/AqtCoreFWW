using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Forms.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Forms.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for FormsPiiAnonymizer using EF Core InMemory.
/// Covers: JSON mode anonymization, guest submission scan, Table mode delegation.
/// SQL-specific behavior (Table mode Dapper) tested via integration tests.
/// </summary>
public sealed class FormsPiiAnonymizerTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SubjectId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    // IGuidGenerator for FormSubmission.Create (T2 entity)
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();

    private readonly FormsDbContext _db;
    private readonly IFormDataStorageFactory _factory = Substitute.For<IFormDataStorageFactory>();
    private readonly IFormDataStorage _storage = Substitute.For<IFormDataStorage>();
    private readonly ILogger<FormsPiiAnonymizer> _logger = Substitute.For<ILogger<FormsPiiAnonymizer>>();
    private readonly FormsPiiAnonymizer _sut;

    public FormsPiiAnonymizerTests()
    {
        var options = new DbContextOptionsBuilder<FormsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = Substitute.For<ITenantContext>();
        _db = new FormsDbContext(options, tenantContext);
        _factory.Create(Arg.Any<StorageMode>()).Returns(_storage);
        _sut = new FormsPiiAnonymizer(_db, _factory, _logger);
        // Each FormSubmission/FormTemplate Create call gets a unique PublicId
        _guidGen.NewIdRandom().Returns(_ => Guid.NewGuid());
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
    }

    public void Dispose() => _db.Dispose();

    // --- No data ---

    [Fact]
    public async Task AnonymizeAsync_NoTemplatesInTenant_ReturnsOkWithZero()
    {
        var result = await _sut.AnonymizeAsync(SubjectId, TenantId, null);

        result.Succeeded.Should().BeTrue();
        result.RecordsAnonymized.Should().Be(0);
    }

    [Fact]
    public async Task AnonymizeAsync_TemplatePresentButNoPiiFields_SkipsTemplate()
    {
        var template = SeedTemplate(StorageMode.Json);
        SeedField(template, "name", isPii: false);
        SeedSubmission(template, SubjectId, """{"name":"Nguyen Van A"}""");
        await _db.SaveChangesAsync();

        var result = await _sut.AnonymizeAsync(SubjectId, TenantId, null);

        result.Succeeded.Should().BeTrue();
        result.RecordsAnonymized.Should().Be(0);
    }

    // --- JSON mode ---

    [Fact]
    public async Task AnonymizeAsync_JsonMode_ReplacesPiiFieldsWithSentinel()
    {
        var template = SeedTemplate(StorageMode.Json);
        SeedField(template, "cccd", isPii: true);
        SeedField(template, "address", isPii: true);
        SeedField(template, "caseRef", isPii: false);
        var sub = SeedSubmission(template, SubjectId,
            """{"cccd":"123456789","address":"Ha Noi","caseRef":"C-001"}""");
        await _db.SaveChangesAsync();

        await _sut.AnonymizeAsync(SubjectId, TenantId, null);

        var saved = await _db.Set<FormSubmission>().IgnoreQueryFilters()
            .FirstAsync(s => s.Id == sub.Id);
        saved.IsAnonymized.Should().BeTrue();

        var json = saved.SubmissionData!;
        json.Should().Contain("\"cccd\":\"[DA AN DANH]\"");
        json.Should().Contain("\"address\":\"[DA AN DANH]\"");
        json.Should().Contain("\"caseRef\":\"C-001\""); // non-PII preserved
    }

    [Fact]
    public async Task AnonymizeAsync_JsonMode_SkipsSubmissionsAlreadyAnonymized()
    {
        var template = SeedTemplate(StorageMode.Json);
        SeedField(template, "phone", isPii: true);
        var sub = SeedSubmission(template, SubjectId, """{"phone":"0901234567"}""");
        sub.AnonymizeSubmissionData("[DA AN DANH]"); // mark already done
        await _db.SaveChangesAsync();

        var result = await _sut.AnonymizeAsync(SubjectId, TenantId, null);

        // IsAnonymized = true → filtered out by WHERE !s.IsAnonymized
        result.RecordsAnonymized.Should().Be(0);
    }

    [Fact]
    public async Task AnonymizeAsync_JsonMode_IgnoresOtherSubjectSubmissions()
    {
        var template = SeedTemplate(StorageMode.Json);
        SeedField(template, "phone", isPii: true);
        var otherSub = SeedSubmission(template, Guid.NewGuid(), """{"phone":"0901234567"}""");
        await _db.SaveChangesAsync();

        await _sut.AnonymizeAsync(SubjectId, TenantId, null); // different SubjectId

        var saved = await _db.Set<FormSubmission>().IgnoreQueryFilters()
            .FirstAsync(s => s.Id == otherSub.Id);
        saved.SubmissionData.Should().Contain("0901234567"); // untouched
    }

    // --- Guest submission scan ---

    [Fact]
    public async Task AnonymizeAsync_GuestSubmission_AnonymizesWhenCitizenNationalIdFoundInData()
    {
        const string nid = "012345678901";
        var template = SeedTemplate(StorageMode.Json);
        SeedField(template, "cccd", isPii: true);
        // Guest submission: SubmittedBy = Guid.Empty, data contains the NID
        var guest = SeedSubmission(template, Guid.Empty,
            $$$"""{"cccd":"{{{nid}}}","note":"test"}""");
        await _db.SaveChangesAsync();

        await _sut.AnonymizeAsync(SubjectId, TenantId, citizenNationalId: nid);

        var saved = await _db.Set<FormSubmission>().IgnoreQueryFilters()
            .FirstAsync(s => s.Id == guest.Id);
        saved.IsAnonymized.Should().BeTrue();
        saved.SubmissionData.Should().Contain("[DA AN DANH]");
    }

    [Fact]
    public async Task AnonymizeAsync_GuestSubmission_IgnoredWhenCitizenNationalIdNotInData()
    {
        var template = SeedTemplate(StorageMode.Json);
        SeedField(template, "cccd", isPii: true);
        var guest = SeedSubmission(template, Guid.Empty, """{"cccd":"999999999999"}""");
        await _db.SaveChangesAsync();

        await _sut.AnonymizeAsync(SubjectId, TenantId, citizenNationalId: "012345678901");

        var saved = await _db.Set<FormSubmission>().IgnoreQueryFilters()
            .FirstAsync(s => s.Id == guest.Id);
        saved.IsAnonymized.Should().BeFalse();
    }

    // --- Table mode ---

    [Fact]
    public async Task AnonymizeAsync_TableMode_DelegatesAnonymizationToStorage()
    {
        var template = SeedTemplate(StorageMode.Table);
        SeedField(template, "cccd", isPii: true);
        var sub = SeedSubmission(template, SubjectId, null); // Table mode: no SubmissionData
        await _db.SaveChangesAsync();

        _storage.AnonymizeAsync(default, default!, default!, default)
            .ReturnsForAnyArgs(Task.CompletedTask);

        var result = await _sut.AnonymizeAsync(SubjectId, TenantId, null);

        result.Succeeded.Should().BeTrue();
        await _storage.Received(1).AnonymizeAsync(
            template.Id,
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Contains(sub.PublicId)),
            Arg.Is<IReadOnlyList<string>>(keys => keys.Contains("cccd")),
            Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    private FormTemplate SeedTemplate(StorageMode mode)
    {
        // Phase-07: FormTemplate retiered T1→T2 — factory now requires IGuidGenerator gen
        var t = FormTemplate.Create(_guidGen, TenantId, "Test Form", "Form Test", $"T{Guid.NewGuid():N}"[..10], mode, AdminId);
        _db.Set<FormTemplate>().Add(t);
        return t;
    }

    // Counter for unique T1 FormField long Id values (ValueGeneratedNever on FormField —
    // EF InMemory cannot auto-assign; tests must supply unique IDs).
    private long _fieldIdCounter = 1L;

    private FormField SeedField(FormTemplate template, string key, bool isPii)
    {
        // Inject unique Id via reflection (T1 entity — Id is private set, DB-assigned normally).
        int order = (int)_fieldIdCounter;
        var f = FormField.Create(template.Id, key, FormFieldType.Text, key, key, order, false, 1, isPii: isPii);
        // Set the Id directly via the base Entity's backing field to ensure EF uniqueness
        var idProp = typeof(AqtCoreFW.SharedKernel.Domain.Entity)
            .GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        idProp.GetSetMethod(nonPublic: true)!.Invoke(f, [_fieldIdCounter++]);
        _db.Set<FormField>().Add(f);
        return f;
    }

    private FormSubmission SeedSubmission(FormTemplate template, Guid submittedBy, string? data)
    {
        // FormSubmission is T2: requires IGuidGenerator for PublicId generation
        _guidGen.NewIdRandom().Returns(Guid.NewGuid());
        var s = FormSubmission.Create(_guidGen, TenantId, template.Id, template.Version, data, submittedBy);
        _db.Set<FormSubmission>().Add(s);
        return s;
    }
}
