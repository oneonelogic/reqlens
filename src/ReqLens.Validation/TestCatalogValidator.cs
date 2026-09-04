using ReqLens.Domain;

namespace ReqLens.Validation;

/// <summary>
/// What the catalogue had to say about the panel the form asked for.
/// </summary>
/// <remarks>
/// Three of the four answers are nullable, and the nulls are load-bearing. A property of a
/// catalogue row cannot be evaluated when no row resolved - "is the panel active" has no answer
/// for a code that is in no catalogue. That is a different situation from looking something up
/// and finding it false, and collapsing the two would hide which path a document exercised.
/// <see cref="PanelCodePresent"/> and <see cref="PanelInCatalog"/> say which situation you are in.
/// </remarks>
public sealed record CatalogCheck(
    bool PanelCodePresent,
    bool? PanelInCatalog,
    bool? PanelActive,
    bool? SpecimenMatches,
    TestCatalogEntry? Entry);

/// <summary>
/// Resolves the panel code printed on a form against the tenant's catalogue.
/// </summary>
/// <remarks>
/// Not an <see cref="IFieldValidator"/>: it needs two fields (the panel code and the specimen)
/// plus the tenant's catalogue, so the single-value interface does not fit. Forcing it into that
/// shape would mean smuggling state through a field validator, which is worse than an honest
/// second interface.
/// </remarks>
public sealed class TestCatalogValidator
{
    public CatalogCheck Check(
        string? panelCode,
        string? specimenType,
        IReadOnlyCollection<TestCatalogEntry> catalog)
    {
        if (string.IsNullOrWhiteSpace(panelCode))
            return new CatalogCheck(PanelCodePresent: false, null, null, null, null);

        var code = panelCode.Trim();

        var entry = catalog.FirstOrDefault(e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            return new CatalogCheck(PanelCodePresent: true, PanelInCatalog: false, null, null, null);

        return new CatalogCheck(
            PanelCodePresent: true,
            PanelInCatalog: true,
            PanelActive: entry.Active,
            SpecimenMatches: SpecimenMatches(specimenType, entry.SpecimenType),
            Entry: entry);
    }

    /// <summary>
    /// Compared case- and whitespace-insensitively. Deliberately not fuzzier than that: accepting
    /// "blood" for "Whole Blood EDTA" would swallow the specimen-mismatch case this pipeline is
    /// meant to catch, and a wrong tube is a re-draw on a real patient.
    /// </summary>
    private static bool? SpecimenMatches(string? actual, string? required)
    {
        if (string.IsNullOrWhiteSpace(required)) return null;
        if (string.IsNullOrWhiteSpace(actual)) return false;

        return string.Equals(Normalise(actual), Normalise(required), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string value) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
