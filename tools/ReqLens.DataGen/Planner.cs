namespace ReqLens.DataGen;

/// <summary>
/// Decides what the twenty documents contain. Roughly half are clean so the happy path is real;
/// the rest carry one defect each so every branch of the review gate has something to catch.
/// </summary>
public static class Planner
{
    public static List<Requisition> Plan(DataSet data, int count, int seed)
    {
        var rnd = new Random(seed);
        var active = data.Catalog.Where(c => c.Active).ToList();
        var inactive = data.Catalog.Where(c => !c.Active).ToList();

        // One defect per document, spread deterministically. The rest are clean.
        var defects = new List<Defect>
        {
            Defect.MissingConsent,
            Defect.AmbiguousPanel,
            Defect.MissingDiagnosis,
            Defect.InvalidNpi,
            Defect.UnknownPanelCode,
            Defect.InactivePanel,
            Defect.HandwrittenNote,
            Defect.SpecimenMismatch,
            Defect.MissingConsent
        };
        while (defects.Count < count) defects.Add(Defect.None);

        var result = new List<Requisition>();
        for (var i = 0; i < count; i++)
        {
            var defect = defects[i];
            var tenant = data.Tenants[i % data.Tenants.Count];
            var provider = data.Providers.Where(p => p.TenantSlug == tenant.Slug).ElementAt(i % 3 % 3);
            var patient = data.Patients[i % data.Patients.Count];

            var panel = defect.HasFlag(Defect.InactivePanel)
                ? inactive[i % inactive.Count]
                : active[i % active.Count];

            var diagnosis = defect.HasFlag(Defect.MissingDiagnosis)
                ? null
                : data.IcdCodes[rnd.Next(data.IcdCodes.Count)];

            var printedNpi = defect.HasFlag(Defect.InvalidNpi)
                ? data.InvalidNpis[i % data.InvalidNpis.Count]
                : provider.Npi;

            var printedPanel = defect switch
            {
                _ when defect.HasFlag(Defect.AmbiguousPanel)   => "BRCA panel",
                _ when defect.HasFlag(Defect.UnknownPanelCode) => "GXP-999  Comprehensive Genome Profile",
                _ => $"{panel.Code}  {panel.Name}"
            };

            var specimen = defect.HasFlag(Defect.SpecimenMismatch)
                ? "Buccal Swab"          // wrong collection medium for a blood-only panel
                : panel.SpecimenType;

            var collected = new DateTime(2026, 8, 1).AddDays(rnd.Next(0, 26));

            result.Add(new Requisition
            {
                Id = $"req-{i + 1:D3}",
                Tenant = tenant,
                Provider = provider,
                Patient = patient,
                Panel = panel,
                Diagnosis = diagnosis,
                CollectionDate = collected.ToString("MM/dd/yyyy"),
                SpecimenType = specimen,
                ConsentObtained = !defect.HasFlag(Defect.MissingConsent),
                Layout = (Layout)(i % 3),
                Defects = defect,
                PrintedNpi = printedNpi,
                PrintedPanel = printedPanel
            });
        }
        return result;
    }
}
