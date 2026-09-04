using ReqLens.Domain;

namespace ReqLens.Ocr;

/// <summary>
/// Turns the bytes of one requisition into the OCR projection the rest of the pipeline reads.
/// </summary>
/// <remarks>
/// An interface with three implementations because the pipeline has to be able to run when
/// Textract cannot. Everything downstream - the prompt, the schema, the validators, the eval
/// harness - consumes <see cref="OcrDocument"/> and neither knows nor cares which provider
/// produced it.
/// </remarks>
public interface IOcrProvider
{
    /// <summary>Names the provider in logs and telemetry, so a result can be attributed later.</summary>
    string Name { get; }

    Task<OcrDocument> ReadAsync(
        byte[] document,
        string sourceObjectKey,
        string tenantSlug,
        Guid orderId,
        CancellationToken cancellationToken = default);
}

/// <summary>Chooses the provider from configuration.</summary>
/// <remarks>
/// Textract is the default and the one the architecture is built around. The environment
/// variable exists because a provider that can only be swapped by a rebuild is not really
/// swappable, and because this account's Textract subscription was not a thing that could be
/// fixed from inside the codebase.
/// </remarks>
public static class OcrProviders
{
    public const string TextractName = "textract";
    public const string PdfTextLayerName = "pdf-text-layer";
    public const string BedrockVisionName = "bedrock-vision";

    public static IOcrProvider FromEnvironment(
        Amazon.Textract.IAmazonTextract? textract = null,
        Amazon.BedrockRuntime.IAmazonBedrockRuntime? bedrock = null)
    {
        var chosen = Environment.GetEnvironmentVariable("OCR_PROVIDER")?.Trim().ToLowerInvariant();

        return chosen switch
        {
            PdfTextLayerName => new PdfTextLayerOcr(),

            BedrockVisionName => new BedrockVisionOcr(
                bedrock ?? new Amazon.BedrockRuntime.AmazonBedrockRuntimeClient(),
                BedrockVisionOptions.FromEnvironment(),
                ReqLens.Ai.GuardrailOptions.FromEnvironment("OCR_")),

            TextractName or null or "" => new TextractOcrService(textract ?? new Amazon.Textract.AmazonTextractClient()),

            _ => throw new InvalidOperationException(
                $"OCR_PROVIDER '{chosen}' is not a provider. Use '{TextractName}', "
                + $"'{PdfTextLayerName}' or '{BedrockVisionName}'.")
        };
    }
}
