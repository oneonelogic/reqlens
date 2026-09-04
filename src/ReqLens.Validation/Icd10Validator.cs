using System.Text.RegularExpressions;

namespace ReqLens.Validation;

/// <summary>
/// Validates an ICD-10-CM diagnosis code: format check, then membership in the loaded code list.
/// </summary>
/// <remarks>
/// Format alone is not enough - a well-formed code that is not in the list is exactly the failure
/// mode a model produces, so the list lookup is the real check. Format is still worth running
/// first because it gives a better message: "E11.99999 is not shaped like a code" is more useful
/// to a reviewer than "E11.99999 is not a code".
/// </remarks>
public sealed partial class Icd10Validator : IFieldValidator
{
    private readonly IReadOnlySet<string> _knownCodes;

    public Icd10Validator(IReadOnlySet<string> knownCodes) => _knownCodes = knownCodes;

    /// <summary>Uses the embedded code list.</summary>
    public Icd10Validator() : this(Icd10CodeSet.Default) { }

    public string FieldName => "diagnosis_code";

    public ValidationResult Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Invalid("Diagnosis code is missing.");

        var code = value.Trim().ToUpperInvariant();

        if (!Shape().IsMatch(code))
            return ValidationResult.Invalid($"'{value}' is not a well-formed ICD-10-CM code.");

        return _knownCodes.Contains(code)
            ? ValidationResult.Valid()
            : ValidationResult.Invalid($"'{code}' is well formed but is not a known ICD-10-CM code.");
    }

    /// <summary>
    /// One letter, two characters, then optionally a dot and up to four more.
    /// U is reserved and I is not used as a leading character in ICD-10-CM, hence [A-TV-Z].
    /// The third character may be A or B - C4A (Merkel cell carcinoma) is a real code - so it is
    /// not simply a digit.
    /// </summary>
    [GeneratedRegex(@"^[A-TV-Z][0-9][0-9AB](\.[0-9A-TV-Z]{1,4})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
