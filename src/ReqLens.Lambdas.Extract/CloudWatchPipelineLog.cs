using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.Lambda.Core;
using ReqLens.Ai;
using ReqLens.Pipeline;

namespace ReqLens.Lambdas.Extract;

/// <summary>
/// Publishes per-call model metrics to CloudWatch under the ReqLens namespace.
/// </summary>
/// <remarks>
/// Built into the first Bedrock call rather than retrofitted. Every dimension here is one a
/// question gets asked about later: which model served this, which role in the chain it was
/// playing, and whether a guardrail fired. Without the role dimension "cost went up" and
/// "fallbacks went up" look identical on a chart.
///
/// A metric publish must never be the reason a document fails. Every failure here is swallowed
/// and logged.
/// </remarks>
public sealed class CloudWatchPipelineLog(IAmazonCloudWatch cloudWatch, ILambdaContext context) : IPipelineLog
{
    public const string Namespace = "ReqLens";

    public void Info(string message) => context.Logger.LogInformation(message);

    public void Warn(string message) => context.Logger.LogWarning(message);

    public async Task RecordAsync(BedrockCallTelemetry call, CancellationToken cancellationToken = default)
    {
        var dimensions = new List<Dimension>
        {
            new() { Name = "ModelId", Value = call.ModelId },
            new() { Name = "Role", Value = call.Role.ToString() }
        };

        var data = new List<MetricDatum>
        {
            Datum("InputTokens", call.InputTokens, StandardUnit.Count, dimensions),
            Datum("OutputTokens", call.OutputTokens, StandardUnit.Count, dimensions),
            Datum("EstimatedCostUsd", (double)call.EstimatedCostUsd, StandardUnit.None, dimensions),
            Datum("LatencyMs", call.LatencyMs, StandardUnit.Milliseconds, dimensions),
            Datum("GuardrailIntervened", call.GuardrailIntervened ? 1 : 0, StandardUnit.Count, dimensions),
            Datum("SchemaInvalid", call.SchemaValid ? 0 : 1, StandardUnit.Count, dimensions)
        };

        if (call.MinFieldConfidence is { } confidence)
            data.Add(Datum("MinFieldConfidence", confidence, StandardUnit.None, dimensions));

        // The structured line goes out whatever CloudWatch does with the metrics. Logs are the
        // record that survives a metrics outage, and this one is greppable per document.
        context.Logger.LogInformation(System.Text.Json.JsonSerializer.Serialize(new
        {
            evt = "bedrock_call",
            document = call.DocumentKey,
            model = call.ModelId,
            role = call.Role.ToString(),
            attempt = call.Attempt,
            latencyMs = call.LatencyMs,
            inputTokens = call.InputTokens,
            outputTokens = call.OutputTokens,
            costUsd = call.EstimatedCostUsd,
            guardrail = call.GuardrailIntervened,
            schemaValid = call.SchemaValid,
            minConfidence = call.MinFieldConfidence,
            failure = call.FailureReason
        }));

        try
        {
            await cloudWatch.PutMetricDataAsync(
                new PutMetricDataRequest { Namespace = Namespace, MetricData = data }, cancellationToken);
        }
        catch (Exception ex)
        {
            context.Logger.LogWarning($"Metric publish failed, continuing: {ex.Message}");
        }
    }

    private static MetricDatum Datum(string name, double value, StandardUnit unit, List<Dimension> dimensions) =>
        new()
        {
            MetricName = name,
            Value = value,
            Unit = unit,
            Dimensions = dimensions,
            Timestamp = DateTime.UtcNow
        };
}
