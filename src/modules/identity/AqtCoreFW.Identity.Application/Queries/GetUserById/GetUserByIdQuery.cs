using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Queries.GetUserById;

public sealed record GetUserByIdQuery(Guid UserId) : IQuery<UserDto>;
