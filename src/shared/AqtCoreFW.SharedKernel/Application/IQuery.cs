using FluentResults;
using MediatR;

namespace AqtCoreFW.SharedKernel.Application;

/// <summary>Query marker — read operations returning Result{TResponse}.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>> { }
