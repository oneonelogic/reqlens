using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using ReqLens.Ai;
using ReqLens.Domain;
using UglyToad.PdfPig;

namespace ReqLens.Ocr;

/// <summary>
/// Reads a requisition by handing the page to a vision model on Bedrock and asking it to
/// transcribe what is printed there.
/// </summary>
/// <remarks>
/// This exists because Textract is not callable on this account - the API returns
/// SubscriptionRequiredException, which is account activation and cannot be fixed in code. The
/// other fallback, <see cref="PdfTextLayerOcr"/>, only works on born-digital PDFs and would
/// return nothing for a real scan. This one reads an actual rendered page, so it keeps the
/// pipeline honest for images as well as PDFs.
///
/// It is a fallback, not the intended design, and the reasons matter:
///
///   * It reports no per-line confidence and no geometry. A vision model can be asked for a
///     bounding box but has no calibrated notion of how sure it is, and inventing either would
///     put a fabricated number in the field that carries a real Textract score. See
///     <see cref="OcrLine.Confidence"/>.
///   * It weakens grounding. The extractor checks that every value it returns occurs in the OCR
///     text. When the same model family produced that text, the check still catches the
///     extraction step inventing a value, but it can no longer catch the reading step doing so.
///     Textract is an independent witness; this is not.
///   * It costs a second model call per document.
///
/// Textract remains the default. Flip <c>OCR_PROVIDER</c> back the moment the account can call it.
/// </remarks>
public sealed class BedrockVisionOcr : IOcrProvider
{
    /// <summary>
    /// Transcription, not interpretation. Any normalising, labelling or field-mapping done here
    /// would be the extraction step happening a layer too early, where nothing validates it and
    /// no telemetry records it.
    /// </summary>
    private const string Instruction = """
        Transcribe this requisition form exactly as printed.

        One line of output per line on the page, in reading order. Preserve the wording, spelling,
        capitalisation and punctuation exactly as they appear, including any that look wrong.
        Render a ticked checkbox as [X] and an unticked one as [ ].

        Do not interpret, normalise, correct, summarise, explain or add commentary. Do not infer a
        value that is not printed. If a labelled field is blank, output the label alone.
        """;

    private readonly IAmazonBedrockRuntime _bedrock;
    private readonly BedrockVisionOptions _options;
    private readonly GuardrailOptions _guardrail;

    public BedrockVisionOcr(
        IAmazonBedrockRuntime bedrock,
        BedrockVisionOptions options,
        GuardrailOptions guardrail)
    {
        _bedrock = bedrock;
        _options = options;
        _guardrail = guardrail;
    }

    public string Name => OcrProviders.BedrockVisionName;

    public async Task<OcrDocument> ReadAsync(
        byte[] document,
        string sourceObjectKey,
        string tenantSlug,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var format = FormatOf(document);

        var request = new ConverseRequest
        {
            ModelId = _options.ModelId,

            Messages =
            [
                new Message
                {
                    Role = ConversationRole.User,
                    Content = [PageBlock(document, format), new ContentBlock { Text = Instruction }]
                }
            ],

            // This is the OCR guardrail, NOT the extraction one, and the difference is structural
            // rather than a preference. The extraction guardrail carries a contextual grounding
            // policy, and Converse rejects any request that applies it without a grounding source
            // - "Grounding source, query and content to guard are required". At OCR time there is
            // no source text to ground against, because producing it is what this call does.
            //
            // The OCR guardrail therefore checks the one thing that can be judged here: blocked
            // identifiers in the transcript. Prompt-attack screening happens one step later, on
            // the transcript, where there is finally text to read. A page cannot be screened
            // before it has been read.
            //
            // Attaching it is not optional either way. The Lambda role carries an explicit Deny
            // on inference without this exact guardrail identifier and version, so an unguarded
            // call fails outright rather than quietly succeeding.
            GuardrailConfig = new GuardrailConfiguration
            {
                GuardrailIdentifier = _guardrail.GuardrailId,
                GuardrailVersion = _guardrail.GuardrailVersion,
                Trace = GuardrailTrace.Enabled
            },

            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = _options.MaxOutputTokens,

                // There is a right answer printed on the page. Sampling away from it has no upside.
                Temperature = 0f
            }
        };

        var response = await _bedrock.ConverseAsync(request, cancellationToken);

        // A blocked scan is not an empty scan, and must not be allowed to look like one. An empty
        // OcrDocument would flow downstream and be reported as a document with no text on it,
        // when what actually happened is that the guardrail refused to read it.
        if (response.StopReason == StopReason.Guardrail_intervened)
            throw new OcrBlockedException(
                $"The Bedrock guardrail blocked '{sourceObjectKey}' during transcription. "
                + "The document has not been read and must go to human review.");

        var transcript = string.Concat(
            response.Output?.Message?.Content?.Select(c => c.Text ?? string.Empty) ?? []);

