namespace ReqLens.Contracts;

public sealed record TenantDto(Guid Id, string Name, string Slug);

/// <summary>One row in the review queue.</summary>
public sealed record OrderSummaryDto(
    Guid Id,
    string SourceObjectKey,
    string DocumentName,
    string Status,
    double? OverallConfidence,
    string? ModelId,
    IReadOnlyList<string> ReviewReasons,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One extracted field as the console shows it.
/// </summary>
/// <remarks>
/// Carries the source snippet and the grounding flag as well as the value, because the question
/// a reviewer is answering is "does this match the page", and the model's confidence score is
/// the least useful evidence available for that.
/// </remarks>
public sealed record FieldDto(
    Guid Id,
    string Name,
    string Label,
    string? Value,
    double Confidence,
    string Band,
    string ValidationState,
    string? ValidationMessage,
    string? SourceText,
    bool? Grounded);

public sealed record ExtractionCallDto(
    string ModelId,
    string Role,
    int Attempt,
    long LatencyMs,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostUsd,
    bool GuardrailIntervened,
    bool SchemaValid,
    double? MinFieldConfidence,
    string? FailureReason,
    DateTimeOffset At);

public sealed record ReviewActionDto(
    Guid Id,
    string ReviewerId,
    string Verdict,
    string? FieldName,
    string? ValueBefore,
    string? ValueAfter,
    string? Note,
    DateTimeOffset At);

public sealed record OrderDetailDto(
    OrderSummaryDto Summary,
    string? ExtractionFailure,
    IReadOnlyList<FieldDto> Fields,
    IReadOnlyList<ExtractionCallDto> Calls,
    IReadOnlyList<ReviewActionDto> Reviews);

public sealed record FieldCorrectionDto(Guid FieldId, string? Value);

/// <summary>
/// What a reviewer submits. Corrections are sent whatever the verdict, so that "approved, but I
/// fixed the MRN" is expressible - which is the common case and the one that feeds overturn rate.
/// </summary>
public sealed record ReviewSubmissionDto(
    string ReviewerId,
    string Verdict,
    IReadOnlyList<FieldCorrectionDto> Corrections,
    string? Note);

/// <summary>
/// The drift signal: how often a human disagreed with the model.
/// </summary>
/// <remarks>
/// Counted over fields, not orders. An order where one digit of an MRN was fixed and an order
/// rewritten from scratch are both "one corrected order", and treating them alike hides the
/// thing the number exists to detect.
/// </remarks>
public sealed record OverturnMetricsDto(
    int WindowDays,
    int OrdersReviewed,
    int OrdersApproved,
    int OrdersCorrected,
    int OrdersRejected,
    int FieldsReviewed,
    int FieldsCorrected,
    double FieldOverturnRate,
    double OrderOverturnRate);
