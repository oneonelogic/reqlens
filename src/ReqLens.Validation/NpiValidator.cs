namespace ReqLens.Validation;

/// <summary>
/// Validates a National Provider Identifier: 10 digits, last of which is a Luhn check digit
/// computed over the 9-digit base prefixed with the NPI-specific constant 80840.
/// </summary>
/// <remarks>
/// PAIRING STUB - intentionally unimplemented. Glenn writes this one by hand; it is the
/// check-digit algorithm he will be asked to walk through in the interview.
/// Algorithm:
///   1. Reject anything that is not exactly 10 digits.
///   2. Take the first 9 digits and prefix the constant "80840" (the NPI's ISO issuer prefix).
///   3. Luhn: from the rightmost digit of that 14-digit string, double every second digit;
///      if a doubled value exceeds 9, subtract 9. Sum all digits.
///   4. The check digit is (10 - (sum mod 10)) mod 10, and must equal the 10th digit.
/// </remarks>
public sealed class NpiValidator : IFieldValidator
{
    public string FieldName => "ordering_provider_npi";

    public ValidationResult Validate(string? value)
        => throw new NotImplementedException("Pairing stub: implement the NPI Luhn check digit.");
}
