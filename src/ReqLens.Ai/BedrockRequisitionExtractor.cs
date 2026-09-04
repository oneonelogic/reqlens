using System.Diagnostics;
using System.Text.Json.Nodes;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReqLens.Domain;

namespace ReqLens.Ai;

/// <summary>
/// Schema-constrained extraction via the Bedrock Converse API: the requisition schema is passed
/// as a tool definition, so the model returns a typed object rather than prose to be parsed.
/// </summary>
/// <remarks>
/// Three failure modes, three different responses, and the difference between them is the whole
/// design:
///
///   * The model is unavailable or throttled - hop to a different vendor and try again.
///   * The model answered but the answer does not satisfy the schema - hop up to a stronger
///     model of the same family, once.
///   * The guardrail intervened - stop. Do not retry, do not try another model. A retry after a
///     guardrail block is an attempt to get a different answer to a question that has already
///     been answered, and on a prompt-injection attempt it is the attacker's second roll of the
///     dice. The document goes to a human, which is the same place a low-confidence field goes.
/// </remarks>
public sealed class BedrockRequisitionExtractor : IRequisitionExtractor
{
    private const int MaxOutputTokens = 2000;

    private readonly IAmazonBedrockRuntime _bedrock;
    private readonly ModelChainOptions _chain;
    private readonly GuardrailOptions _guardrail;
    private readonly ILogger _log;

    public BedrockRequisitionExtractor(
        IAmazonBedrockRuntime bedrock,
        ModelChainOptions chain,
        GuardrailOptions guardrail,
        ILogger<BedrockRequisitionExtractor>? logger = null)
    {
        _bedrock = bedrock;
        _chain = chain;
        _guardrail = guardrail;
        _log = logger ?? NullLogger<BedrockRequisitionExtractor>.Instance;
    }

    public async Task<ExtractionOutcome> ExtractAsync(
        string documentKey,
        string ocrText,
        CancellationToken cancellationToken = default)
    {
        var calls = new List<BedrockCallTelemetry>();
        var tried = new HashSet<ModelRole>();

        var entry = _chain.Primary;

        while (true)
        {
            tried.Add(entry.Role);

            var attempt = await CallAsync(entry, documentKey, ocrText, calls.Count + 1, cancellationToken);
            calls.Add(attempt.Telemetry);

            if (attempt.Fields is { Count: > 0 })
                return new ExtractionOutcome { Fields = attempt.Fields, Calls = calls };

            var next = NextInChain(attempt, tried);

            if (next is null)
            {
                _log.LogWarning("Extraction of {DocumentKey} gave up after {Attempts} call(s): {Reason}",
                    documentKey, calls.Count, attempt.Telemetry.FailureReason);

                return new ExtractionOutcome { Fields = [], Calls = calls };
            }

            _log.LogInformation("Extraction of {DocumentKey} falling back to {Role} ({ModelId}): {Reason}",
                documentKey, next.Role, next.ModelId, attempt.Telemetry.FailureReason);

            entry = next;
        }
    }

    /// <summary>Why an attempt produced nothing, which is what decides where to go next.</summary>
    private enum Failure
    {
        /// <summary>The guardrail blocked it. A decision, not an error.</summary>
        Guardrail,

        /// <summary>The model was unreachable, throttled or erroring.</summary>
        Unreachable,

        /// <summary>The call was not made because it would have breached the cost ceiling.</summary>
        Budget,

        /// <summary>The model answered, and the answer was unusable.</summary>
        BadAnswer
    }

    private ModelChainEntry? NextInChain(Attempt attempt, IReadOnlySet<ModelRole> tried)
    {
        var wanted = attempt.Failure switch
        {
            // The success path never reaches here; Fields being non-empty ends the loop first.
            null => (ModelRole?)null,

            // Nowhere further to go. Retrying a blocked document on another model is an attempt
            // to get a different answer to a question that has already been answered.
            Failure.Guardrail => null,

            // A different vendor may be up when this one is not.
            Failure.Unreachable => ModelRole.Availability,

            // Deliberately NOT the escalation model. This document is already too expensive for
            // the cheap model's ceiling, and the escalation model costs several times more -
            // reaching for it here would spend the most on exactly the documents flagged as not
            // worth spending on. The availability model is the cheaper one, so it is worth a try.
            Failure.Budget => ModelRole.Availability,

            // The model was reachable and wrong. Another vendor of similar weight is no more
            // likely to be right, so the hop that helps is upwards.
            Failure.BadAnswer => ModelRole.Escalation,

            _ => null
        };

        if (wanted is not { } role || tried.Contains(role)) return null;

        var next = _chain.ForRole(role);

        return next is not null && !tried.Contains(next.Role) ? next : null;
    }

