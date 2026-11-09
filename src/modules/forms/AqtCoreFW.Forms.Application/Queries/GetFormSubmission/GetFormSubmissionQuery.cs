using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.GetFormSubmission;

/// <summary>Returns a single form submission by ID.</summary>
public sealed record GetFormSubmissionQuery(Guid SubmissionId, Guid TenantId) : IQuery<FormSubmissionDto>;
