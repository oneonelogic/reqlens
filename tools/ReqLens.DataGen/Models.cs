namespace ReqLens.DataGen;

// Plain classes with settable properties: CsvHelper binds positional records to the
// compiler-generated copy constructor and throws on the argument count.
public sealed class Tenant
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class Provider
{
    public string Npi { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string Credential { get; set; } = "";
    public string TenantSlug { get; set; } = "";
    public string Display => $"{FirstName} {LastName}, {Credential}";
}

public sealed class Patient
{
    public string Mrn { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string Dob { get; set; } = "";
    public string Sex { get; set; } = "";
}

public sealed class CatalogEntry
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string SpecimenType { get; set; } = "";
    public bool Active { get; set; }
}

public sealed class IcdCode
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// What makes a given requisition hard. These are the reasons a real intake queue exists, so the
/// generated set deliberately contains them rather than twenty clean forms.
/// </summary>
[Flags]
public enum Defect
{
    None            = 0,
    MissingConsent  = 1 << 0,
    AmbiguousPanel  = 1 << 1,
    MissingDiagnosis= 1 << 2,
    InvalidNpi      = 1 << 3,
    UnknownPanelCode= 1 << 4,
    InactivePanel   = 1 << 5,
    HandwrittenNote = 1 << 6,
    SpecimenMismatch= 1 << 7
}

public enum Layout { Compact, TwoColumn, Gridded }

/// <summary>One requisition: what gets drawn, and what the extractor should have found.</summary>
public record Requisition
{
    public required string Id { get; init; }
    public required Tenant Tenant { get; init; }
    public required Provider Provider { get; init; }
    public required Patient Patient { get; init; }
    public required CatalogEntry Panel { get; init; }
    public required IcdCode? Diagnosis { get; init; }
    public required string CollectionDate { get; init; }
    public required string SpecimenType { get; init; }
    public required bool ConsentObtained { get; init; }
    public required Layout Layout { get; init; }
    public required Defect Defects { get; init; }

    /// <summary>The NPI actually printed on the form - not always the provider's real one.</summary>
    public required string PrintedNpi { get; init; }

    /// <summary>The panel text actually printed - ambiguous on purpose in some documents.</summary>
    public required string PrintedPanel { get; init; }

    /// <summary>
    /// The panel code actually printed, which is what a correct extraction returns. Null when the
    /// form names no code at all. This is deliberately NOT Panel.Code: on an out-of-catalog
    /// document the form says GXP-999, and grading against the catalogue entry would penalise a
    /// correct read and hide the very validation path the document exists to exercise.
    /// </summary>
    public required string? PrintedPanelCode { get; init; }
}