    private sealed record Attempt(
        IReadOnlyList<ExtractedField>? Fields,
        BedrockCallTelemetry Telemetry,
        Failure? Failure);

    private async Task<Attempt> CallAsync(
        ModelChainEntry entry,
        string documentKey,
        string ocrText,
        int attemptNumber,
        CancellationToken cancellationToken)
    {
        var telemetry = new BedrockCallTelemetry
        {
            ModelId = entry.ModelId,
            Role = entry.Role,
            DocumentKey = documentKey,
            Attempt = attemptNumber
        };

        // The per-document ceiling is a ceiling on calls MADE, not on bills received, so it has
        // to be checked before the request goes out. Four characters per token is the usual
        // rough figure and is deliberately pessimistic here - output is charged at the cap.
        var estimatedInput = (RequisitionSchema.SystemPrompt.Length + RequisitionSchema.Instruction.Length
                              + ocrText.Length + RequisitionSchema.SchemaJson().Length) / 4;

        var worstCase = entry.EstimateCost(estimatedInput, MaxOutputTokens);

        if (worstCase > entry.MaxCostPerDoc)
        {
            return new Attempt(null, telemetry with
            {
                SchemaValid = false,
                EstimatedCostUsd = worstCase,
                FailureReason = $"Estimated worst-case cost ${worstCase:F4} exceeds the ${entry.MaxCostPerDoc:F4} "
                                + "per-document ceiling for this model; call not made."
            }, Failure.Budget);
        }

        var request = BuildRequest(entry, ocrText);

        var stopwatch = Stopwatch.StartNew();
        ConverseResponse response;

        try
        {
            response = await _bedrock.ConverseAsync(request, cancellationToken);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            return new Attempt(null, telemetry with
            {
                LatencyMs = stopwatch.ElapsedMilliseconds,
                SchemaValid = false,
                FailureReason = $"{entry.ModelId} was unavailable: {ex.GetType().Name} - {ex.Message}"
            }, Failure.Unreachable);
        }

        stopwatch.Stop();

        var inputTokens = response.Usage?.InputTokens ?? 0;
        var outputTokens = response.Usage?.OutputTokens ?? 0;

        telemetry = telemetry with
        {
            LatencyMs = response.Metrics?.LatencyMs ?? stopwatch.ElapsedMilliseconds,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedCostUsd = entry.EstimateCost(inputTokens, outputTokens),
            GuardrailIntervened = response.StopReason == StopReason.Guardrail_intervened
        };

        if (telemetry.GuardrailIntervened)
        {
            return new Attempt(null, telemetry with
            {
                SchemaValid = false,
                FailureReason = "The Bedrock guardrail intervened; routed for human review. "
                                + GuardrailDetail(response)
            }, Failure.Guardrail);
        }

        var toolUse = response.Output?.Message?.Content?
            .FirstOrDefault(c => c.ToolUse is not null && c.ToolUse.Name == RequisitionSchema.ToolName)?.ToolUse;

        if (toolUse is null)
        {
            return new Attempt(null, telemetry with
            {
                SchemaValid = false,
                FailureReason = $"{entry.ModelId} returned no {RequisitionSchema.ToolName} tool call "
                                + $"(stop reason {response.StopReason?.Value ?? "unknown"})."
            }, Failure.BadAnswer);
        }

        var parsed = RequisitionSchema.Parse(DocumentJson.ToNode(toolUse.Input));

        if (!parsed.IsValid)
        {
            return new Attempt(null, telemetry with
            {
                SchemaValid = false,
                MinFieldConfidence = MinConfidence(parsed.Fields),
                FailureReason = $"Tool payload did not satisfy the schema: {parsed.Problem}"
            }, Failure.BadAnswer);
        }

        var fields = Ground(parsed.Fields, ocrText);

        return new Attempt(fields, telemetry with
        {
            SchemaValid = true,
            MinFieldConfidence = MinConfidence(fields)
        }, Failure: null);
    }

