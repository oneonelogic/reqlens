namespace ReqLens.DataGen;

/// <summary>
/// Everything loaded off disk, so the generated documents stay reproducible from the CSVs.
/// Hand-parsed on purpose: these files are flat, unquoted and ours, and a mapping library
/// bought nothing but reflection failures.
/// </summary>
public sealed class DataSet
{
    public required IReadOnlyList<Tenant> Tenants { get; init; }
    public required IReadOnlyList<Provider> Providers { get; init; }
    public required IReadOnlyList<Patient> Patients { get; init; }
    public required IReadOnlyList<CatalogEntry> Catalog { get; init; }
    public required IReadOnlyList<IcdCode> IcdCodes { get; init; }
    public required IReadOnlyList<string> InvalidNpis { get; init; }

    public static DataSet Load(string dir)
    {
        return new DataSet
        {
            Tenants = Rows(dir, "tenants.csv")
                .Select(f => new Tenant { Slug = f[0], Name = f[1] }).ToList(),

            Providers = Rows(dir, "providers.csv")
                .Select(f => new Provider
                {
                    Npi = f[0], LastName = f[1], FirstName = f[2],
                    Credential = f[3], TenantSlug = f[4]
                }).ToList(),

            Patients = Rows(dir, "patients.csv")
                .Select(f => new Patient
                {
                    Mrn = f[0], LastName = f[1], FirstName = f[2], Dob = f[3], Sex = f[4]
                }).ToList(),

            Catalog = Rows(dir, "test-catalog.csv")
                .Select(f => new CatalogEntry
                {
                    Code = f[0], Name = f[1], SpecimenType = f[2],
                    Active = bool.Parse(f[3])
                }).ToList(),

            IcdCodes = Rows(dir, "icd10-codes.csv")
                .Select(f => new IcdCode { Code = f[0], Description = f[1] }).ToList(),

            InvalidNpis = Rows(dir, "invalid-npis.csv").Select(f => f[0]).ToList()
        };
    }

    /// <summary>Data rows (header skipped) split on commas. No quoting in these files, by design.</summary>
    private static IEnumerable<string[]> Rows(string dir, string file) =>
        File.ReadLines(Path.Combine(dir, file))
            .Skip(1)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split(',').Select(s => s.Trim()).ToArray());
}
