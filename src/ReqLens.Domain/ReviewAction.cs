namespace ReqLens.Domain;

public enum ReviewVerdict
{
    Approved,
    Corrected,
    Rejected
}

/// <summary>
/// Append-only audit row: who touched what, when, and what the value was before and after.
/// A Corrected verdict is also a scored model miss - this table is the overturn-rate source.
/// </summary>
public class ReviewAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? FieldId { get; set; }

    public required string ReviewerId { get; set; }
    public ReviewVerdict Verdict { get; set; }

    public string? ValueBefore { get; set; }
    public string? ValueAfter { get; set; }
    public string? Note { get; set; }

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
