using System.Text.Json;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using ReqLens.Ai;
using ReqLens.Data;
using ReqLens.Domain;
using ReqLens.Ocr;
using ReqLens.Validation;

namespace ReqLens.Pipeline;

/// <summary>
/// Runs one document through the model chain, re-checks everything it said, and writes the order.
/// </summary>
public sealed class ExtractStep(
    IAmazonS3 s3,
    IRequisitionExtractor extractor,
    RequisitionValidator validator,
    IPipelineLog log)
{
    public async Task<LabOrder> RunAsync(
        ReqLensDbContext db,
        ExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        var ocr = await LoadOcrAsync(request, cancellationToken);

        // Tenant-scoped by key, not by filter-then-check. The order's identity in this system is
        // (TenantId, Id), and querying by anything less is how cross-tenant reads happen.
        var order = await db.Orders
            .Include(o => o.Fields)
            .FirstOrDefaultAsync(o => o.TenantId == request.TenantId && o.Id == request.OrderId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Order {request.OrderId} does not exist for tenant {request.TenantId}.");

        var catalog = await db.TestCatalog
            .Where(c => c.TenantId == request.TenantId)
            .ToListAsync(cancellationToken);

        var outcome = await extractor.ExtractAsync(
            request.SourceObjectKey, OcrPrompt.For(ocr), cancellationToken);

        foreach (var call in outcome.Calls)
            await log.RecordAsync(call, cancellationToken);

        // A redelivery re-extracts, so the previous attempt's fields are replaced rather than
        // duplicated. Review actions are never touched - they are the audit trail.
        db.Fields.RemoveRange(order.Fields);

        foreach (var call in outcome.Calls)
        {
            db.ExtractionCalls.Add(new ExtractionCall
            {
                OrderId = order.Id,
                TenantId = order.TenantId,
                ModelId = call.ModelId,
                Role = call.Role.ToString(),
                Attempt = call.Attempt,
                LatencyMs = call.LatencyMs,
                InputTokens = call.InputTokens,
                OutputTokens = call.OutputTokens,
                EstimatedCostUsd = call.EstimatedCostUsd,
                GuardrailIntervened = call.GuardrailIntervened,
                SchemaValid = call.SchemaValid,
                MinFieldConfidence = call.MinFieldConfidence,
                FailureReason = call.FailureReason,
                At = call.At
            });
        }

        order.ModelId = outcome.Telemetry.ModelId;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        if (!outcome.Succeeded)
        {
            // Nothing to validate. The document still goes to a human - a guardrail block and an
            // exhausted chain both end in the same queue a low-confidence field ends in.
            order.Status = OrderStatus.NeedsReview;
            order.OverallConfidence = null;
            order.ExtractionFailure = outcome.FailureReason;
            order.ReviewReasons = ["Extraction produced no fields; the scan needs to be read by hand."];

            await db.SaveChangesAsync(cancellationToken);

            log.Warn($"{Path.GetFileName(request.SourceObjectKey)}: no extraction. {outcome.FailureReason}");
            return order;
        }

        var fields = outcome.Fields.ToList();

        foreach (var field in fields)
        {
            field.OrderId = order.Id;
            field.TenantId = order.TenantId;
        }

        var assessment = validator.Assess(fields, catalog);

        db.Fields.AddRange(fields);

        order.OverallConfidence = assessment.MinConfidence;
        order.ReviewReasons = [.. assessment.ReviewReasons];
        order.ExtractionFailure = null;
        order.Status = assessment.NeedsReview ? OrderStatus.NeedsReview : OrderStatus.Accepted;

        await db.SaveChangesAsync(cancellationToken);

        log.Info(
            $"{Path.GetFileName(request.SourceObjectKey)} -> {order.Status}. "
            + $"Lowest confidence {assessment.MinConfidence:P0}, {assessment.ReviewReasons.Count} reason(s), "
            + $"{outcome.Calls.Count} model call(s).");

        return order;
    }

    private async Task<OcrDocument> LoadOcrAsync(ExtractionRequest request, CancellationToken cancellationToken)
    {
        using var response = await s3.GetObjectAsync(request.Bucket, request.OcrObjectKey, cancellationToken);
        await using var stream = response.ResponseStream;

        return await JsonSerializer.DeserializeAsync<OcrDocument>(stream, cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException(
                   $"{request.OcrObjectKey} did not deserialise to an OcrDocument.");
    }
}
