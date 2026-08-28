using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ReqLens.Lambdas.Extract;

/// <summary>
/// SQS-triggered. Loads the OCR blocks, runs schema-constrained extraction through the model
/// chain, applies the deterministic validators and the confidence gate, writes the order and its
/// fields, and emits per-call telemetry.
/// </summary>
public class ExtractFunction
{
    public Task FunctionHandler(SQSEvent evnt, ILambdaContext context)
        => throw new NotImplementedException("Slice 1: wire extractor -> validators -> gate -> Postgres.");
}
