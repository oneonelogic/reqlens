using System.Reflection;

namespace ReqLens.Validation;

/// <summary>
/// The ICD-10-CM codes this deployment recognises.
/// </summary>
/// <remarks>
/// Embedded in the assembly rather than read from the database. The real system would carry the
/// full ~70,000-code set in a reference table on a release cycle of its own; this demo carries
/// the genetic-testing subset the synthetic data draws from, and embedding it means the Extract
/// Lambda needs no database round trip to answer "is that a real code".
/// </remarks>
public static class Icd10CodeSet
{
    private const string ResourceName = "ReqLens.Validation.icd10-codes.csv";

    private static readonly Lazy<IReadOnlySet<string>> Loaded = new(Load);

    public static IReadOnlySet<string> Default => Loaded.Value;

    private static IReadOnlySet<string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");

        using var reader = new StreamReader(stream);

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _ = reader.ReadLine(); // header: code,description

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            var comma = line.IndexOf(',');
            var code = comma < 0 ? line : line[..comma];

            if (code.Length > 0) codes.Add(code.Trim());
        }

        return codes;
    }
}
