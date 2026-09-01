namespace ReqLens.Domain;

/// <summary>
/// The canonical field names, in one place. The extraction schema, the validators, the eval
/// harness and the console all key off these, so a rename cannot drift between the model's
/// output and the code that grades it.
/// </summary>
public static class RequisitionFields
{
    public const string ProviderName = "ordering_provider_name";
    public const string ProviderNpi = "ordering_provider_npi";
    public const string PatientLastName = "patient_last_name";
    public const string PatientFirstName = "patient_first_name";
    public const string PatientDob = "patient_dob";
    public const string PatientSex = "patient_sex";
    public const string PatientMrn = "patient_mrn";
    public const string TestPanelCode = "test_panel_code";
    public const string DiagnosisCode = "diagnosis_code";
    public const string SpecimenType = "specimen_type";
    public const string CollectionDate = "collection_date";
    public const string ConsentObtained = "consent_obtained";

    /// <summary>
    /// Free text on the form that the schema has nowhere to put - a margin note, a scrawled
    /// instruction. Not graded by the golden set, because there is no correct structured value
    /// for it; its whole job is to be non-empty, which forces the order into review rather than
    /// letting the pipeline silently drop something a human wrote on the page.
    /// </summary>
    public const string UnmappedNotes = "unmapped_notes";

    /// <summary>The twelve fields the golden set grades, in the order a form presents them.</summary>
    public static readonly IReadOnlyList<string> Graded =
    [
        ProviderName, ProviderNpi,
        PatientLastName, PatientFirstName, PatientDob, PatientSex, PatientMrn,
        TestPanelCode, DiagnosisCode, SpecimenType, CollectionDate, ConsentObtained
    ];

    public static readonly IReadOnlyList<string> All = [.. Graded, UnmappedNotes];

    /// <summary>Fields a requisition is invalid without. Absence is a failure, not a blank.</summary>
    public static readonly IReadOnlySet<string> Required = new HashSet<string>
    {
        ProviderName, ProviderNpi,
        PatientLastName, PatientFirstName, PatientDob, PatientMrn,
        SpecimenType, CollectionDate
    };

    /// <summary>Human label for the console. Falls back to the raw name for anything unlisted.</summary>
    public static string Label(string field) => field switch
    {
        ProviderName => "Ordering provider",
        ProviderNpi => "NPI",
        PatientLastName => "Patient last name",
        PatientFirstName => "Patient first name",
        PatientDob => "Date of birth",
        PatientSex => "Sex",
        PatientMrn => "MRN",
        TestPanelCode => "Test panel code",
        DiagnosisCode => "Diagnosis (ICD-10)",
        SpecimenType => "Specimen",
        CollectionDate => "Collection date",
        ConsentObtained => "Consent obtained",
        UnmappedNotes => "Unmapped note",
        _ => field
    };
}
