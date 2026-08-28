using ReqLens.Domain;

namespace ReqLens.Ai;

/// <summary>
/// Schema-constrained extraction via the Bedrock Converse API: the requisition schema is passed
/// as a tool definition, so the model returns a typed object rather than prose to be parsed.
/// </summary>
/// <remarks>
/// PAIRING STUB - intentionally unimplemented. This is the Bedrock call Glenn walks through in
/// the interview, so he writes it rather than inheriting it.
/// Shape to build:
///   1. Build a ToolSpecification whose InputSchema is the requisition JSON schema
///      (provider + NPI, patient demographics, panel codes, ICD-10 codes, specimen, consent).
///   2. ConverseRequest with ToolChoice forcing that tool, the OCR text as the user message,
///      and the guardrail identifier attached.
///   3. Read usage.InputTokens / usage.OutputTokens off the response, time the call, and fill in
///      BedrockCallTelemetry - including GuardrailIntervened from the stop reason.
///   4. Validate the tool payload against the schema; on failure, escalate per ModelChainOptions.
/// </remarks>
public sealed class BedrockRequisitionExtractor : IRequisitionExtractor
{
    private readonly ModelChainOptions _chain;

    public BedrockRequisitionExtractor(ModelChainOptions chain) => _chain = chain;

    public Task<ExtractionOutcome> ExtractAsync(
        string documentKey,
        string ocrText,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Pairing stub: implement the Bedrock Converse call.");
}
