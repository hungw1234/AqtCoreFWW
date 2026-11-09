using AqtCoreFW.Audit.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.GetClassificationInventory;

/// <summary>Scans all loaded domain assemblies for [DataClassification] annotations.</summary>
public sealed record GetClassificationInventoryQuery : IRequest<Result<ClassificationInventoryDto>>;
