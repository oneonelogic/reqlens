namespace ReqLens.Domain;

/// <summary>
/// One OCR text line, with where on the page it sat and how sure the reader was.
/// </summary>
/// <remarks>
/// <see cref="Confidence"/> is null when the provider does not measure one. Only a real OCR
/// engine scores its own reading; a PDF text layer is read exactly or not at all, and a vision
/// model's opinion of its transcription is a model self-report, which is the other confidence
/// signal this pipeline already carries and deliberately keeps separate. Null says "not
/// measured", which is the truth. A stand-in number here would flow into the same field as a
/// Textract score and quietly corrupt it.
///
/// <see cref="Top"/> and <see cref="Left"/> are null on the same principle, for a provider that
/// reports no geometry. They are a normalised 0..1 position from the top left when present, so
/// putting anything else in them - an ordinal, a zero - would be a different unit wearing the
/// same name. Reading order does not depend on them: it is the order of
/// <see cref="OcrDocument.Lines"/>, which is what <see cref="OcrDocument.PlainText"/> uses.
/// </remarks>
public sealed record OcrLine(string Text, float? Confidence, float? Top, float? Left, int Page);

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
    /// Mean OCR confidence over the page, or null when the provider measured none.
    /// </summary>
    /// <remarks>
    /// When it has a value this is an OCR measurement, not the model's opinion of itself, which
    /// is what makes it the more honest of the two confidence signals the pipeline carries - it
    /// is recorded in telemetry beside the model's self-reported number. Null is therefore
    /// meaningful and must be rendered as "not measured" rather than coerced to a number: a
    /// provider that cannot score its own reading has to say so, not report a confident zero or
    /// a flattering one hundred.
    /// </remarks>
    public double? MeanOcrConfidence
    {
        get
        {
            var measured = Lines.Where(l => l.Confidence is not null).ToList();
            return measured.Count == 0 ? null : measured.Average(l => l.Confidence!.Value) / 100.0;
        }
    }

    /// <summary>Reading-order plain text. This, and the key/value pairs, is all the model sees.</summary>
    public string PlainText => string.Join("\n", Lines.Select(l => l.Text));
}
