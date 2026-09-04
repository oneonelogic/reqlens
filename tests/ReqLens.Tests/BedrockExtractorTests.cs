using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using ReqLens.Ai;
using ReqLens.Domain;

namespace ReqLens.Tests;

/// <summary>
/// The fallback chain, tested against a stubbed Bedrock.
/// </summary>
/// <remarks>
/// This is the logic that is hardest to check by running the real thing: throttling and guardrail
/// interventions do not happen on demand, and the difference between "hop to another vendor" and
/// "stop and ask a human" is exactly the kind of branch that is wrong for a month before anyone
/// notices.
/// </remarks>
public class BedrockExtractorTests
{
    private const string OcrText = "NPI 1245319599\nPatient Ashgrove, Marguerite\nMRN MRN782554";

    private static ModelChainOptions Chain(decimal maxCostPerDoc = 0.05m) => new()
    {
        Models =
        [
            new ModelChainEntry
            {
                ModelId = "primary-model", Role = ModelRole.Primary, MaxCostPerDoc = maxCostPerDoc,
                InputPricePerMillionTokens = 1.00m, OutputPricePerMillionTokens = 5.00m
            },
            new ModelChainEntry
            {
                ModelId = "availability-model", Role = ModelRole.Availability, MaxCostPerDoc = maxCostPerDoc,
                InputPricePerMillionTokens = 0.06m, OutputPricePerMillionTokens = 0.24m
            },
            new ModelChainEntry
            {
                ModelId = "escalation-model", Role = ModelRole.Escalation, MaxCostPerDoc = 0.25m,
                InputPricePerMillionTokens = 3.00m, OutputPricePerMillionTokens = 15.00m
            }
        ]
    };

    private static readonly GuardrailOptions Guardrail = new()
    {
        GuardrailId = "gr-test",
        GuardrailVersion = "1"
    };

    [Fact]
    public async Task A_clean_primary_call_returns_fields_and_costs_what_it_costs()
    {
        using var bedrock = new FakeBedrock().Then(_ => ToolResponse());

        var outcome = await new BedrockRequisitionExtractor(bedrock, Chain(), Guardrail)
            .ExtractAsync("req-001.pdf", OcrText);

        Assert.True(outcome.Succeeded);
        Assert.Single(outcome.Calls);
        Assert.Equal("primary-model", outcome.Telemetry.ModelId);
        Assert.True(outcome.Telemetry.SchemaValid);

        // 1000 in at $1/M plus 200 out at $5/M.
        Assert.Equal(0.001m + 0.001m, outcome.Telemetry.EstimatedCostUsd);
    }

    [Fact]
    public async Task Every_request_carries_the_guardrail_and_forces_the_tool()
    {
        using var bedrock = new FakeBedrock().Then(_ => ToolResponse());

        await new BedrockRequisitionExtractor(bedrock, Chain(), Guardrail).ExtractAsync("req-001.pdf", OcrText);

        var request = bedrock.Requests.Single();

        Assert.Equal("gr-test", request.GuardrailConfig.GuardrailIdentifier);
        Assert.Equal("1", request.GuardrailConfig.GuardrailVersion);
        Assert.Equal(RequisitionSchema.ToolName, request.ToolConfig.ToolChoice.Tool.Name);

        // The document is the untrusted part, so it has to be evaluated for prompt attacks as
        // well as used as the grounding source. The IAM policy denies an unguarded call outright,
        // but nothing enforces the qualifiers except this.
        var document = request.Messages.Single().Content[0].GuardContent.Text;

        Assert.Contains("grounding_source", document.Qualifiers);
        Assert.Contains("guard_content", document.Qualifiers);
        Assert.Contains("query", request.Messages.Single().Content[1].GuardContent.Text.Qualifiers);
    }

    [Fact]
    public async Task A_guardrail_intervention_stops_the_chain_dead()
    {
        // Retrying a blocked document on another model is the attacker's second roll of the dice.
        using var bedrock = new FakeBedrock()
            .Then(_ => new ConverseResponse
            {
                StopReason = StopReason.Guardrail_intervened,
                Usage = new TokenUsage { InputTokens = 900, OutputTokens = 0 },
                Metrics = new ConverseMetrics { LatencyMs = 120 }
            })
            .Then(_ => ToolResponse());

        var outcome = await new BedrockRequisitionExtractor(bedrock, Chain(), Guardrail)
            .ExtractAsync("req-013.pdf", OcrText);

        Assert.False(outcome.Succeeded);
        Assert.Single(outcome.Calls);
        Assert.True(outcome.Telemetry.GuardrailIntervened);
        Assert.Contains("guardrail", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Throttling_hops_to_the_other_vendor()
    {
        using var bedrock = new FakeBedrock()
            .Then(_ => throw new ThrottlingException("slow down"))
            .Then(_ => ToolResponse());

        var outcome = await new BedrockRequisitionExtractor(bedrock, Chain(), Guardrail)
            .ExtractAsync("req-001.pdf", OcrText);

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Calls.Count);
        Assert.Equal("availability-model", outcome.Telemetry.ModelId);
        Assert.Equal(ModelRole.Availability, outcome.Telemetry.Role);
    }

    [Fact]
    public async Task A_payload_that_fails_the_schema_escalates_rather_than_hopping_vendor()
    {
        // The model was reachable and answered - a different vendor is no more likely to be
        // right, so the hop that helps is upwards.
        using var bedrock = new FakeBedrock()
            .Then(_ => ToolResponse(payload =>
            {
                payload.Remove(RequisitionFields.PatientMrn);
                return payload;
            }))
            .Then(_ => ToolResponse());

        var outcome = await new BedrockRequisitionExtractor(bedrock, Chain(), Guardrail)
            .ExtractAsync("req-001.pdf", OcrText);

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Calls.Count);
        Assert.Equal("escalation-model", outcome.Telemetry.ModelId);
        Assert.False(outcome.Calls[0].SchemaValid);
    }

