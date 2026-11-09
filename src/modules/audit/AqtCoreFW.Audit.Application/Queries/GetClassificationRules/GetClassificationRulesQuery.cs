using AqtCoreFW.Audit.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.GetClassificationRules;

/// <summary>Returns all 4 classification-level rules ordered by Level ascending.</summary>
public sealed record GetClassificationRulesQuery : IRequest<Result<IReadOnlyList<ClassificationRuleDto>>>;
