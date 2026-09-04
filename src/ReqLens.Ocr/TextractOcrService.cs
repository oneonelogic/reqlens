using Amazon.Textract;
using Amazon.Textract.Model;
using ReqLens.Domain;

namespace ReqLens.Ocr;

/// <summary>
/// Turns a scanned requisition into the trimmed OCR projection the rest of the pipeline reads.
/// </summary>
/// <remarks>
/// Shared by the Ingest Lambda and the eval harness on purpose. If the harness parsed Textract
/// output its own way, its scores would describe a pipeline that does not exist - and the whole
/// value of a golden set is that it grades the thing actually running.
///
/// Synchronous AnalyzeDocument only, which caps input at one page. Requisitions are one page;
/// a multi-page intake would need StartDocumentAnalysis and a completion notification, which is
/// a different shape of Lambda and is deliberately out of scope here.
/// </remarks>
public sealed class TextractOcrService(IAmazonTextract textract) : IOcrProvider
{
    public string Name => OcrProviders.TextractName;

    /// <summary>
    /// Takes bytes rather than an S3 pointer.
    /// </summary>
    /// <remarks>
    /// Textract can read an object out of the bucket itself, which saves the Lambda a download.
    /// Bytes are used anyway so that every OCR provider has one signature, and so the eval
    /// harness on a laptop and the Lambda in the VPC exercise the identical call. A requisition
    /// is tens of kilobytes; the download it costs is not worth two code paths.
    /// </remarks>
    public async Task<OcrDocument> ReadAsync(
        byte[] document,
        string sourceObjectKey,
        string tenantSlug,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var sourceKey = sourceObjectKey;
        using var bytes = new MemoryStream(document);

        // FORMS only. TABLES would cost more per page and add nothing here: the gridded layout's
        // cells come back as ordinary LINE blocks regardless, and the model reads text, not grids.
        var response = await textract.AnalyzeDocumentAsync(new AnalyzeDocumentRequest
        {
            Document = new Document { Bytes = bytes },
            FeatureTypes = [FeatureType.FORMS]
        }, ct);

        var blocks = response.Blocks.ToDictionary(b => b.Id);

        // Textract returns blocks in reading order, including for multi-column layouts, so they
        // are deliberately not re-sorted. Sorting by vertical position would interleave the two
        // columns of the TwoColumn layout into nonsense.
        var lines = response.Blocks
            .Where(b => b.BlockType == BlockType.LINE)
            .Select(b => new OcrLine(
                b.Text ?? string.Empty,
                (float?)b.Confidence,
                (float)(b.Geometry?.BoundingBox?.Top ?? 0),
                (float)(b.Geometry?.BoundingBox?.Left ?? 0),
                b.Page ?? 1))
            .Where(l => l.Text.Length > 0)
            .ToList();

        var keyValues = response.Blocks
            .Where(b => b.BlockType == BlockType.KEY_VALUE_SET && b.EntityTypes?.Contains("KEY") == true)
            .Select(k => new OcrKeyValue(
                TextOf(k, blocks),
                ValueOf(k, blocks),
                (float)(k.Confidence ?? 0),
                k.Page ?? 1))
            .Where(kv => kv.Key.Length > 0)
            .ToList();

        return new OcrDocument
        {
            SourceObjectKey = sourceKey,
            TenantSlug = tenantSlug,
            OrderId = orderId,
            PageCount = response.DocumentMetadata?.Pages ?? 1,
            Lines = lines,
            KeyValues = keyValues
        };
    }

    private static string? ValueOf(Block key, IReadOnlyDictionary<string, Block> blocks)
    {
        var valueId = key.Relationships?
            .FirstOrDefault(r => r.Type == RelationshipType.VALUE)?.Ids
            .FirstOrDefault();

        return valueId is not null && blocks.TryGetValue(valueId, out var value) ? TextOf(value, blocks) : null;
    }

    /// <summary>
    /// Flattens a block's CHILD words into text. A selection element carries no text of its own,
    /// so it is rendered the way the form prints it - which is also how the model is told to
    /// read it.
    /// </summary>
    private static string TextOf(Block block, IReadOnlyDictionary<string, Block> blocks)
    {
        var childIds = block.Relationships?
            .FirstOrDefault(r => r.Type == RelationshipType.CHILD)?.Ids ?? [];

        var words = childIds
            .Select(blocks.GetValueOrDefault)
            .Where(b => b is not null)
            .Select(b => b!.BlockType == BlockType.SELECTION_ELEMENT
                ? b.SelectionStatus == SelectionStatus.SELECTED ? "[X]" : "[ ]"
                : b.Text ?? string.Empty)
            .Where(t => t.Length > 0);

        return string.Join(' ', words).Trim();
    }
}

/// <summary>
/// How the model is shown a page: the text as read, plus the key/value pairs Textract resolved.
/// </summary>
/// <remarks>
/// Both, not either. The pairs are high precision but incomplete and use each clinic's own
/// labels; the plain text is complete but unstructured. Giving the model only the pairs loses
/// whatever Textract failed to pair up, which on the compact layout is most of it.
/// </remarks>
public static class OcrPrompt
{
    public static string For(OcrDocument ocr)
    {
        var pairs = ocr.KeyValues.Count == 0
            ? "(none detected)"
            : string.Join("\n", ocr.KeyValues.Select(kv => $"{kv.Key} = {kv.Value ?? "(blank)"}"));

        return $"""
                --- REQUISITION, AS READ FROM THE PAGE ---
                {ocr.PlainText}

                --- FORM FIELDS TEXTRACT PAIRED UP (labels are each clinic's own wording) ---
                {pairs}
                """;
    }
}
