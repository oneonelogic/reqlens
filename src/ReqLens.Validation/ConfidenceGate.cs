namespace ReqLens.Validation;

/// <summary>
/// Thresholds that decide whether an order flows straight through or lands in the review queue.
/// Amber and red both queue; the split only drives the badge colour in the console.
/// </summary>
public sealed class ConfidenceGate
{
    public double AcceptThreshold { get; init; } = 0.90;
    public double ReviewThreshold { get; init; } = 0.70;

    /// <summary>A field that fails deterministic validation always queues, whatever the model claimed.</summary>
    public bool RequiresReview(double confidence, bool validationPassed)
        => !validationPassed || confidence < AcceptThreshold;
}
