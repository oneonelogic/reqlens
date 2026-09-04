using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Transfer;

namespace ReqLens.Cli;

/// <summary>
/// Uploads a synthetic requisition into the bucket, which is what starts the pipeline.
/// </summary>
/// <remarks>
/// The key carries the clinic: scans/&lt;tenant-slug&gt;/&lt;file&gt;.pdf. Where a document is
/// part of the golden set, the clinic is read from its golden record rather than asked for, so
/// `ingest --all` files all twenty documents to the clinics they were generated for. Getting that
/// wrong would put a form in front of a lab that never ordered it.
/// </remarks>
public static class IngestCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var bucket = Options.Value(args, "--bucket") ?? Workspace.Require("REQUISITIONS_BUCKET", "bucket_name");
        var uploads = Plan(args);

        using var s3 = new AmazonS3Client();

        foreach (var upload in uploads)
        {
            await UploadAsync(s3, bucket, upload);
            Console.WriteLine($"s3://{bucket}/{upload.Key}");
        }

        Console.WriteLine();
        Console.WriteLine($"{uploads.Count} document(s) uploaded. Ingest runs on the object-created event;");
        Console.WriteLine("watch it with:  aws logs tail /aws/lambda/reqlens-ingest --follow --region us-east-2");

        return 0;
    }

    public static async Task UploadAsync(IAmazonS3 s3, string bucket, Upload upload) =>
        await new TransferUtility(s3).UploadAsync(upload.Path, bucket, upload.Key);

    /// <summary>Which files go to which clinic, resolved before anything is uploaded.</summary>
    public static List<Upload> Plan(string[] args)
    {
        var tenantOverride = Options.Value(args, "--tenant");

        var paths = args.Contains("--all")
            ? Directory.EnumerateFiles(Workspace.Requisitions, "*.pdf").Order().ToList()
            : args.Where(a => a.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();

        if (paths.Count == 0)
            throw new InvalidOperationException("Nothing to do. Pass one or more .pdf paths, or --all.");

        return paths.Select(path =>
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"No such file: {path}", path);

            var document = Path.GetFileNameWithoutExtension(path);

            var slug = tenantOverride ?? TenantFromGolden(document)
                ?? throw new InvalidOperationException(
                    $"{document} is not in the golden set, so its clinic is unknown. Pass --tenant <slug>.");

            return new Upload(path, slug, $"scans/{slug}/{Path.GetFileName(path)}");
        }).ToList();
    }

    private static string? TenantFromGolden(string document)
    {
        var path = Path.Combine(Workspace.Golden, document + ".json");
        if (!File.Exists(path)) return null;

        using var json = JsonDocument.Parse(File.ReadAllText(path));

        return json.RootElement.TryGetProperty("tenant", out var tenant) ? tenant.GetString() : null;
    }
}

public sealed record Upload(string Path, string TenantSlug, string Key);

public static class Options
{
    public static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    public static int? Int(string[] args, string name)
        => int.TryParse(Value(args, name), out var value) ? value : null;
}
