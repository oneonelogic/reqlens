using Amazon.Lambda.Core;
using ReqLens.Ai;
using ReqLens.Pipeline;

namespace ReqLens.Lambdas.Ingest;

/// <summary>
/// Pipeline output routed to CloudWatch Logs.
/// </summary>
/// <remarks>
/// Ingest publishes no metrics: it makes no model calls, so <see cref="RecordAsync"/> never
/// fires. The Extract function has its own implementation that does.
/// </remarks>
public sealed class LambdaPipelineLog(ILambdaContext context) : IPipelineLog
{
    public void Info(string message) => context.Logger.LogInformation(message);

    public void Warn(string message) => context.Logger.LogWarning(message);

    public Task RecordAsync(BedrockCallTelemetry call, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
