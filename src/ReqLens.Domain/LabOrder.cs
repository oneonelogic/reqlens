namespace ReqLens.Domain;

/// <summary>One requisition form as it moves from scanned PDF to structured, accepted order.</summary>
public class LabOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>S3 key of the uploaded requisition PDF.</summary>
    public required string SourceObjectKey { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Received;

    /// <summary>Lowest field confidence on the order; drives the review gate.</summary>
    public double? OverallConfidence { get; set; }

    /// <summary>Which model in the chain actually served this extraction.</summary>
    public string? ModelId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Why this order is in the queue, in the reviewer's language. Computed by the validation
    /// layer at extraction time and stored, rather than recomputed on read: the reason has to
    /// stay what it was when the decision was made, even after the catalogue changes underneath.
    /// </summary>
    public List<string> ReviewReasons { get; set; } = [];

    /// <summary>
    /// Set when the model chain produced nothing at all - a guardrail block, or every model in
    /// the chain exhausted. The order still lands in the queue; a human just has no extracted
    /// values to check, only the scan.
    /// </summary>
    public string? ExtractionFailure { get; set; }

    public List<ExtractedField> Fields { get; set; } = [];
    public List<ReviewAction> Reviews { get; set; } = [];
    public List<ExtractionCall> Calls { get; set; } = [];
}
