namespace ReqLens.Domain;

public enum FieldValidationState
{
    Unvalidated,
    Valid,
    Invalid,
    Unverifiable
}

/// <summary>One model-extracted field, with the confidence and validation outcome that gate it.</summary>
public class ExtractedField
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid TenantId { get; set; }

    public required string Name { get; set; }
    public string? Value { get; set; }

    /// <summary>Model-reported confidence, 0..1.</summary>
    public double Confidence { get; set; }

    public FieldValidationState ValidationState { get; set; } = FieldValidationState.Unvalidated;

    /// <summary>Why validation failed, when it did. Null when valid.</summary>
    public string? ValidationMessage { get; set; }

    public int? SourcePage { get; set; }
}
