using System.Text.Json;

namespace ReqLens.Cli;

/// <summary>
/// Where things are on disk, and what the deployed stack is called.
/// </summary>
/// <remarks>
/// Stack values come from environment variables first, and fall back to reading Terraform's
/// state file directly. Reading the state beats shelling out to `terraform output`: it needs no
/// Terraform on PATH, no initialised working directory, and it cannot hang waiting for a lock.
/// Nothing is ever written back to it.
/// </remarks>
public static class Workspace
{
    public static string Root { get; } = FindRoot();

    public static string Synthetic => Path.Combine(Root, "data", "synthetic");
    public static string Requisitions => Path.Combine(Synthetic, "requisitions");
    public static string Golden => Path.Combine(Synthetic, "golden");
    public static string Artifacts => Path.Combine(Root, "artifacts");

    private static readonly Lazy<Dictionary<string, JsonElement>> TerraformOutputs = new(LoadOutputs);

    /// <summary>Environment variable first, then the Terraform output of the same purpose.</summary>
    public static string? Setting(string environmentVariable, string terraformOutput)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;

        if (!TerraformOutputs.Value.TryGetValue(terraformOutput, out var value)) return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    public static string Require(string environmentVariable, string terraformOutput)
        => Setting(environmentVariable, terraformOutput)
           ?? throw new InvalidOperationException(
               $"{environmentVariable} is not set and Terraform output '{terraformOutput}' was not found. "
               + "Either export it, or apply the stack in infra/terraform.");

    /// <summary>
    /// Fills in the settings the shared libraries read from the environment, from Terraform
    /// state, without printing any of them.
    /// </summary>
    /// <remarks>
    /// The database password is never one of these: DB_SECRET_ARN names the secret, and the
    /// value is fetched inside the process by the AWS SDK. Nothing here, and no shell command
    /// anyone has to type, ever holds the credential itself.
    /// </remarks>
    public static void ApplyEnvironment()
    {
        Fill("DB_SECRET_ARN", "db_secret_arn");
        Fill("REQUISITIONS_BUCKET", "bucket_name");
        Fill("GUARDRAIL_ID", "guardrail_id");
        Fill("GUARDRAIL_VERSION", "guardrail_version");
        Fill("AWS_REGION", "region");

        static void Fill(string variable, string output)
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))) return;

            if (Setting(variable, output) is { } value)
                Environment.SetEnvironmentVariable(variable, value);
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ReqLens.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static Dictionary<string, JsonElement> LoadOutputs()
    {
        var state = Path.Combine(Root, "infra", "terraform", "terraform.tfstate");
        if (!File.Exists(state)) return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(state));

            if (!document.RootElement.TryGetProperty("outputs", out var outputs)) return [];

            return outputs.EnumerateObject().ToDictionary(
                p => p.Name,
                p => p.Value.TryGetProperty("value", out var v) ? v.Clone() : default);
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>Minimal CSV reader for the synthetic reference files, which have no quoted fields.</summary>
public static class Csv
{
    public static IEnumerable<string[]> Rows(string path, int expectedColumns)
    {
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            if (line.Length == 0) continue;

            var parts = line.Split(',');

            if (parts.Length == expectedColumns)
            {
                yield return parts;
                continue;
            }

            // A value contained a comma. Everything between the first column and the trailing
            // fixed ones belongs to the second column.
            var merged = new string[expectedColumns];
            merged[0] = parts[0];
            merged[1] = string.Join(',', parts[1..(parts.Length - expectedColumns + 2)]);

            for (var i = 2; i < expectedColumns; i++)
                merged[i] = parts[parts.Length - expectedColumns + i];

            yield return merged;
        }
    }
}
