using ReqLens.Contracts;
using ReqLens.Domain;
using ReqLens.Validation;

namespace ReqLens.Lambdas.Api;

/// <summary>
/// Domain entities to wire contracts. Kept explicit rather than reflective so that adding a
/// column to an entity is not the same thing as publishing it to a browser.
/// </summary>
public static class Mapping
{
    private static readonly ConfidenceGate Gate = new();

    public static OrderSummaryDto ToSummary(this LabOrder order) => new(
        order.Id,
        order.SourceObjectKey,
        Path.GetFileName(order.SourceObjectKey),
        order.Status.ToString(),
        order.OverallConfidence,
        order.ModelId,
        order.ReviewReasons,
        order.CreatedAt,
        order.UpdatedAt);

    public static FieldDto ToDto(this ExtractedField field) => new(
        field.Id,
        field.Name,
        RequisitionFields.Label(field.Name),
        field.Value,
        field.Confidence,
        Gate.Band(field.Confidence).ToString(),
        field.ValidationState.ToString(),
        field.ValidationMessage,
        field.SourceText,
        field.Grounded);

    public static ExtractionCallDto ToDto(this ExtractionCall call) => new(
        call.ModelId,
        call.Role,
        call.Attempt,
        call.LatencyMs,
        call.InputTokens,
        call.OutputTokens,
        call.EstimatedCostUsd,
        call.GuardrailIntervened,
        call.SchemaValid,
        call.MinFieldConfidence,
        call.FailureReason,
        call.At);

    public static ReviewActionDto ToDto(this ReviewAction action, IReadOnlyDictionary<Guid, string> fieldNames) => new(
        action.Id,
        action.ReviewerId,
        action.Verdict.ToString(),
        action.FieldId is { } id ? fieldNames.GetValueOrDefault(id) : null,
        action.ValueBefore,
        action.ValueAfter,
        action.Note,
        action.At);
}
