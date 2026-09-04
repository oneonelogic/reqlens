using ReqLens.Domain;

namespace ReqLens.Cli;

/// <summary>
/// Per-field precision and recall over the golden set, plus the two order-level accuracies.
/// </summary>
/// <remarks>
/// Precision and recall are reported per field rather than as one number, because the fields
/// fail differently and an average hides it: a model that reads every name perfectly and
/// hallucinates panel codes has a respectable overall score and is unusable.
///
/// A value that is present but wrong counts against both precision and recall - it is a false
/// positive for the value returned and a false negative for the value that should have been.
/// Scoring it only once would make a confidently wrong extraction look better than a blank.
/// </remarks>
public sealed record Report
{
    public required IReadOnlyList<FieldScore> Fields { get; init; }
    public required int Documents { get; init; }
    public required double FieldExactAccuracy { get; init; }
    public required double ValidationAccuracy { get; init; }
    public required double ReviewDecisionAccuracy { get; init; }
    public required int ReviewFalseNegatives { get; init; }
    public required int ReviewFalsePositives { get; init; }
    public required decimal TotalCostUsd { get; init; }
    public required decimal CostPerDocumentUsd { get; init; }
    public required long MeanLatencyMs { get; init; }
    public required int ModelCalls { get; init; }
    public required int GuardrailInterventions { get; init; }

    public static Report From(IReadOnlyList<DocumentResult> results)
    {
        var scores = RequisitionFields.Graded.Select(field =>
        {
            var comparisons = results.Select(r => r.Fields.First(f => f.Field == field)).ToList();

            var truePositives = comparisons.Count(c => Present(c.Expected) && Present(c.Actual) && c.Correct);
            var falsePositives = comparisons.Count(c => Present(c.Actual) && !c.Correct);
            var falseNegatives = comparisons.Count(c => Present(c.Expected) && !c.Correct);

            return new FieldScore(
                field,
                comparisons.Count,
                comparisons.Count(c => c.Correct),
                Ratio(truePositives, truePositives + falsePositives),
                Ratio(truePositives, truePositives + falseNegatives));
        }).ToList();

        var graded = results.Sum(r => r.FieldsGraded);

        return new Report
        {
            Fields = scores,
            Documents = results.Count,
            FieldExactAccuracy = Ratio(results.Sum(r => r.FieldsCorrect), graded),
            ValidationAccuracy = Ratio(results.Count(r => r.ValidationMatches), results.Count),
            ReviewDecisionAccuracy = Ratio(results.Count(r => r.ReviewMatches), results.Count),

            // The asymmetric one. A document that needed a human and did not get one is a
            // clinical risk; a clean document sent for review is somebody's afternoon.
            ReviewFalseNegatives = results.Count(r => r.ExpectedReview && !r.ActualReview),
            ReviewFalsePositives = results.Count(r => !r.ExpectedReview && r.ActualReview),

            TotalCostUsd = results.Sum(r => r.CostUsd),
            CostPerDocumentUsd = results.Count == 0 ? 0 : results.Sum(r => r.CostUsd) / results.Count,
            MeanLatencyMs = results.Count == 0 ? 0 : (long)results.Average(r => r.LatencyMs),
            ModelCalls = results.Sum(r => r.ModelCalls),
            GuardrailInterventions = results.Count(r => r.GuardrailIntervened)
        };
    }

    public void Print()
    {
        Console.WriteLine($"{"FIELD",-26} {"N",3} {"EXACT",7} {"PREC",7} {"RECALL",7}");
        Console.WriteLine(new string('-', 60));

        foreach (var score in Fields)
            Console.WriteLine(
                $"{score.Field,-26} {score.Count,3} {score.ExactAccuracy,7:P0} {score.Precision,7:P0} {score.Recall,7:P0}");

        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"Documents                  {Documents}");
        Console.WriteLine($"Field exact accuracy       {FieldExactAccuracy:P1}");
        Console.WriteLine($"Validation agreement       {ValidationAccuracy:P1}");
        Console.WriteLine($"Review decision accuracy   {ReviewDecisionAccuracy:P1}");
        Console.WriteLine($"  missed reviews           {ReviewFalseNegatives}   <- the one that matters");
        Console.WriteLine($"  unnecessary reviews      {ReviewFalsePositives}");
        Console.WriteLine($"Model calls                {ModelCalls} ({GuardrailInterventions} guardrail)");
        Console.WriteLine($"Cost                       {Money(TotalCostUsd)} total, {Money(CostPerDocumentUsd)}/doc");
        Console.WriteLine($"Mean latency               {MeanLatencyMs} ms");
    }

    /// <summary>
    /// Dollars, formatted by hand. InvariantGlobalization is on across this solution, so the "C"
    /// format specifier renders the generic currency sign rather than a dollar sign.
    /// </summary>
    public static string Money(decimal amount) => $"${amount:F4}";

    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value);

    private static double Ratio(int numerator, int denominator) => denominator == 0 ? 1 : (double)numerator / denominator;
}

public sealed record FieldScore(string Field, int Count, int Correct, double Precision, double Recall)
{
    public double ExactAccuracy => Count == 0 ? 1 : (double)Correct / Count;
}
