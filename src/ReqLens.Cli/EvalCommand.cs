using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.BedrockRuntime;
using Amazon.Textract;
using ReqLens.Ai;
using ReqLens.Domain;
using ReqLens.Ocr;
using ReqLens.Validation;

namespace ReqLens.Cli;

/// <summary>
/// Runs the golden set through the real extractor and grades the result.
/// </summary>
/// <remarks>
/// The whole point is that it exercises the same classes the Lambda does - the same OCR parsing,
/// the same schema, the same validators. A harness that reimplemented any of them would be
/// grading a pipeline that is not the one in production.
///
/// OCR output is cached under artifacts/eval-ocr/. Textract is the expensive half of a run and
/// its answer for a fixed PDF does not change, so re-running to compare two models costs Bedrock
/// tokens and nothing else. Pass --refresh-ocr to force it.
/// </remarks>
public static class EvalCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var limit = Options.Int(args, "--limit") ?? int.MaxValue;
        var modelOverride = Options.Value(args, "--model");
        var refreshOcr = args.Contains("--refresh-ocr");

        var chain = ChainSource.Load(modelOverride);
        var guardrail = new GuardrailOptions
        {
            GuardrailId = Workspace.Require("GUARDRAIL_ID", "guardrail_id"),
            GuardrailVersion = Workspace.Require("GUARDRAIL_VERSION", "guardrail_version")
        };

        using var bedrock = new AmazonBedrockRuntimeClient();
        using var textract = new AmazonTextractClient();

        var ocrService = OcrProviders.FromEnvironment(textract);
        var extractor = new BedrockRequisitionExtractor(bedrock, chain, guardrail);
        var validator = new RequisitionValidator();
        var catalog = LoadCatalog();

        var documents = Directory.EnumerateFiles(Workspace.Golden, "*.json").Order().Take(limit).ToList();

        Console.WriteLine($"Grading {documents.Count} document(s) against {chain.Primary.ModelId}");
        Console.WriteLine($"OCR provider: {ocrService.Name}");
        Console.WriteLine();

        var results = new List<DocumentResult>();

        foreach (var goldenPath in documents)
        {
            var document = Path.GetFileNameWithoutExtension(goldenPath);
            var golden = JsonDocument.Parse(await File.ReadAllTextAsync(goldenPath)).RootElement;

            var ocr = await OcrFor(document, golden, ocrService, refreshOcr);
            var outcome = await extractor.ExtractAsync($"{document}.pdf", OcrPrompt.For(ocr));

            var result = Grade(document, golden, outcome, validator, catalog);
            results.Add(result);

            Console.WriteLine(
                $"  {document}  {result.FieldsCorrect,2}/{result.FieldsGraded,2} fields  "
                + $"validation {(result.ValidationMatches ? "ok " : "OFF")}  "
                + $"review {(result.ReviewMatches ? "ok " : "OFF")}  "
                + $"{Report.Money(result.CostUsd)}  {result.LatencyMs} ms");
        }

        Console.WriteLine();
        var report = Report.From(results);
        report.Print();

        var path = Path.Combine(Workspace.Artifacts,
            $"eval-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");

        Directory.CreateDirectory(Workspace.Artifacts);

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            new { model = chain.Primary.ModelId, at = DateTimeOffset.UtcNow, report, results },
            new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }));

        Console.WriteLine();
        Console.WriteLine($"Written to {Path.GetRelativePath(Workspace.Root, path)}");

        return report.FieldExactAccuracy >= 0.95 && report.ReviewDecisionAccuracy >= 0.95 ? 0 : 2;
    }

    private static async Task<OcrDocument> OcrFor(
        string document, JsonElement golden, IOcrProvider service, bool refresh)
    {
        var cacheDirectory = Path.Combine(Workspace.Artifacts, "eval-ocr");
        Directory.CreateDirectory(cacheDirectory);

        // Cached per provider: a Textract read and a text-layer read of the same PDF are
        // different inputs, and silently reusing one for the other would make a comparison
        // between them meaningless.
        var cache = Path.Combine(cacheDirectory, $"{document}.{service.Name}.json");

        if (!refresh && File.Exists(cache))
        {
            var cached = JsonSerializer.Deserialize<OcrDocument>(await File.ReadAllTextAsync(cache));
            if (cached is not null) return cached;
        }

        var pdf = await File.ReadAllBytesAsync(Path.Combine(Workspace.Requisitions, document + ".pdf"));
        var slug = golden.GetProperty("tenant").GetString() ?? "unknown";

        var ocr = await service.ReadAsync(pdf, $"{document}.pdf", slug, Guid.Empty);

        await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(ocr));

        return ocr;
    }

    private static DocumentResult Grade(
        string document,
        JsonElement golden,
        ExtractionOutcome outcome,
        RequisitionValidator validator,
        IReadOnlyList<TestCatalogEntry> catalog)
    {
        var expectedFields = golden.GetProperty("fields");
        var expectedValidation = golden.GetProperty("expected_validation");

        var fields = outcome.Fields.ToList();
        var assessment = fields.Count > 0 ? validator.Assess(fields, catalog) : null;

        var comparisons = new List<FieldComparison>();

        foreach (var name in RequisitionFields.Graded)
        {
            var expected = expectedFields.TryGetProperty(name, out var e) ? Text(e) : null;
            var actual = fields.FirstOrDefault(f => f.Name == name)?.Value;

            comparisons.Add(new FieldComparison(name, expected, actual, Same(expected, actual)));
        }

        var validationMatches = assessment is not null
                                && Matches(assessment.Outcome, expectedValidation);

        var expectedReview = expectedValidation.GetProperty("should_need_review").GetBoolean();

        // A document that produced nothing at all is, correctly, in the review queue - so a
        // guardrail block on a document that should have been reviewed counts as agreement.
        var actualReview = assessment?.NeedsReview ?? true;

        return new DocumentResult(
            document,
            comparisons,
            comparisons.Count(c => c.Correct),
            comparisons.Count,
            validationMatches,
            expectedReview,
            actualReview,
            expectedReview == actualReview,
            outcome.Calls.Sum(c => c.EstimatedCostUsd),
            outcome.Calls.Sum(c => c.LatencyMs),
            outcome.Calls.Count,
            outcome.Telemetry.GuardrailIntervened,
            outcome.FailureReason);
    }

    private static bool Matches(ValidationOutcome actual, JsonElement expected) =>
        Bool(expected, "npi_valid") == actual.NpiValid
        && Bool(expected, "panel_code_present") == actual.PanelCodePresent
        && Bool(expected, "panel_in_catalog") == actual.PanelInCatalog
        && Bool(expected, "panel_active") == actual.PanelActive
        && Bool(expected, "specimen_matches") == actual.SpecimenMatches
        && Bool(expected, "diagnosis_code_present") == actual.DiagnosisCodePresent
        && Bool(expected, "icd10_valid") == actual.Icd10Valid;

    private static bool? Bool(JsonElement parent, string name) =>
        parent.GetProperty(name) is { ValueKind: not JsonValueKind.Null } v ? v.GetBoolean() : null;

    private static string? Text(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => e.GetString()
    };

    /// <summary>
    /// Whitespace-insensitive and case-insensitive. Anything stricter would score OCR spacing
    /// rather than extraction, and anything looser would let a wrong value pass.
    /// </summary>
    private static bool Same(string? expected, string? actual)
    {
        var left = Normalise(expected);
        var right = Normalise(actual);

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static List<TestCatalogEntry> LoadCatalog() =>
        Csv.Rows(Path.Combine(Workspace.Synthetic, "test-catalog.csv"), 4)
            .Select(r => new TestCatalogEntry
            {
                Code = r[0],
                Name = r[1],
                SpecimenType = r[2],
                Active = bool.Parse(r[3])
            })
            .ToList();

}

public sealed record FieldComparison(string Field, string? Expected, string? Actual, bool Correct);

public sealed record DocumentResult(
    string Document,
    IReadOnlyList<FieldComparison> Fields,
    int FieldsCorrect,
    int FieldsGraded,
    bool ValidationMatches,
    bool ExpectedReview,
    bool ActualReview,
    bool ReviewMatches,
    decimal CostUsd,
    long LatencyMs,
    int ModelCalls,
    bool GuardrailIntervened,
    string? FailureReason);
