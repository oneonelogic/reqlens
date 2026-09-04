using ReqLens.Domain;

namespace ReqLens.Ai;

/// <summary>
/// The result of running one document through the model chain.
/// </summary>
/// <remarks>
/// Carries every attempt, not just the one that answered. A document that succeeded only after
/// falling back to a second vendor cost more and took longer than one that did not, and the
/// dashboard is uninteresting if that difference is thrown away at the end of the call.
/// </remarks>
public sealed record ExtractionOutcome
{
    public required IReadOnlyList<ExtractedField> Fields { get; init; }

    /// <summary>One entry per model call made, in the order they were made.</summary>
    public required IReadOnlyList<BedrockCallTelemetry> Calls { get; init; }

    /// <summary>The call that decided the outcome - the last one made.</summary>
    public BedrockCallTelemetry Telemetry => Calls[^1];

    public bool Succeeded => Fields.Count > 0;

    /// <summary>Set when the chain gave up. Null on success.</summary>
    public string? FailureReason => Succeeded ? null : Telemetry.FailureReason;
}

/// <summary>Turns OCR text from one requisition into structured, per-field-scored output.</summary>
public interface IRequisitionExtractor
{
    Task<ExtractionOutcome> ExtractAsync(
        string documentKey,
        string ocrText,
        CancellationToken cancellationToken = default);
}
