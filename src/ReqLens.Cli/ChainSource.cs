using ReqLens.Ai;

namespace ReqLens.Cli;

/// <summary>
/// Where the CLI gets the model chain from.
/// </summary>
/// <remarks>
/// MODEL_CHAIN first, then the Terraform output, then the values below. The order matters: the
/// harness should grade the chain that is actually deployed, and only fall back to a copy when
/// there is no deployment to read. The copy is here rather than being the first choice precisely
/// so that it cannot silently diverge from lambdas.tf without anyone noticing - a graded run
/// prints the model id it used.
/// </remarks>
public static class ChainSource
{
    public static ModelChainOptions Load(string? primaryOverride = null)
    {
        var json = Workspace.Setting("MODEL_CHAIN", "model_chain") ?? Fallback;
        var chain = ModelChainOptions.FromJson(json);

        if (primaryOverride is null) return chain;

        // Swapping the primary is the point of --model: it makes "is Haiku good enough here"
        // a measurement rather than an opinion.
        var primary = chain.Primary;

        return new ModelChainOptions
        {
            Models =
            [
                new ModelChainEntry
                {
                    ModelId = primaryOverride,
                    Role = ModelRole.Primary,
                    MaxCostPerDoc = primary.MaxCostPerDoc,
                    InputPricePerMillionTokens = primary.InputPricePerMillionTokens,
                    OutputPricePerMillionTokens = primary.OutputPricePerMillionTokens
                },
                .. chain.Models.Where(m => m.Role != ModelRole.Primary)
            ]
        };
    }

    /// <summary>Mirrors infra/terraform/lambdas.tf. Used only when there is no deployment to read.</summary>
    private const string Fallback = """
        {
          "models": [
            { "modelId": "us.anthropic.claude-haiku-4-5-20251001-v1:0", "role": "Primary",
              "maxCostPerDoc": 0.05, "inputPricePerMillionTokens": 1.00, "outputPricePerMillionTokens": 5.00 },
            { "modelId": "us.amazon.nova-2-lite-v1:0", "role": "Availability",
              "maxCostPerDoc": 0.05, "inputPricePerMillionTokens": 0.06, "outputPricePerMillionTokens": 0.24 },
            { "modelId": "us.anthropic.claude-sonnet-4-5-20250929-v1:0", "role": "Escalation",
              "maxCostPerDoc": 0.25, "inputPricePerMillionTokens": 3.00, "outputPricePerMillionTokens": 15.00 }
          ]
        }
        """;
}
