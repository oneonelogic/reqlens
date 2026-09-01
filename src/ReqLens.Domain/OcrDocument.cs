namespace ReqLens.Domain;

/// <summary>One OCR text line, with where on the page it sat and how sure Textract was.</summary>
public sealed record OcrLine(string Text, float Confidence, float Top, float Left, int Page);

/// <summary>
/// A form key/value pair Textract resolved on its own. Useful but not sufficient: clinics label
/// the same field differently ("NPI", "Provider NPI", "National Provider ID"), which is exactly
/// the normalisation the model is there to do.
/// </summary>
public sealed record OcrKeyValue(string Key, string? Value, float Confidence, int Page);

/// <summary>
/// What the Ingest Lambda parks in S3 under ocr/ and the Extract Lambda picks up. A trimmed
/// projection of the Textract response rather than the raw blocks: the raw form is an order of
/// magnitude larger, and nothing downstream reads the parts that were dropped.
/// </summary>
public sealed record OcrDocument
{
    /// <summary>S3 key of the PDF this came from, under scans/.</summary>
    public required string SourceObjectKey { get; init; }

    public required string TenantSlug { get; init; }

    /// <summary>The order shell Ingest created. Extract fills it in rather than creating its own.</summary>
    public required Guid OrderId { get; init; }

    public required int PageCount { get; init; }
    public required IReadOnlyList<OcrLine> Lines { get; init; }
    public required IReadOnlyList<OcrKeyValue> KeyValues { get; init; }

    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Mean Textract confidence over the page. This is an OCR measurement, not the model's
    /// opinion of itself, so it is the more honest of the two confidence signals the pipeline
    /// carries - it is recorded in telemetry beside the model's self-reported number.
    /// </summary>
    public double MeanOcrConfidence => Lines.Count == 0 ? 0 : Lines.Average(l => l.Confidence) / 100.0;

    /// <summary>Reading-order plain text. This, and the key/value pairs, is all the model sees.</summary>
    public string PlainText => string.Join("\n", Lines.Select(l => l.Text));
}
