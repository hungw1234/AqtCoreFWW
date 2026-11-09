using System.Linq.Expressions;
using AqtCoreFW.SharedKernel.Application;
using Hangfire;

namespace AqtCoreFW.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire implementation of IBackgroundJobService.
/// Registered as Scoped — Hangfire resolves job instances from DI per execution.
/// Thin wrapper: all scheduling logic stays in Hangfire's static API.
/// </summary>
public sealed class HangfireBackgroundJobService : IBackgroundJobService
{
    public string Enqueue<T>(Expression<Action<T>> methodCall) =>
        BackgroundJob.Enqueue(methodCall);

    public string Enqueue<T>(Expression<Func<T, Task>> methodCall) =>
        BackgroundJob.Enqueue(methodCall);

    public string Enqueue<T>(string queue, Expression<Func<T, Task>> methodCall) =>
        BackgroundJob.Enqueue(queue, methodCall);

    public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay) =>
        BackgroundJob.Schedule(methodCall, delay);

    public void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression) =>
        RecurringJob.AddOrUpdate(jobId, methodCall, cronExpression);

    public string ContinueJobWith<T>(string parentJobId, Expression<Action<T>> methodCall) =>
        BackgroundJob.ContinueJobWith(parentJobId, methodCall);

    public void RemoveRecurring(string jobId) =>
        RecurringJob.RemoveIfExists(jobId);
}
