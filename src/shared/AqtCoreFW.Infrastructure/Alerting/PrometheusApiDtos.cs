namespace AqtCoreFW.Infrastructure.Alerting;

// Minimal projections of the Prometheus HTTP API instant-query response.
// Used by AlertEvaluationJob to deserialize GET /api/v1/query results.

internal sealed class PrometheusQueryResponse
{
    public string? Status { get; init; }
    public PrometheusData? Data { get; init; }
}

internal sealed class PrometheusData
{
    public List<PrometheusResult>? Result { get; init; }
}

internal sealed class PrometheusResult
{
    public List<object>? Value { get; init; }  // [unixTimestamp, "valueString"]
}
