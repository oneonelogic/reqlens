using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using ReqLens.Data;
using ReqLens.Domain;
using ReqLens.Ocr;

namespace ReqLens.Pipeline;

/// <summary>
/// A scanned requisition arrives: read it, park the OCR, open an order.
/// </summary>
/// <remarks>
/// The tenant comes from the key - scans/&lt;tenant-slug&gt;/&lt;file&gt;.pdf. Each partner clinic
/// uploads under its own prefix, which is the thing a per-clinic IAM policy or a presigned URL
/// can actually be scoped to. Reading the clinic name off the form instead would let the
/// document decide which tenant owns it, and a form is untrusted input.
///
/// This class is the step, not the trigger. The Lambda that runs it on an S3 event and the CLI
/// that runs it against a local file are both adapters, which is what makes the pipeline
/// testable without constructing an S3Event.
/// </remarks>
public sealed class IngestStep(IAmazonS3 s3, IOcrProvider ocr, IPipelineLog log)
{
    public const string ScanPrefix = "scans/";

    public async Task<ExtractionRequest> RunAsync(
        ReqLensDbContext db,
        string bucket,
        string scanKey,
        CancellationToken cancellationToken = default)
    {
        if (!scanKey.StartsWith(ScanPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"'{scanKey}' is not under {ScanPrefix}; the bucket notification is misconfigured.");

        var slug = TenantSlugFrom(scanKey);

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No tenant with slug '{slug}'. Upload to {ScanPrefix}<tenant-slug>/<file>.pdf, or seed the tenant first.");

        // S3 delivers a notification at least once, and a redelivery must not produce a second
        // order for the same document. The source key is the natural identity of a scan.
        var order = await db.Orders.FirstOrDefaultAsync(
            o => o.TenantId == tenant.Id && o.SourceObjectKey == scanKey, cancellationToken);

        if (order is null)
        {
            order = new LabOrder
            {
                TenantId = tenant.Id,
                SourceObjectKey = scanKey,
                Status = OrderStatus.Received
            };

            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            log.Info($"Order {order.Id} already exists for {scanKey}; re-reading into the same shell.");
        }

        var pdf = await DownloadAsync(bucket, scanKey, cancellationToken);
        var document = await ocr.ReadAsync(pdf, scanKey, slug, order.Id, cancellationToken);

        var ocrKey = OcrKeyFor(scanKey);

        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = ocrKey,
            ContentType = "application/json",
            ContentBody = JsonSerializer.Serialize(document)
        }, cancellationToken);

        order.Status = OrderStatus.Extracting;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // "not measured" rather than a number, because only a real OCR engine scores its own
        // reading. Printing 0% or 100% for a provider that measured nothing would read as a
        // result rather than an absence.
        var confidence = document.MeanOcrConfidence is { } mean ? mean.ToString("P1") : "not measured";

        log.Info(
            $"{Path.GetFileName(scanKey)} ({slug}): {document.Lines.Count} lines, "
            + $"{document.KeyValues.Count} key/value pairs, mean OCR confidence "
            + $"{confidence} via {ocr.Name}.");

        return new ExtractionRequest
        {
            OrderId = order.Id,
            TenantId = tenant.Id,
            TenantSlug = slug,
            Bucket = bucket,
            SourceObjectKey = scanKey,
            OcrObjectKey = ocrKey
        };
    }

    /// <summary>scans/northgate/req-001.pdf -> northgate. A key with no clinic segment is rejected.</summary>
    public static string TenantSlugFrom(string scanKey)
    {
        var parts = scanKey.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 3
            ? parts[1]
            : throw new InvalidOperationException(
                $"'{scanKey}' does not name a tenant. Expected {ScanPrefix}<tenant-slug>/<file>.pdf.");
    }

    /// <summary>scans/northgate/req-001.pdf -> ocr/northgate/req-001.json</summary>
    public static string OcrKeyFor(string scanKey) =>
        "ocr/" + Path.ChangeExtension(scanKey[ScanPrefix.Length..], ".json");

    private async Task<byte[]> DownloadAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        using var response = await s3.GetObjectAsync(bucket, key, cancellationToken);
        await using var stream = response.ResponseStream;

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }
}
