using System.Text.Json.Serialization;

namespace AqtCoreFW.Contracts.Domain.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContractStatus
{
    Draft,
    Active,
    Expired,
    Terminated
}
