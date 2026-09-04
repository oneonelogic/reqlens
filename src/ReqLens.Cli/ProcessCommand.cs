using Amazon.BedrockRuntime;
using Amazon.S3;
using Amazon.Textract;
using ReqLens.Ai;
using ReqLens.Data;
using ReqLens.Ocr;
using ReqLens.Pipeline;
using ReqLens.Validation;

namespace ReqLens.Cli;

/// <summary>
/// Runs the whole pipeline here rather than in AWS: upload, read, extract, validate, write.
/// </summary>
/// <remarks>
/// The same two step classes the Lambdas run, driven directly instead of by an S3 event and a
/// queue. That makes it a genuine end-to-end exercise of the logic rather than a parallel
/// implementation of it - what is skipped is the delivery mechanism, and only that.
///
/// It earns its place twice over. It fills the review queue for a demo without waiting on a
/// deploy, and when a document comes out wrong it puts a breakpoint in reach, which a Lambda
/// behind a queue does not.
/// </remarks>
public static class ProcessCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var bucket = Options.Value(args, "--bucket") ?? Workspace.Require("REQUISITIONS_BUCKET", "bucket_name");
        var verbose = args.Contains("--verbose");

        var uploads = IngestCommand.Plan(args);

        using var s3 = new AmazonS3Client();
        using var bedrock = new AmazonBedrockRuntimeClient();
        using var textract = new AmazonTextractClient();

        var log = new ConsolePipelineLog(verbose);
        var ocr = OcrProviders.FromEnvironment(textract);

        var extractor = new BedrockRequisitionExtractor(
            bedrock,
            ChainSource.Load(Options.Value(args, "--model")),
            new GuardrailOptions
            {
                GuardrailId = Workspace.Require("GUARDRAIL_ID", "guardrail_id"),
                GuardrailVersion = Workspace.Require("GUARDRAIL_VERSION", "guardrail_version")
            });

        var ingest = new IngestStep(s3, ocr, log);
        var extract = new ExtractStep(s3, extractor, new RequisitionValidator(), log);

        Console.WriteLine($"Processing {uploads.Count} document(s) locally. OCR provider: {ocr.Name}");
        Console.WriteLine();

        var failed = 0;

        foreach (var upload in uploads)
        {
            Console.WriteLine($"{Path.GetFileName(upload.Path)} -> {upload.TenantSlug}");

            try
            {
                // Uploaded even though the pipeline runs here: the review console shows the
                // original scan through a presigned URL, so the object has to exist in the
                // bucket for the order to be reviewable.
                await IngestCommand.UploadAsync(s3, bucket, upload);

                await using var db = await ReqLensDb.OpenAsync();

                var request = await ingest.RunAsync(db, bucket, upload.Key);
                await extract.RunAsync(db, request);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ! {ex.Message}");
                failed++;
            }

            Console.WriteLine();
        }

        Console.WriteLine($"{uploads.Count - failed} of {uploads.Count} processed.");

        return failed == 0 ? 0 : 1;
    }
}
