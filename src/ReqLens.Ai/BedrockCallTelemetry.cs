namespace ReqLens.Ai;

/// <summary>
/// Emitted for every model call, without exception. Built in from the first Bedrock call rather
/// than retrofitted: this record is what the CloudWatch dashboard, the cost alarm and the
/// fallback/escalation charts are all computed from.
/// Never carries extracted values - only pointers - so no PHI-shaped text reaches the logs.
/// </summary>
public sealed record BedrockCallTelemetry
{
    public required string ModelId { get; init; }
    public required ModelRole Role { get; init; }
    public required string DocumentKey { get; init; }

    public int Attempt { get; init; } = 1;
    public long LatencyMs { get; init; }

    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public decimal EstimatedCostUsd { get; init; }

    /// <summary>True when a Bedrock Guardrail intervened on the request or the response.</summary>
    public bool GuardrailIntervened { get; init; }

    /// <summary>False when the model's tool output did not satisfy the JSON schema - the escalation trigger.</summary>
    public bool SchemaValid { get; init; }

    public double? MinFieldConfidence { get; init; }
    public string? FailureReason { get; init; }

    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}
