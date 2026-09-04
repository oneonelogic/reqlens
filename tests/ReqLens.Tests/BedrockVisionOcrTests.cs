using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using ReqLens.Ai;
using ReqLens.Ocr;
using Xunit;

namespace ReqLens.Tests;

/// <summary>
/// The vision OCR fallback, tested against a stubbed Bedrock.
/// </summary>
/// <remarks>
/// What is worth pinning down here is not that a model can read a form - it can - but the things
/// around that which are easy to get quietly wrong: that a PDF goes as a document and an image as
/// an image, that the guardrail is attached on every call because the role's policy denies the
/// call without it, that a blocked document is not allowed to look like an empty one, and that no
/// confidence is invented for a reader that measures none.
/// </remarks>
public class BedrockVisionOcrTests
{
    private static readonly GuardrailOptions Guardrail = new()
    {
        GuardrailId = "gr-test",
        GuardrailVersion = "1"
    };

    private static readonly BedrockVisionOptions Options = new() { ModelId = "ocr-model" };

    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private static byte[] Pdf() =>
        File.ReadAllBytes(Path.Combine(RepoData.Synthetic, "requisitions", "req-003.pdf"));

    private static BedrockVisionOcr Ocr(FakeBedrock bedrock) =>
        new(bedrock, Options, Guardrail);

    private static ConverseResponse Transcribes(string text) => new()
    {
        StopReason = StopReason.End_turn,
        Output = new ConverseOutput
        {
            Message = new Message
            {
                Role = ConversationRole.Assistant,
                Content = [new ContentBlock { Text = text }]
            }
        },
        Usage = new TokenUsage { InputTokens = 1793, OutputTokens = 174 }
    };

    [Fact]
    public async Task A_pdf_is_sent_as_a_document_not_an_image()
    {
        var bedrock = new FakeBedrock().Then(_ => Transcribes("NPI 1440403628"));

        await Ocr(bedrock).ReadAsync(Pdf(), "scans/bluffcreek/req-003.pdf", "bluffcreek", Guid.NewGuid());

        var content = bedrock.Requests.Single().Messages.Single().Content;
        var page = content.Single(c => c.Document is not null);

        Assert.Equal(DocumentFormat.Pdf, page.Document.Format);
        Assert.DoesNotContain(content, c => c.Image is not null);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public async Task A_raster_scan_is_sent_as_an_image(string format)
    {
        var bedrock = new FakeBedrock().Then(_ => Transcribes("NPI 1440403628"));
        var bytes = format == "png" ? Png : Jpeg;

        await Ocr(bedrock).ReadAsync(bytes, "scans/bluffcreek/fax.png", "bluffcreek", Guid.NewGuid());

        var content = bedrock.Requests.Single().Messages.Single().Content;
        var page = content.Single(c => c.Image is not null);

        Assert.Equal(format == "png" ? ImageFormat.Png : ImageFormat.Jpeg, page.Image.Format);
        Assert.DoesNotContain(content, c => c.Document is not null);
    }

    /// <summary>
    /// The format is taken from the file's magic bytes, never from the key. A key can claim
    /// anything; a partner clinic uploading a PNG named .pdf must not steer the request shape.
    /// </summary>
    [Fact]
    public async Task The_format_comes_from_the_bytes_not_the_key()
    {
        var bedrock = new FakeBedrock().Then(_ => Transcribes("NPI 1440403628"));

        await Ocr(bedrock).ReadAsync(Png, "scans/bluffcreek/actually-a-png.pdf", "bluffcreek", Guid.NewGuid());

        var content = bedrock.Requests.Single().Messages.Single().Content;
        Assert.Contains(content, c => c.Image is not null);
        Assert.DoesNotContain(content, c => c.Document is not null);
    }

    [Fact]
    public async Task Every_call_carries_the_pinned_guardrail()
    {
        var bedrock = new FakeBedrock().Then(_ => Transcribes("NPI 1440403628"));

        await Ocr(bedrock).ReadAsync(Png, "scans/bluffcreek/fax.png", "bluffcreek", Guid.NewGuid());

        var config = bedrock.Requests.Single().GuardrailConfig;

        Assert.Equal("gr-test", config.GuardrailIdentifier);
        Assert.Equal("1", config.GuardrailVersion);
    }

    [Fact]
    public async Task The_transcript_becomes_lines_in_reading_order_with_blanks_dropped()
    {
        var bedrock = new FakeBedrock().Then(_ => Transcribes(
            "Bluff Creek Oncology Associates\n\n  NPI  \n1440403628\n\n[X] Consent obtained\n"));

        var doc = await Ocr(bedrock).ReadAsync(Png, "scans/bluffcreek/fax.png", "bluffcreek", Guid.NewGuid());

        Assert.Equal(
            ["Bluff Creek Oncology Associates", "NPI", "1440403628", "[X] Consent obtained"],
            doc.Lines.Select(l => l.Text));

        // PlainText is what the extractor is shown, so reading order has to survive the round trip.
        Assert.StartsWith("Bluff Creek Oncology Associates\nNPI\n", doc.PlainText);

        // No geometry is invented; order lives in the list, not in a fabricated position.
        Assert.All(doc.Lines, l => Assert.Null(l.Top));
        Assert.All(doc.Lines, l => Assert.Null(l.Left));
    }

    /// <summary>
    /// The point of the nullable confidence. A vision model measures nothing, so it reports
    /// nothing, and the mean has to come back null rather than as a flattering 100% that would
    /// sit in the same field as a real Textract score.
    /// </summary>
    [Fact]
    public async Task No_confidence_is_invented()
    {
        var bedrock = new FakeBedrock().Then(_ => Transcribes("NPI 1440403628\nWhole Blood EDTA"));

        var doc = await Ocr(bedrock).ReadAsync(Png, "scans/bluffcreek/fax.png", "bluffcreek", Guid.NewGuid());

        Assert.All(doc.Lines, l => Assert.Null(l.Confidence));
        Assert.Null(doc.MeanOcrConfidence);
    }

    /// <summary>
    /// A blocked document and an unreadable one are different facts and must not collapse into
    /// the same empty OcrDocument. An empty document would travel downstream and be reported as
    /// a page with no text on it, when the truth is that the guardrail refused to read it.
    /// </summary>
    [Fact]
    public async Task A_guardrail_block_throws_rather_than_returning_an_empty_document()
    {
        var bedrock = new FakeBedrock().Then(_ => new ConverseResponse
        {
            StopReason = StopReason.Guardrail_intervened,
            Output = new ConverseOutput { Message = new Message { Content = [] } }
        });

        var blocked = await Assert.ThrowsAsync<OcrBlockedException>(() =>
            Ocr(bedrock).ReadAsync(Png, "scans/bluffcreek/fax.png", "bluffcreek", Guid.NewGuid()));

        Assert.Contains("scans/bluffcreek/fax.png", blocked.Message);
    }

    [Fact]
    public async Task An_unsupported_file_is_refused_before_any_model_is_called()
    {
        var bedrock = new FakeBedrock();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Ocr(bedrock).ReadAsync("GIF89a"u8.ToArray(), "scans/bluffcreek/x.gif", "bluffcreek", Guid.NewGuid()));

        Assert.Empty(bedrock.Requests);
    }

