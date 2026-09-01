namespace ReqLens.Domain;

/// <summary>
/// The SQS message Ingest sends and Extract consumes.
/// </summary>
/// <remarks>
/// Carries pointers, never content. The OCR text can run to several kilobytes and SQS caps a
/// message at 256 KB, but the real reason is that a queue is not a place to keep transcribed
/// patient data - it has its own retention, its own encryption story and its own console.
/// The text stays in the bucket that is already encrypted and access-scoped for it.
/// </remarks>
public sealed record ExtractionRequest
{
    public required Guid OrderId { get; init; }
    public required Guid TenantId { get; init; }
    public required string TenantSlug { get; init; }
    public required string Bucket { get; init; }

    /// <summary>The original PDF, under scans/.</summary>
    public required string SourceObjectKey { get; init; }

    /// <summary>The OCR projection Ingest parked, under ocr/.</summary>
    public required string OcrObjectKey { get; init; }
}
