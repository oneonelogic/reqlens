namespace ReqLens.Validation;

/// <summary>
/// Validates an ICD-10-CM diagnosis code: format check, then membership in the loaded code list.
/// </summary>
/// <remarks>
/// PAIRING STUB - intentionally unimplemented.
/// Format: one letter, two digits, then optionally a dot and up to four alphanumeric characters
/// (e.g. E11.9, Z00.00, C50.911). Format alone is not enough - a well-formed code that is not in
/// the list is exactly the failure mode a model produces, so the list lookup is the real check.
/// </remarks>
public sealed class Icd10Validator : IFieldValidator
{
    private readonly IReadOnlySet<string> _knownCodes;

    public Icd10Validator(IReadOnlySet<string> knownCodes) => _knownCodes = knownCodes;

    public string FieldName => "diagnosis_code";

    public ValidationResult Validate(string? value)
        => throw new NotImplementedException("Pairing stub: implement ICD-10 format + code-list lookup.");
}
