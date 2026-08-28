// ReqLens CLI - the Slice 1 driver and, later, the golden-set eval harness.
//
//   reqlens ingest <path-to-pdf> [--tenant <slug>]   upload a synthetic requisition and watch it land
//   reqlens eval   [--model <id>]                    run the golden set, report field precision/recall

var command = args.FirstOrDefault();

switch (command)
{
    case "ingest":
        Console.Error.WriteLine("Not implemented yet (Slice 1).");
        return 1;

    case "eval":
        Console.Error.WriteLine("Not implemented yet (Slice 5).");
        return 1;

    default:
        Console.WriteLine("usage: reqlens <ingest|eval> [options]");
        return string.IsNullOrEmpty(command) ? 0 : 1;
}