    /// <summary>
    /// Page count is read from the file, not inferred from the transcript, so it stays true for a
    /// scanned PDF whose text layer is empty.
    /// </summary>
    [Fact]
    public async Task Page_count_is_read_from_the_file()
    {
        var bedrock = new FakeBedrock().Then(_ => Transcribes("NPI 1440403628"));

        var doc = await Ocr(bedrock).ReadAsync(Pdf(), "scans/bluffcreek/req-003.pdf", "bluffcreek", Guid.NewGuid());

        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public async Task The_order_and_tenant_are_carried_through_untouched()
    {
        var orderId = Guid.NewGuid();
        var bedrock = new FakeBedrock().Then(_ => Transcribes("NPI 1440403628"));

        var doc = await Ocr(bedrock).ReadAsync(Png, "scans/bluffcreek/fax.png", "bluffcreek", orderId);

        Assert.Equal(orderId, doc.OrderId);
        Assert.Equal("bluffcreek", doc.TenantSlug);
        Assert.Equal("scans/bluffcreek/fax.png", doc.SourceObjectKey);

        // Key/value pairing is a Textract feature and is deliberately not guessed at here.
        Assert.Empty(doc.KeyValues);
    }

    private sealed class FakeBedrock : AmazonBedrockRuntimeClient
    {
        private readonly Queue<Func<ConverseRequest, ConverseResponse>> _script = new();

        public List<ConverseRequest> Requests { get; } = [];

        public FakeBedrock()
            : base(new BasicAWSCredentials("test", "test"), RegionEndpoint.USEast2) { }

        public FakeBedrock Then(Func<ConverseRequest, ConverseResponse> answer)
        {
            _script.Enqueue(answer);
            return this;
        }

        public override Task<ConverseResponse> ConverseAsync(
            ConverseRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (_script.Count == 0)
                throw new InvalidOperationException("The provider made more calls than the test scripted.");

            return Task.FromResult(_script.Dequeue()(request));
        }
    }
}
