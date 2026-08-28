using ReqLens.Domain;

namespace ReqLens.Ai;

public sealed record ExtractionOutcome(
    IReadOnlyList<ExtractedField> Fields,
    BedrockCallTelemetry Telemetry);

/// <summary>Turns OCR text from one requisition into structured, per-field-scored output.</summary>
public interface IRequisitionExtractor
{
    Task<ExtractionOutcome> ExtractAsync(
        string documentKey,
        string ocrText,
        CancellationToken cancellationToken = default);
}