    private ConverseRequest BuildRequest(ModelChainEntry entry, string ocrText) => new()
    {
        ModelId = entry.ModelId,

        System = [new SystemContentBlock { Text = RequisitionSchema.SystemPrompt }],

        Messages =
        [
            new Message
            {
                Role = ConversationRole.User,
                Content =
                [
                    // The document is marked as BOTH a grounding source and guarded content.
                    // Qualifying it only as a grounding source would score it for grounding but
                    // skip prompt-attack evaluation on the one part of this request that came
                    // from outside - which is precisely backwards.
                    Guarded(ocrText, "grounding_source", "guard_content"),

                    // The instruction is ours, and is sent as the query the answer is graded
                    // for relevance against.
                    Guarded(RequisitionSchema.Instruction, "query")
                ]
            }
        ],

        ToolConfig = new ToolConfiguration
        {
            Tools =
            [
                new Tool
                {
                    ToolSpec = new ToolSpecification
                    {
                        Name = RequisitionSchema.ToolName,
                        Description = "Record the fields transcribed from one genetic-testing requisition.",
                        InputSchema = new ToolInputSchema { Json = RequisitionSchema.InputSchema() }
                    }
                }
            ],

            // Forcing the tool is what makes this an extraction rather than a conversation:
            // there is no path by which the model returns a paragraph of prose instead.
            ToolChoice = new ToolChoice { Tool = new SpecificToolChoice { Name = RequisitionSchema.ToolName } }
        },

        GuardrailConfig = new GuardrailConfiguration
        {
            GuardrailIdentifier = _guardrail.GuardrailId,
            GuardrailVersion = _guardrail.GuardrailVersion,
            Trace = GuardrailTrace.Enabled
        },

        InferenceConfig = new InferenceConfiguration
        {
            MaxTokens = MaxOutputTokens,

            // Transcription, not composition. There is a right answer printed on the page and
            // sampling away from it has no upside.
            Temperature = 0f
        }
    };

    private static ContentBlock Guarded(string text, params string[] qualifiers) => new()
    {
        GuardContent = new GuardrailConverseContentBlock
        {
            Text = new GuardrailConverseTextBlock
            {
                Text = text,
                Qualifiers = [.. qualifiers]
            }
        }
    };

    /// <summary>
    /// Confirms each value actually occurs in the OCR text, and drops the confidence of anything
    /// that does not.
    /// </summary>
    /// <remarks>
    /// Capped rather than rejected, on purpose. Whitespace and line breaks make exact matching
    /// imperfect - a name split across two OCR lines is present on the page but not as a
    /// substring - so a miss here is evidence, not proof. Capping confidence sends the field to
    /// a human instead of throwing away a document that may be perfectly good.
    /// </remarks>
    private static List<ExtractedField> Ground(IReadOnlyList<ExtractedField> fields, string ocrText)
    {
        const double UngroundedCeiling = 0.5;

        var haystack = Normalise(ocrText);

        foreach (var field in fields)
        {
            // Consent is read off a checkbox and notes are transcribed prose; neither is a
            // substring of the page in any useful sense.
            if (field.Name is RequisitionFields.ConsentObtained or RequisitionFields.UnmappedNotes)
                continue;

            if (string.IsNullOrWhiteSpace(field.Value)) continue;

            field.Grounded = haystack.Contains(Normalise(field.Value), StringComparison.OrdinalIgnoreCase);

            if (field.Grounded == false && field.Confidence > UngroundedCeiling)
                field.Confidence = UngroundedCeiling;
        }

        return [.. fields];
    }

    private static string Normalise(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static double? MinConfidence(IReadOnlyList<ExtractedField> fields)
    {
        var graded = fields.Where(f => RequisitionFields.Graded.Contains(f.Name)).ToList();
        return graded.Count == 0 ? null : graded.Min(f => f.Confidence);
    }

    /// <summary>
    /// Whether the failure is the model being unreachable rather than the model being wrong.
    /// Only the first kind is worth trying a different vendor for.
    /// </summary>
    private static bool IsTransient(Exception ex) => ex switch
    {
        ThrottlingException => true,
        ModelNotReadyException => true,
        ModelTimeoutException => true,
        ServiceUnavailableException => true,
        InternalServerException => true,
        Amazon.Runtime.AmazonServiceException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => true,
        Amazon.Runtime.AmazonServiceException { StatusCode: System.Net.HttpStatusCode.ServiceUnavailable } => true,
        HttpRequestException => true,
        TaskCanceledException => true,
        _ => false
    };

    private static string GuardrailDetail(ConverseResponse response)
    {
        var reason = response.Trace?.Guardrail?.ActionReason;
        return string.IsNullOrWhiteSpace(reason) ? string.Empty : $"Reason: {reason}";
    }
}
