using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.CloudWatch;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using ReqLens.Ai;
using ReqLens.Data;
using ReqLens.Domain;
using ReqLens.Pipeline;
using ReqLens.Validation;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ReqLens.Lambdas.Extract;

/// <summary>
/// SQS-triggered. Loads the OCR, runs schema-constrained extraction through the model chain,
/// applies the deterministic validators and the confidence gate, writes the order and its fields,
/// and emits per-call telemetry.
/// </summary>
/// <remarks>
/// Like Ingest, an adapter: the work is in <see cref="ExtractStep"/>. What this class owns is the
/// batch-failure contract with SQS and the CloudWatch metric sink.
/// </remarks>
public class ExtractFunction
{
    // Clients and configuration are static so a warm execution environment pays for them once.
    // Reading MODEL_CHAIN per invocation would re-parse the same JSON for every document.
    private static readonly IAmazonS3 S3 = new AmazonS3Client();
    private static readonly IAmazonBedrockRuntime Bedrock = new AmazonBedrockRuntimeClient();
    private static readonly IAmazonCloudWatch CloudWatch = new AmazonCloudWatchClient();

    private static readonly ModelChainOptions Chain = ModelChainOptions.FromEnvironment();
    private static readonly GuardrailOptions Guardrail = GuardrailOptions.FromEnvironment();

    private static readonly RequisitionValidator Validator = new();

    public async Task<SQSBatchResponse> FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        var log = new CloudWatchPipelineLog(CloudWatch, context);
        var extractor = new BedrockRequisitionExtractor(Bedrock, Chain, Guardrail);
        var step = new ExtractStep(S3, extractor, Validator, log);

        var failures = new List<SQSBatchResponse.BatchItemFailure>();

        foreach (var message in evnt.Records)
        {
            try
            {
                var request = JsonSerializer.Deserialize<ExtractionRequest>(message.Body)
                              ?? throw new InvalidOperationException("SQS message body was not an ExtractionRequest.");

                await using var db = await ReqLensDb.OpenAsync();

                await step.RunAsync(db, request);
            }
            catch (Exception ex)
            {
                // Reported rather than thrown, so one poisoned document cannot take a batch with
                // it. The queue redelivers this message alone and eventually parks it in the DLQ.
                context.Logger.LogError($"Message {message.MessageId} failed: {ex}");
                failures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = message.MessageId });
            }
        }

        return new SQSBatchResponse { BatchItemFailures = failures };
    }
}
