using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ReqLens.Lambdas.Ingest;

/// <summary>
/// S3 object-created trigger. Runs Textract over the uploaded requisition, parks the blocks JSON
/// back in S3, creates the order shell, and enqueues extraction.
/// </summary>
/// <remarks>PAIRING STUB - this is the Lambda Glenn wires up by hand.</remarks>
public class IngestFunction
{
    public Task FunctionHandler(S3Event evnt, ILambdaContext context)
        => throw new NotImplementedException("Pairing stub: Textract + order shell + SQS enqueue.");
}
