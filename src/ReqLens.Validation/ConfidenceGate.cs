namespace ReqLens.Validation;

/// <summary>Badge colour in the console. Amber and red both queue; only the shade differs.</summary>
public enum ConfidenceBand { High, Medium, Low }

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

    public ConfidenceBand Band(double confidence) => confidence switch
    {
        _ when confidence >= AcceptThreshold => ConfidenceBand.High,
        _ when confidence >= ReviewThreshold => ConfidenceBand.Medium,
        _ => ConfidenceBand.Low
    };
}
