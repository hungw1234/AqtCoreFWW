namespace AqtCoreFW.Infrastructure.Clients;

/// <summary>
/// Selects how <see cref="AqtCoreFW.SharedKernel.Contracts.Clients.IOrgUnitModuleClient"/> is resolved.
/// <list type="bullet">
/// <item><see cref="InProcess"/> (default): direct Dapper query inside the monolith.</item>
/// <item><see cref="Http"/>: the typed <see cref="HttpOrgUnitModuleClient"/> calling the OrgUnit service
/// once it is extracted.</item>
/// </list>
/// </summary>
public enum OrgUnitClientTransport
{
    InProcess,
    Http,
}
