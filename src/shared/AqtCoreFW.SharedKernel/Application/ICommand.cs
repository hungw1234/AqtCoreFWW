using FluentResults;
using MediatR;

namespace AqtCoreFW.SharedKernel.Application;

/// <summary>Non-generic marker for MediatR pipeline behavior targeting all commands.</summary>
public interface IBaseCommand { }

/// <summary>Command marker — write operations returning Result{TResponse}.</summary>
public interface ICommand<TResponse> : IBaseCommand, IRequest<Result<TResponse>> { }

/// <summary>Command with no return value.</summary>
public interface ICommand : IBaseCommand, IRequest<Result> { }
