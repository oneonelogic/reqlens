using ReqLens.Domain;

namespace ReqLens.Validation;

/// <summary>
/// What the deterministic layer concluded about one requisition, in the same shape the golden
/// set records its expectations. Keeping the two shapes identical is what lets the eval harness
/// grade validation directly rather than re-deriving it from field states.
/// </summary>
public sealed record ValidationOutcome
{
    /// <summary>Null when the extraction returned no NPI field at all.</summary>
    public bool? NpiValid { get; init; }

    public bool PanelCodePresent { get; init; }
    public bool? PanelInCatalog { get; init; }
    public bool? PanelActive { get; init; }
    public bool? SpecimenMatches { get; init; }

    public bool DiagnosisCodePresent { get; init; }

    /// <summary>Null when the form carries no diagnosis code - there is nothing to judge.</summary>
    public bool? Icd10Valid { get; init; }

    public bool ShouldNeedReview { get; init; }
}

/// <summary>
/// The full verdict: per-field states (written back onto the fields themselves), the outcome
/// summary, and the reasons a human is being asked to look.
/// </summary>
public sealed record RequisitionAssessment
{
    public required IReadOnlyList<ExtractedField> Fields { get; init; }
    public required ValidationOutcome Outcome { get; init; }
    public required CatalogCheck Catalog { get; init; }

    /// <summary>Ordered, human-readable. This is what the review queue shows as the "why".</summary>
    public required IReadOnlyList<string> ReviewReasons { get; init; }

    /// <summary>Lowest confidence across the graded fields; null when nothing was extracted.</summary>
    public double? MinConfidence { get; init; }

    public bool NeedsReview => Outcome.ShouldNeedReview;
}
