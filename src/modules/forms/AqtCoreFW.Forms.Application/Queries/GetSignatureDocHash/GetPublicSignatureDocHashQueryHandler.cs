using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetSignatureDocHash;

/// <summary>
/// Returns the Mode-2 document hash for the anonymous public sign-time endpoint.
///
/// Resolves the template by Code via Dapper (same two-step pattern as
/// <c>SubmitPublicFormCommandHandler</c>: Dapper id-lookup → EF GetWithFieldsAsync),
/// then delegates hash computation to <see cref="FormDataCanonicalizer.ComputeMode2DocHash"/>.
/// </summary>
public sealed class GetPublicSignatureDocHashQueryHandler(
    IReadDbConnection db,
    IFormTemplateRepository templateRepository)
    : IRequestHandler<GetPublicSignatureDocHashQuery, Result<SignatureDocHashDto>>
{
    public async Task<Result<SignatureDocHashDto>> Handle(
        GetPublicSignatureDocHashQuery request,
        CancellationToken cancellationToken)
    {
        // Step 1: resolve Id + TenantId by Code via Dapper (same as SubmitPublicFormCommandHandler).
        // Anonymous caller has no ambient tenant → resolve on a public_forms_bypass connection so the
        // FormTemplates RLS FILTER carve-out admits the IsPublic=1 row under a non-db_owner login.
        const string idSql = """
            SELECT Id, TenantId FROM forms.FormTemplates
            WHERE Code = @Code AND Status = 'Active' AND IsDeleted = 0 AND IsPublic = 1
            """;

        var row = await db.QueryFirstOrDefaultWithPublicFormsBypassAsync<TemplateIdRow>(
            idSql, new { Code = request.TemplateCode }, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError(
                $"Form '{request.TemplateCode}' không tìm thấy hoặc chưa active."));

        // Step 2: load with fields via EF repository — pins SESSION_CONTEXT to the resolved tenant so
        // the EF read passes the standard FormTemplates RLS FILTER under a non-db_owner login.
        var template = await templateRepository.GetPublicWithFieldsAsync(
            row.Id, row.TenantId, cancellationToken);

        if (template is null)
            return Result.Fail(new NotFoundError(
                $"Form '{request.TemplateCode}' không tìm thấy."));

        var activeFields = template.Fields.Where(f => f.IsActive).ToList();

        var digSigField = activeFields.FirstOrDefault(f =>
            f.Type == FormFieldType.DigitalSignature && f.IsActive);

        if (digSigField is null)
            return Result.Fail(new ValidationError(
                "Template này không có trường chữ ký số.", "templateCode"));

        if (SignatureModeParser.Parse(digSigField.ValidationRulesJson) != SignatureMode.ServerDocHash)
            return Result.Fail(new ValidationError(
                "Trường chữ ký số không sử dụng chế độ ServerDocHash.", "templateCode"));

        var hashBytes = FormDataCanonicalizer.ComputeMode2DocHash(
            template.Code,
            template.Version,
            request.Data,
            activeFields);

        return Result.Ok(new SignatureDocHashDto(
            HashBase64: Convert.ToBase64String(hashBytes),
            HashAlg:    "SHA256"));
    }

    // Dapper projection row — same minimal shape as SubmitPublicFormCommandHandler.TemplateIdRow
    private sealed class TemplateIdRow
    {
        public long Id { get; set; }
        public Guid TenantId { get; set; }
    }
}
