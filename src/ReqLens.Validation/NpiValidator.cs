namespace ReqLens.Validation;

/// <summary>
/// Validates a National Provider Identifier: 10 digits, last of which is a Luhn check digit
/// computed over the 9-digit base prefixed with the NPI-specific constant 80840.
/// </summary>
/// <remarks>
/// Algorithm:
///   1. Reject anything that is not exactly 10 digits.
///   2. Take the first 9 digits and prefix the constant "80840" (the NPI's ISO issuer prefix).
///   3. Luhn: from the rightmost digit of that 14-digit string, double every second digit;
///      if a doubled value exceeds 9, subtract 9. Sum all digits.
///   4. The check digit is (10 - (sum mod 10)) mod 10, and must equal the 10th digit.
///
/// This is the cheapest and most useful check in the pipeline. A model that hallucinates an NPI
/// produces a plausible-looking ten-digit number, and roughly nine times in ten the check digit
/// will not agree - so arithmetic catches what no amount of prompting reliably prevents.
/// </remarks>
public sealed class NpiValidator : IFieldValidator
{
    public string FieldName => "ordering_provider_npi";

    public ValidationResult Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Invalid("NPI is missing.");

        var npi = value.Trim();

        if (npi.Length != 10 || !npi.All(char.IsAsciiDigit))
            return ValidationResult.Invalid("NPI must be exactly 10 digits.");

        return npi[9] == CheckDigit(npi.AsSpan(0, 9))
            ? ValidationResult.Valid()
            : ValidationResult.Invalid("NPI check digit does not match.");
    }

    /// <summary>The Luhn check digit for a 9-digit NPI base, as a character.</summary>
    public static char CheckDigit(ReadOnlySpan<char> nineDigits)
    {
        // The ISO 7812 issuer identifier NPIs are allocated under. It is not printed on the
        // card, so it has to be prepended here before the checksum will agree.
        const string IssuerPrefix = "80840";

        var sum = 0;

        // Luhn counts positions from the right of the number INCLUDING its check digit, and
        // doubles every second one. The check digit itself is position 0, so the rightmost
        // base digit is position 1 - odd positions are the doubled ones.
        var position = 1;

        for (var i = nineDigits.Length - 1; i >= 0; i--)
            sum += Contribution(nineDigits[i] - '0', position++);

        for (var i = IssuerPrefix.Length - 1; i >= 0; i--)
            sum += Contribution(IssuerPrefix[i] - '0', position++);

        return (char)('0' + (10 - sum % 10) % 10);
    }

    private static int Contribution(int digit, int position)
    {
        if (position % 2 == 0)
            return digit;

        var doubled = digit * 2;
        return doubled > 9 ? doubled - 9 : doubled;
    }
}
