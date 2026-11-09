using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.GetCredentialPolicyById;

public sealed record GetCredentialPolicyByIdQuery(long Id) : IQuery<CredentialPolicyDto>;
