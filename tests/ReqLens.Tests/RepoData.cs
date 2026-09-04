namespace ReqLens.Tests;

/// <summary>
/// Finds the synthetic data in the working tree.
/// </summary>
/// <remarks>
/// Walks up from the test binary rather than copying the data into the output directory. The
/// golden set is the contract these tests grade against, and a copy taken at build time is a
/// copy that can silently go stale against a regenerated set.
/// </remarks>
public static class RepoData
{
    public static string Root { get; } = Find();

    public static string Synthetic => Path.Combine(Root, "data", "synthetic");
    public static string Golden => Path.Combine(Synthetic, "golden");

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "synthetic", "golden")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate data/synthetic above the test binary.");
    }

    /// <summary>The shared test catalogue, as the seeder loads it for every tenant.</summary>
    public static List<Domain.TestCatalogEntry> Catalog(Guid tenantId = default)
    {
        var rows = File.ReadAllLines(Path.Combine(Synthetic, "test-catalog.csv")).Skip(1);

        return rows.Where(l => l.Length > 0).Select(line =>
        {
            var parts = SplitCsv(line);
            return new Domain.TestCatalogEntry
            {
                TenantId = tenantId,
                Code = parts[0],
                Name = parts[1],
                SpecimenType = parts[2],
                Active = bool.Parse(parts[3])
            };
        }).ToList();
    }

    /// <summary>Minimal CSV split - the synthetic files have no quoted commas, and are asserted on here.</summary>
    private static string[] SplitCsv(string line)
    {
        var parts = line.Split(',');
        if (parts.Length == 4) return parts;

        // A panel name contains a comma ("Hereditary Cancer Panel (47 genes)" does not, but a
        // future one might). Re-join everything between the first and last two fields.
        return
        [
            parts[0],
            string.Join(',', parts[1..^2]),
            parts[^2],
            parts[^1]
        ];
    }
}
