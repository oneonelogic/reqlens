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

    public List<ExtractedField> Fields { get; set; } = [];
    public List<ReviewAction> Reviews { get; set; } = [];
}
