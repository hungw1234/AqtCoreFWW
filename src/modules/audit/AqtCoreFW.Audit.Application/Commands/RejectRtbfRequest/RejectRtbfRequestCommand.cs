using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.RejectRtbfRequest;

/// <summary>Reject an RTBF request with a reason (e.g. legal hold applies).</summary>
public sealed record RejectRtbfRequestCommand(Guid RequestId, Guid ProcessedBy, string Reason) : IRequest<Result>;
