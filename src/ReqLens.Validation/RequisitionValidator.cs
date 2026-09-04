using ReqLens.Domain;

namespace ReqLens.Validation;

/// <summary>
/// Runs every deterministic check over one extraction and decides whether a human has to look.
/// </summary>
/// <remarks>
/// This is the half of the guardrail story that cannot hallucinate. The model is constrained by a
/// JSON schema on the way out; everything it returns is then re-derived here by arithmetic and
/// table lookups. Nothing in this class asks the model whether it was right.
/// </remarks>
public sealed class RequisitionValidator(
    NpiValidator? npi = null,
    Icd10Validator? icd10 = null,
    TestCatalogValidator? catalog = null,
    ConfidenceGate? gate = null)
{
    private readonly NpiValidator _npi = npi ?? new NpiValidator();
    private readonly Icd10Validator _icd10 = icd10 ?? new Icd10Validator();
    private readonly TestCatalogValidator _catalog = catalog ?? new TestCatalogValidator();
    private readonly ConfidenceGate _gate = gate ?? new ConfidenceGate();

    public RequisitionAssessment Assess(
        IReadOnlyList<ExtractedField> fields,
        IReadOnlyCollection<TestCatalogEntry> tenantCatalog)
    {
        var byName = fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var reasons = new List<string>();

        string? ValueOf(string name) =>
            byName.TryGetValue(name, out var f) && !string.IsNullOrWhiteSpace(f.Value) ? f.Value.Trim() : null;

        var catalogCheck = _catalog.Check(
            ValueOf(RequisitionFields.TestPanelCode),
            ValueOf(RequisitionFields.SpecimenType),
            tenantCatalog);

        foreach (var field in fields)
            ApplyFieldState(field, catalogCheck);

        // ---- reasons a human is being asked to look --------------------------------------

        foreach (var field in fields.Where(f => f.ValidationState == FieldValidationState.Invalid))
            reasons.Add($"{RequisitionFields.Label(field.Name)}: {field.ValidationMessage}");

        if (!catalogCheck.PanelCodePresent)
            reasons.Add("The form names a panel in prose but gives no code to resolve.");
        else if (catalogCheck.PanelInCatalog == false)
            reasons.Add($"Panel code '{ValueOf(RequisitionFields.TestPanelCode)}' is not in this clinic's catalogue.");

        if (ConsentObtained(byName) != true)
            reasons.Add("Consent for genetic testing is not recorded as obtained.");

        if (ValueOf(RequisitionFields.UnmappedNotes) is { } note)
            reasons.Add($"Free-text note on the form with nowhere structured to put it: \"{note}\"");

        var graded = fields.Where(f => RequisitionFields.Graded.Contains(f.Name)).ToList();
        double? minConfidence = graded.Count == 0 ? null : graded.Min(f => f.Confidence);

        if (minConfidence is { } min && min < _gate.AcceptThreshold)
        {
            var weakest = graded.OrderBy(f => f.Confidence).First();
            reasons.Add($"Lowest field confidence {min:P0} is below the {_gate.AcceptThreshold:P0} " +
                        $"accept threshold ({RequisitionFields.Label(weakest.Name)}).");
        }

        // An unverifiable field is not a failure, but it is an open question, and open questions
        // are what the queue is for.
        foreach (var field in fields.Where(f => f.ValidationState == FieldValidationState.Unverifiable))
            reasons.Add($"{RequisitionFields.Label(field.Name)}: {field.ValidationMessage}");

        var npiField = byName.GetValueOrDefault(RequisitionFields.ProviderNpi);

        var outcome = new ValidationOutcome
        {
            NpiValid = npiField is null ? null : npiField.ValidationState == FieldValidationState.Valid,
            PanelCodePresent = catalogCheck.PanelCodePresent,
            PanelInCatalog = catalogCheck.PanelInCatalog,
            PanelActive = catalogCheck.PanelActive,
            SpecimenMatches = catalogCheck.SpecimenMatches,
            DiagnosisCodePresent = ValueOf(RequisitionFields.DiagnosisCode) is not null,
            Icd10Valid = ValueOf(RequisitionFields.DiagnosisCode) is null
                ? null
                : byName[RequisitionFields.DiagnosisCode].ValidationState == FieldValidationState.Valid,
            ShouldNeedReview = reasons.Count > 0
        };

        return new RequisitionAssessment
        {
            Fields = fields,
            Outcome = outcome,
            Catalog = catalogCheck,
            ReviewReasons = reasons,
            MinConfidence = minConfidence
        };
    }

    private void ApplyFieldState(ExtractedField field, CatalogCheck catalogCheck)
    {
        var present = !string.IsNullOrWhiteSpace(field.Value);

        switch (field.Name)
        {
            case RequisitionFields.ProviderNpi:
                Set(field, _npi.Validate(field.Value));
                return;

            case RequisitionFields.DiagnosisCode:
                // Absent is not wrong. Plenty of requisitions arrive without one; it just means
                // nobody can say whether the code is valid, so a human decides.
                if (!present)
                    Mark(field, FieldValidationState.Unverifiable, "No diagnosis code is printed on the form.");
                else
                    Set(field, _icd10.Validate(field.Value));
                return;

            case RequisitionFields.TestPanelCode:
                if (!catalogCheck.PanelCodePresent)
                    Mark(field, FieldValidationState.Unverifiable, "The form names no panel code.");
                else if (catalogCheck.PanelInCatalog == false)
                    Mark(field, FieldValidationState.Invalid, "Not a panel in this clinic's catalogue.");
                else if (catalogCheck.PanelActive == false)
                    Mark(field, FieldValidationState.Invalid, $"Panel '{catalogCheck.Entry?.Name}' is retired and no longer offered.");
                else
                    Mark(field, FieldValidationState.Valid, null);
                return;

            case RequisitionFields.SpecimenType:
                if (catalogCheck.SpecimenMatches is null)
                    Mark(field, FieldValidationState.Unverifiable, "No catalogue row resolved, so the required specimen is unknown.");
                else if (catalogCheck.SpecimenMatches == false)
                    Mark(field, FieldValidationState.Invalid,
                        $"Panel requires {catalogCheck.Entry?.SpecimenType}, form says {field.Value}.");
                else
                    Mark(field, FieldValidationState.Valid, null);
                return;

            case RequisitionFields.ConsentObtained:
            case RequisitionFields.UnmappedNotes:
                // Both are valid data whatever they say. An unticked consent box and a scrawled
                // note are correctly extracted facts, not extraction failures - they drive the
                // review decision, not the field's own state.
                Mark(field, FieldValidationState.Valid, null);
                return;

            default:
                if (present)
                    Mark(field, FieldValidationState.Valid, null);
                else if (RequisitionFields.Required.Contains(field.Name))
                    Mark(field, FieldValidationState.Invalid, $"{RequisitionFields.Label(field.Name)} is required and was not found.");
                else
                    Mark(field, FieldValidationState.Unverifiable, "Not found on the form.");
                return;
        }
    }

    private static void Set(ExtractedField field, ValidationResult result) =>
        Mark(field,
            result.IsValid ? FieldValidationState.Valid : FieldValidationState.Invalid,
            result.IsValid ? null : result.Message);

    private static void Mark(ExtractedField field, FieldValidationState state, string? message)
    {
        field.ValidationState = state;
        field.ValidationMessage = message;
    }

    private static bool? ConsentObtained(IReadOnlyDictionary<string, ExtractedField> byName) =>
        byName.TryGetValue(RequisitionFields.ConsentObtained, out var f) && bool.TryParse(f.Value, out var v)
            ? v
            : null;
}