        var lines = transcript
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(text => new OcrLine(
                text,

                // No measurement is made. See the class remarks and OcrLine.Confidence.
                Confidence: null,

                // No geometry either. The model is not asked for bounding boxes: a plausible box
                // it cannot actually see would be worse than none, because a downstream reader
                // has no way to tell an estimated position from a measured one. Reading order is
                // carried by the order of this list, which is what PlainText uses.
                Top: null,
                Left: null,
                Page: 1))
            .ToList();

        return new OcrDocument
        {
            SourceObjectKey = sourceObjectKey,
            TenantSlug = tenantSlug,
            OrderId = orderId,
            PageCount = PageCountOf(document, format),
            Lines = lines,

            // Form key/value pairing is a Textract feature. Asking the model to pair labels with
            // values would be the extraction step doing itself badly one layer too early, with
            // none of the schema, validation or telemetry that step carries.
            KeyValues = []
        };
    }

    /// <summary>
    /// What kind of file this is, by its magic bytes rather than by the object key. A key can say
    /// anything; the first four bytes cannot.
    /// </summary>
    private static PageFormat FormatOf(byte[] document)
    {
        if (document.Length >= 5 && document[0] == 0x25 && document[1] == 0x50
            && document[2] == 0x44 && document[3] == 0x46 && document[4] == 0x2D)
            return PageFormat.Pdf;

        if (document.Length >= 8 && document[0] == 0x89 && document[1] == 0x50
            && document[2] == 0x4E && document[3] == 0x47)
            return PageFormat.Png;

        if (document.Length >= 3 && document[0] == 0xFF && document[1] == 0xD8 && document[2] == 0xFF)
            return PageFormat.Jpeg;

        throw new NotSupportedException(
            "The scan is not a PDF, PNG or JPEG. Bedrock accepts those three for a page; anything "
            + "else has to be converted before it reaches this provider.");
    }

    /// <summary>
    /// A PDF goes to Bedrock as a document block, which means no rasteriser and therefore no
    /// native dependency to get into a Lambda package. An image goes as an image block, which is
    /// what a real scan or a fax arrives as.
    /// </summary>
    private static ContentBlock PageBlock(byte[] document, PageFormat format) => format switch
    {
        PageFormat.Pdf => new ContentBlock
        {
            Document = new DocumentBlock
            {
                Format = DocumentFormat.Pdf,

                // Bedrock requires a name and rejects several characters in it, so this is a
                // constant rather than the object key. It is not used for anything downstream.
                Name = "requisition",
                Source = new DocumentSource { Bytes = new MemoryStream(document) }
            }
        },

        _ => new ContentBlock
        {
            Image = new ImageBlock
            {
                Format = format == PageFormat.Png ? ImageFormat.Png : ImageFormat.Jpeg,
                Source = new ImageSource { Bytes = new MemoryStream(document) }
            }
        }
    };

    /// <summary>
    /// Counted from the file rather than guessed from the transcript. PdfPig is already a
    /// dependency here and reading the page count does not require a text layer, so this stays
    /// true even for a scanned PDF that <see cref="PdfTextLayerOcr"/> could not read a word of.
    /// </summary>
    private static int PageCountOf(byte[] document, PageFormat format)
    {
        if (format != PageFormat.Pdf) return 1;

        try
        {
            using var pdf = PdfDocument.Open(document);
            return pdf.NumberOfPages;
        }
        catch
        {
            // A PDF Bedrock can render but PdfPig cannot parse is possible and is not worth
            // failing the read over. The transcript is the output that matters.
            return 1;
        }
    }

    private enum PageFormat { Pdf, Png, Jpeg }
}

/// <summary>Which model transcribes a page, and how much of it to allow back.</summary>
public sealed class BedrockVisionOptions
{
    /// <summary>
    /// The cheap model is the right one here. Transcription is the easiest thing a vision model
    /// does, and paying escalation prices to read a one-page form would be spending in the wrong
    /// place - the hard judgement happens in the extraction call, not this one.
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// A one-page requisition transcribes in a few hundred tokens. The cap is generous enough
    /// for a dense form and low enough that a model looping on a bad page cannot run up a bill.
    /// </summary>
    public int MaxOutputTokens { get; init; } = 2000;

    public static BedrockVisionOptions FromEnvironment() => new()
    {
        ModelId = Environment.GetEnvironmentVariable("OCR_MODEL_ID")
            ?? throw new InvalidOperationException(
                "OCR_MODEL_ID is not set, and OCR_PROVIDER is 'bedrock-vision'.")
    };
}

/// <summary>
/// The guardrail refused to read a document. Distinct from an empty read on purpose: one is a
/// decision about the document, the other is a fact about the page.
/// </summary>
public sealed class OcrBlockedException(string message) : Exception(message);
