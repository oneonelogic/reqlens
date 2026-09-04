using System.Net;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.SQS;
using Amazon.SQS.Model;
using ReqLens.Data;
using ReqLens.Ocr;
using ReqLens.Pipeline;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ReqLens.Lambdas.Ingest;

/// <summary>
/// S3 object-created trigger. Reads the uploaded requisition, parks the OCR back in S3, opens the
/// order, and enqueues extraction.
/// </summary>
/// <remarks>
/// An adapter, deliberately thin: it turns an S3 event into a call on <see cref="IngestStep"/>
/// and turns the result into an SQS message. Everything worth testing lives in the step, which
/// can be exercised without constructing an S3Event or standing up a Lambda runtime.
/// </remarks>
public class IngestFunction
{
    private static readonly IAmazonS3 S3 = new AmazonS3Client();
    private static readonly IAmazonSQS Sqs = new AmazonSQSClient();

    private static readonly string QueueUrl =
        Environment.GetEnvironmentVariable("EXTRACT_QUEUE_URL")
        ?? throw new InvalidOperationException("EXTRACT_QUEUE_URL is not set.");

    public async Task FunctionHandler(S3Event evnt, ILambdaContext context)
    {
        var log = new LambdaPipelineLog(context);
        var step = new IngestStep(S3, OcrProviders.FromEnvironment(), log);

        foreach (var record in evnt.Records)
        {
            var bucket = record.S3.Bucket.Name;

            // S3 URL-encodes the key in the event, and encodes spaces as '+' rather than %20.
            var key = WebUtility.UrlDecode(record.S3.Object.Key.Replace("+", " "));

            context.Logger.LogInformation($"Ingesting s3://{bucket}/{key}");

            await using var db = await ReqLensDb.OpenAsync();

            var request = await step.RunAsync(db, bucket, key);

            await Sqs.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = QueueUrl,
                MessageBody = JsonSerializer.Serialize(request)
            });

            context.Logger.LogInformation($"Order {request.OrderId} queued for extraction.");
        }
    }
}
