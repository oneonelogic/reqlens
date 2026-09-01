namespace ReqLens.Domain;

/// <summary>
/// One model call, recorded. The telemetry record that the Extract Lambda emits to CloudWatch,
/// kept relationally as well.
/// </summary>
/// <remarks>
/// CloudWatch answers "what is the fleet doing" and this table answers "what happened to THIS
/// document" - which is the question a reviewer looking at a suspicious order actually has, and
/// one a metric namespace cannot answer at all. A document that needed three calls across two
/// vendors has three rows here, and the review console shows them.
///
/// Deliberately carries no extracted values, only counts and pointers, so the audit trail never
/// becomes a second copy of the patient data.
/// </remarks>
public class ExtractionCall
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid TenantId { get; set; }

    public required string ModelId { get; set; }

    /// <summary>Primary, Availability or Escalation. A string so the domain need not know the chain.</summary>
    public required string Role { get; set; }

    public int Attempt { get; set; }
    public long LatencyMs { get; set; }

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }

    public bool GuardrailIntervened { get; set; }
    public bool SchemaValid { get; set; }

    public double? MinFieldConfidence { get; set; }
    public string? FailureReason { get; set; }

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
