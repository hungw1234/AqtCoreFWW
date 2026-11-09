using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Self-service: the authenticated user sets/changes their own <c>SigningNationalId</c> (CCCD) so they can then
/// enroll a signing certificate. Identity (<paramref name="TenantId"/>/<paramref name="UserId"/>) is resolved from
/// the JWT by the controller and never bound from the request body — a caller can only set their OWN CCCD.
///
/// <para>Changing the CCCD is refused once the user has an <b>active</b> enrollment (Pending/Approved bind the
/// certificate to this CCCD). Active-only — a user whose enrollments were all Rejected/revoked may still correct
/// their CCCD and re-enroll, which is the whole point of a self-service path. (The admin update path uses the
/// stricter <c>HasAnyEnrollment</c> because its full-replace PUT semantics could silently null the CCCD.)</para>
/// </summary>
public sealed record SetMySigningNationalIdCommand(Guid TenantId, Guid UserId, string NationalId)
    : IRequest<Result>;
