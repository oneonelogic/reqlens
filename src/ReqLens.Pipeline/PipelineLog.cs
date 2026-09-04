using ReqLens.Ai;

namespace ReqLens.Pipeline;

/// <summary>
/// Where a pipeline step reports what it did.
/// </summary>
/// <remarks>
/// The steps below are the actual pipeline, and they run in two places: inside a Lambda, where
/// "report" means CloudWatch Logs and a metric datum, and on a laptop, where it means stdout.
/// Neither of those belongs in the logic, so both arrive through this.
/// </remarks>
public interface IPipelineLog
{
    void Info(string message);
    void Warn(string message);

    /// <summary>Called once per model call, whatever the outcome. Never allowed to throw.</summary>
    Task RecordAsync(BedrockCallTelemetry call, CancellationToken cancellationToken = default);
}

/// <summary>Console output, for the CLI and for tests.</summary>
public sealed class ConsolePipelineLog(bool verbose = false) : IPipelineLog
{
    public void Info(string message) => Console.WriteLine($"  {message}");

    public void Warn(string message) => Console.Error.WriteLine($"  ! {message}");

    public Task RecordAsync(BedrockCallTelemetry call, CancellationToken cancellationToken = default)
    {
        if (verbose || !call.SchemaValid || call.GuardrailIntervened)
            Console.WriteLine(
                $"  {call.ModelId} [{call.Role}] attempt {call.Attempt}: {call.LatencyMs} ms, "
                + $"{call.InputTokens}+{call.OutputTokens} tokens, ${call.EstimatedCostUsd:F4}"
                + (call.FailureReason is { } reason ? $" - {reason}" : ""));

        return Task.CompletedTask;
    }
}
