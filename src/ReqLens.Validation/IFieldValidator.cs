namespace ReqLens.Validation;

/// <summary>
/// Deterministic check on one extracted field. This layer is the second half of the guardrail
/// story - the model is constrained by a JSON schema, then everything it returns is re-checked
/// here by code that cannot hallucinate.
/// </summary>
public interface IFieldValidator
{
    /// <summary>Field name this validator claims, matching <see cref="ReqLens.Domain.ExtractedField.Name"/>.</summary>
    string FieldName { get; }

    ValidationResult Validate(string? value);
}
