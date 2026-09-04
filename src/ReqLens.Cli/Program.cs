// ReqLens CLI - the Slice 1 driver and the golden-set eval harness.
//
//   reqlens migrate                                  bring the database schema up to date
//   reqlens seed                                     load clinics and the test catalogue
//   reqlens ingest <pdf...> [--tenant <slug>]        upload requisitions and start the pipeline
//   reqlens ingest --all                             upload all twenty, each to its own clinic
//   reqlens process <pdf...|--all>                   run the whole pipeline locally, no Lambdas
//   reqlens orders [--tenant <slug>]                 what the pipeline has produced so far
//   reqlens eval [--model <id>] [--limit n]          grade the golden set, report precision/recall

using ReqLens.Cli;

// Bucket, guardrail and secret ARN come from Terraform state unless already exported.
Workspace.ApplyEnvironment();

var command = args.FirstOrDefault();

try
{
    return command switch
    {
        "migrate" => await MigrateCommand.RunAsync(),
        "seed" => await SeedCommand.RunAsync(),
        "ingest" => await IngestCommand.RunAsync(args),
        "process" => await ProcessCommand.RunAsync(args),
        "orders" => await OrdersCommand.RunAsync(args),
        "eval" => await EvalCommand.RunAsync(args),
        null or "" or "help" or "--help" => Usage(0),
        _ => Usage(1, $"Unknown command '{command}'.")
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int Usage(int exitCode, string? message = null)
{
    if (message is not null) Console.Error.WriteLine(message);

    var output = exitCode == 0 ? Console.Out : Console.Error;

    output.WriteLine("usage: reqlens <command> [options]");
    output.WriteLine();
    output.WriteLine("  migrate                               bring the database schema up to date");
    output.WriteLine("  seed                                  load clinics and the test catalogue");
    output.WriteLine("  ingest <pdf...> [--tenant <slug>]     upload requisitions and start the pipeline");
    output.WriteLine("  ingest --all                          upload all twenty, each to its own clinic");
    output.WriteLine("  process <pdf...|--all>                run the whole pipeline locally, no Lambdas");
    output.WriteLine("  orders [--tenant <slug>]              what the pipeline has produced so far");
    output.WriteLine("  eval [--model <id>] [--limit n]       grade the golden set");
    output.WriteLine();
    output.WriteLine("Bucket, guardrail and database settings come from the environment, and fall");
    output.WriteLine("back to the Terraform state in infra/terraform.");

    return exitCode;
}
