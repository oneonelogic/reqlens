namespace ReqLens.Validation;

public readonly record struct ValidationResult(bool IsValid, string? Message)
{
    public static ValidationResult Valid() => new(true, null);
    public static ValidationResult Invalid(string message) => new(false, message);
}
