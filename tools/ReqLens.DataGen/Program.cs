using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ReqLens.DataGen;

// Generates the synthetic requisition set and the golden expectations that go with it.
//   dotnet run --project tools/ReqLens.DataGen -- [--count 20] [--seed 20260828] [--out data/synthetic]

QuestPDF.Settings.License = LicenseType.Community;

var count = ArgInt("--count", 20);
var seed = ArgInt("--seed", 20260828);
var outDir = ArgStr("--out", null) ?? Path.Combine("data", "synthetic");

var pdfDir = Path.Combine(outDir, "requisitions");
var goldDir = Path.Combine(outDir, "golden");
Directory.CreateDirectory(pdfDir);
Directory.CreateDirectory(goldDir);

var data = DataSet.Load(outDir);
var plan = Planner.Plan(data, count, seed);

var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

foreach (var r in plan)
{
    // A catalogue row resolves only when the form names a code AND that code is in the catalogue.
    var rowResolves = r.PrintedPanelCode is not null && !r.Defects.HasFlag(Defect.UnknownPanelCode);

    new RequisitionDocument(r).GeneratePdf(Path.Combine(pdfDir, $"{r.Id}.pdf"));

    // The golden record is what a correct extraction looks like. Nulls are genuinely absent
    // on the form, which is different from "the model failed to find it" - the eval harness
    // needs to be able to tell those apart.
    var expected = new
    {
        document = $"{r.Id}.pdf",
        tenant = r.Tenant.Slug,
        layout = r.Layout.ToString(),
        defects = r.Defects == Defect.None ? Array.Empty<string>() : r.Defects.ToString().Split(", "),
        fields = new Dictionary<string, object?>
        {
            ["ordering_provider_name"] = r.Provider.Display,
            ["ordering_provider_npi"]  = r.PrintedNpi,
            ["patient_last_name"]      = r.Patient.LastName,
            ["patient_first_name"]     = r.Patient.FirstName,
            ["patient_dob"]            = r.Patient.Dob,
            ["patient_sex"]            = r.Patient.Sex,
            ["patient_mrn"]            = r.Patient.Mrn,
            ["test_panel_code"]        = r.PrintedPanelCode,
            ["diagnosis_code"]         = r.Diagnosis?.Code,
            ["specimen_type"]          = r.SpecimenType,
            ["collection_date"]        = r.CollectionDate,
            ["consent_obtained"]       = r.ConsentObtained
        },
        // What the deterministic layer should conclude, independent of the model.
        //
        // Everything that depends on a catalogue row is derived from ONE decision - whether a row
        // resolves at all - rather than each check reaching for the panel the generator happened
        // to pick. Deriving them independently leaked that hidden panel three separate times.
        expected_validation = new
        {
            npi_valid              = !r.Defects.HasFlag(Defect.InvalidNpi),

            panel_code_present     = r.PrintedPanelCode is not null,
            // Null, not false: with no code on the form there is nothing to look up, and
            // "unanswerable" is a different outcome from "looked it up and it was absent".
            panel_in_catalog       = r.PrintedPanelCode is null
                ? null
                : (bool?)!r.Defects.HasFlag(Defect.UnknownPanelCode),

            // The three below are properties OF a catalogue row. No row, nothing to ask.
            panel_active           = rowResolves ? (bool?)!r.Defects.HasFlag(Defect.InactivePanel) : null,
            specimen_matches       = rowResolves ? (bool?)!r.Defects.HasFlag(Defect.SpecimenMismatch) : null,

            diagnosis_code_present = r.Diagnosis is not null,
            // Every diagnosis drawn for these documents comes from the committed ICD-10 list, so a
            // present code is always a real one. Absent means there is nothing to validate.
            icd10_valid            = r.Diagnosis is null ? null : (bool?)true,

            should_need_review     = r.Defects != Defect.None,

            // Why review is expected. Some documents fail no deterministic check at all - an
            // unticked consent box and a margin note the schema cannot hold are both perfectly
            // valid data - so without this an eval harness sees every check pass next to
            // should_need_review: true and cannot tell which path it was supposed to exercise.
            review_reason          = r.Defects switch
            {
                Defect.None             => null,
                Defect.MissingConsent   => "consent checkbox not ticked",
                Defect.AmbiguousPanel   => "panel named in prose, no code to resolve",
                Defect.MissingDiagnosis => "no diagnosis code on the form",
                Defect.InvalidNpi       => "NPI check digit fails",
                Defect.UnknownPanelCode => "panel code not in the catalogue",
                Defect.InactivePanel    => "panel is retired",
                Defect.HandwrittenNote  => "free-text note the schema has nowhere to put",
                Defect.SpecimenMismatch => "specimen type wrong for the panel",
                _                       => r.Defects.ToString()
            }
        }
    };

    File.WriteAllText(Path.Combine(goldDir, $"{r.Id}.json"), JsonSerializer.Serialize(expected, jsonOpts));
}

var clean = plan.Count(p => p.Defects == Defect.None);
Console.WriteLine($"{plan.Count} requisitions -> {pdfDir}");
Console.WriteLine($"{plan.Count} golden records -> {goldDir}");
Console.WriteLine($"  {clean} clean, {plan.Count - clean} carrying a defect");
foreach (var g in plan.Where(p => p.Defects != Defect.None).GroupBy(p => p.Defects))
    Console.WriteLine($"    {g.Key,-18} {string.Join(", ", g.Select(x => x.Id))}");

int ArgInt(string name, int fallback)
    => int.TryParse(ArgStr(name, null), out var v) ? v : fallback;

string? ArgStr(string name, string? fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}
