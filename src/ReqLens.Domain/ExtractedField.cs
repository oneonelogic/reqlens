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

    /// <summary>
    /// The literal snippet the model says it copied this value from.
    /// </summary>
    /// <remarks>
    /// Shown beside the value in the review console, so a reviewer checks the extraction against
    /// the page rather than against the model's own confidence score.
    /// </remarks>
    public string? SourceText { get; set; }

    /// <summary>
    /// Whether that snippet actually occurs in the OCR text.
    /// </summary>
    /// <remarks>
    /// A cheap, deterministic grounding check. The Bedrock guardrail scores grounding over the
    /// whole response; this is the per-field version, and it is the one that catches a
    /// well-formed value that was never on the page - a hallucinated NPI with a correct check
    /// digit passes every other test in this system. Null when there was nothing to check.
    /// </remarks>
    public bool? Grounded { get; set; }
}
