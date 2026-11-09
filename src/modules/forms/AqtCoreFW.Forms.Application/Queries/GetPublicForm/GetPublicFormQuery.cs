using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.GetPublicForm;

/// <summary>
/// Returns template JSON for a public (anonymous) form by code.
/// No tenant context — code must be globally unique or scoped by domain.
/// </summary>
public sealed record GetPublicFormQuery(string TemplateCode) : IQuery<FormTemplateDto>;