    [Fact]
    public async Task The_chain_gives_up_rather_than_looping()
    {
        using var bedrock = new FakeBedrock()
            .Then(_ => throw new ThrottlingException("slow down"))
            .Then(_ => throw new ThrottlingException("slow down"))
            .Then(_ => ToolResponse());

        var outcome = await new BedrockRequisitionExtractor(bedrock, Chain(), Guardrail)
            .ExtractAsync("req-001.pdf", OcrText);

        Assert.False(outcome.Succeeded);
        Assert.Equal(2, outcome.Calls.Count);
    }

    [Fact]
    public async Task A_value_that_is_not_on_the_page_has_its_confidence_capped()
    {
        // A hallucinated NPI with a valid check digit passes every deterministic test there is.
        // Grounding is the only thing that notices it was never printed.
        using var bedrock = new FakeBedrock().Then(_ => ToolResponse(payload =>
        {
            payload[RequisitionFields.ProviderNpi]!["value"] = "1679576722";
            payload[RequisitionFields.ProviderNpi]!["confidence"] = 0.99;
            return payload;
        }));

        var outcome = await new BedrockRequisitionExtractor(bedrock, Chain(), Guardrail)
            .ExtractAsync("req-001.pdf", OcrText);

        var npi = outcome.Fields.Single(f => f.Name == RequisitionFields.ProviderNpi);

        Assert.False(npi.Grounded);
        Assert.Equal(0.5, npi.Confidence);
    }

    [Fact]
    public async Task A_value_copied_from_the_page_is_left_alone()
    {
        using var bedrock = new FakeBedrock().Then(_ => ToolResponse());

        var outcome = await new BedrockRequisitionExtractor(bedrock, Chain(), Guardrail)
            .ExtractAsync("req-001.pdf", OcrText);

        var npi = outcome.Fields.Single(f => f.Name == RequisitionFields.ProviderNpi);

        Assert.True(npi.Grounded);
        Assert.Equal(0.97, npi.Confidence);
    }

    [Fact]
    public async Task A_call_that_would_breach_the_per_document_ceiling_is_not_made()
    {
        using var bedrock = new FakeBedrock();

        var outcome = await new BedrockRequisitionExtractor(bedrock, Chain(maxCostPerDoc: 0.0001m), Guardrail)
            .ExtractAsync("req-001.pdf", OcrText);

        Assert.False(outcome.Succeeded);
        Assert.Empty(bedrock.Requests);
        Assert.Contains("ceiling", outcome.FailureReason);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static ConverseResponse ToolResponse(Func<JsonObject, JsonObject>? mutate = null)
    {
        var payload = new JsonObject();

        foreach (var field in RequisitionFields.All)
        {
            payload[field] = new JsonObject
            {
                ["value"] = field switch
                {
                    RequisitionFields.ProviderNpi => "1245319599",
                    RequisitionFields.PatientLastName => "Ashgrove",
                    RequisitionFields.ConsentObtained => "true",
                    RequisitionFields.UnmappedNotes => "",
                    _ => "MRN782554"
                },
                ["confidence"] = 0.97,
                ["source_text"] = "NPI 1245319599"
            };
        }

        payload = mutate?.Invoke(payload) ?? payload;

        return new ConverseResponse
        {
            StopReason = StopReason.Tool_use,
            Usage = new TokenUsage { InputTokens = 1000, OutputTokens = 200 },
            Metrics = new ConverseMetrics { LatencyMs = 480 },
            Output = new ConverseOutput
            {
                Message = new Message
                {
                    Role = ConversationRole.Assistant,
                    Content =
                    [
                        new ContentBlock
                        {
                            ToolUse = new ToolUseBlock
                            {
                                Name = RequisitionSchema.ToolName,
                                ToolUseId = "tool-1",
                                Input = DocumentJson.FromElement(JsonSerializer.Deserialize<JsonElement>(payload))
                            }
                        }
                    ]
                }
            }
        };
    }

    /// <summary>
    /// A Bedrock client that answers from a script.
    /// </summary>
    /// <remarks>
    /// Subclassing the generated client rather than implementing IAmazonBedrockRuntime: the
    /// interface has dozens of members that would all have to be stubbed to exercise one, and
    /// the generated operations are virtual precisely so this works.
    /// </remarks>
    private sealed class FakeBedrock : AmazonBedrockRuntimeClient
    {
        private readonly Queue<Func<ConverseRequest, ConverseResponse>> _script = new();

        public List<ConverseRequest> Requests { get; } = [];

        public FakeBedrock()
            : base(new BasicAWSCredentials("test", "test"), RegionEndpoint.USEast2) { }

        public FakeBedrock Then(Func<ConverseRequest, ConverseResponse> answer)
        {
            _script.Enqueue(answer);
            return this;
        }

        public override Task<ConverseResponse> ConverseAsync(
            ConverseRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (_script.Count == 0)
                throw new InvalidOperationException("The extractor made more calls than the test scripted.");

            return Task.FromResult(_script.Dequeue()(request));
        }
    }
}
