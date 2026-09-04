using System.Text.Json;
using ReqLens.Domain;
using ReqLens.Validation;

namespace ReqLens.Tests;

/// <summary>
/// Grades the deterministic layer against all twenty golden documents, with the model taken out
/// of the picture: the golden `fields` are fed in as if extraction had been perfect, and the
/// validators have to reach the golden `expected_validation` on their own.
/// </summary>
/// <remarks>
/// This is the test that would have caught the tri-state bug. It runs offline and costs nothing,
/// so it can gate every commit, which the live eval harness cannot.
/// </remarks>
public class GoldenSetValidationTests
{
    private readonly RequisitionValidator _validator = new();
    private readonly List<TestCatalogEntry> _catalog = RepoData.Catalog();

    public static TheoryData<string> Documents()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(RepoData.Golden, "*.json").Order())
            data.Add(Path.GetFileNameWithoutExtension(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void Deterministic_layer_reaches_the_golden_verdict(string document)
    {
        var golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoData.Golden, document + ".json"))).RootElement;
        var expected = golden.GetProperty("expected_validation");

        var assessment = _validator.Assess(FieldsFrom(golden), _catalog);
        var actual = assessment.Outcome;

        Assert.Equal(Bool(expected, "npi_valid"), actual.NpiValid);
        Assert.Equal(Bool(expected, "panel_code_present"), actual.PanelCodePresent);
        Assert.Equal(Bool(expected, "panel_in_catalog"), actual.PanelInCatalog);
        Assert.Equal(Bool(expected, "panel_active"), actual.PanelActive);
        Assert.Equal(Bool(expected, "specimen_matches"), actual.SpecimenMatches);
        Assert.Equal(Bool(expected, "diagnosis_code_present"), actual.DiagnosisCodePresent);
        Assert.Equal(Bool(expected, "icd10_valid"), actual.Icd10Valid);

        // The one document whose review reason no deterministic check can see: req-007 carries a
        // handwritten margin note, and there is no structured field on the form that is wrong.
        // Only the model can surface it, by reporting it in unmapped_notes - so it is graded by
        // the live eval harness, not here.
        if (golden.GetProperty("defects").EnumerateArray().Any(d => d.GetString() == "HandwrittenNote"))
            return;

        Assert.Equal(
            Bool(expected, "should_need_review"),
            actual.ShouldNeedReview);
    }

    /// <summary>
    /// Golden fields at full confidence. The point of this suite is the deterministic layer, so
    /// the confidence rule is deliberately not allowed to fire and mask a validator that is wrong.
    /// </summary>
    private static List<ExtractedField> FieldsFrom(JsonElement golden)
    {
        var fields = golden.GetProperty("fields");

        return RequisitionFields.Graded.Select(name => new ExtractedField
        {
            Name = name,
            Value = fields.TryGetProperty(name, out var v) ? Text(v) : null,
            Confidence = 1.0
        }).ToList();
    }

    private static string? Text(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => e.GetString()
    };

    private static bool? Bool(JsonElement parent, string name) =>
        parent.GetProperty(name) is { ValueKind: not JsonValueKind.Null } v ? v.GetBoolean() : null;
}
