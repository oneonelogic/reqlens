namespace ReqLens.Ai;

public enum ModelRole
{
    /// <summary>First choice. Cheap.</summary>
    Primary,

    /// <summary>Used when the primary is throttled or erroring - a different model family on purpose.</summary>
    Availability,

    /// <summary>Used when the primary answered but the answer failed schema or confidence checks.</summary>
    Escalation
}

public sealed class ModelChainEntry
{
    public required string ModelId { get; init; }
    public required ModelRole Role { get; init; }

    /// <summary>Per-document ceiling; a call that would exceed it is not made.</summary>
    public decimal MaxCostPerDoc { get; init; } = 0.05m;

    public decimal InputPricePerMillionTokens { get; init; }
    public decimal OutputPricePerMillionTokens { get; init; }

    public decimal EstimateCost(int inputTokens, int outputTokens)
        => (inputTokens / 1_000_000m * InputPricePerMillionTokens)
         + (outputTokens / 1_000_000m * OutputPricePerMillionTokens);
}

/// <summary>
/// The fallback chain, declared in config rather than hardcoded, so swapping models is a config
/// change that can be demonstrated live.
/// </summary>
public sealed class ModelChainOptions
{
    public const string SectionName = "ModelChain";

    public List<ModelChainEntry> Models { get; init; } = [];

    public ModelChainEntry Primary
        => Models.FirstOrDefault(m => m.Role == ModelRole.Primary)
           ?? throw new InvalidOperationException("Model chain has no Primary entry.");
}
