namespace ReqLens.Ai;

/// <summary>
/// The published guardrail every extraction call must carry.
/// </summary>
/// <remarks>
/// Pinned to a numbered version, never DRAFT: editing the guardrail in the console must not be
/// able to change what a running pipeline does. The Extract Lambda's IAM policy carries an
/// explicit Deny on inference without this exact identifier, so omitting it here does not
/// degrade to an unguarded call - it fails outright, which is the intent.
/// </remarks>
public sealed class GuardrailOptions
{
    public required string GuardrailId { get; init; }
    public required string GuardrailVersion { get; init; }

    /// <param name="prefix">
    /// Names a second guardrail in the same environment. The vision OCR provider passes "OCR_"
    /// and gets its own, because the extraction guardrail's contextual grounding policy cannot be
    /// applied to a transcription call - Converse rejects the request when no grounding source is
    /// present, and at OCR time producing that source is the point of the call.
    /// </param>
    public static GuardrailOptions FromEnvironment(string prefix = "") => new()
    {
        GuardrailId = Environment.GetEnvironmentVariable($"{prefix}GUARDRAIL_ID")
            ?? throw new InvalidOperationException($"{prefix}GUARDRAIL_ID is not set."),
        GuardrailVersion = Environment.GetEnvironmentVariable($"{prefix}GUARDRAIL_VERSION")
            ?? throw new InvalidOperationException($"{prefix}GUARDRAIL_VERSION is not set.")
    };
}
