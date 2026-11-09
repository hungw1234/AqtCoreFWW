using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain.Repositories;

namespace AqtCoreFW.Forms.Domain.Repositories;

/// <summary>Form submission repository — write path only. Query handlers use IReadDbConnection (Dapper).</summary>
public interface IFormSubmissionRepository : IRepository<FormSubmission, Guid>
{
}
