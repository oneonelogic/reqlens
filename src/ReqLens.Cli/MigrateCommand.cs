using Microsoft.EntityFrameworkCore;
using ReqLens.Data;

namespace ReqLens.Cli;

/// <summary>
/// Applies pending EF Core migrations to whatever database the environment points at.
/// </summary>
/// <remarks>
/// Exists so that updating the schema never requires a connection string on a command line.
/// `dotnet ef database update` wants REQLENS_DB_CONNECTION exported, which means the password
/// passes through a shell, a history file and whatever is watching the terminal. This resolves
/// the secret inside the process from DB_SECRET_ARN and hands it straight to Npgsql.
///
/// It reports what it is about to do before doing it: a migration list is short, and a surprise
/// in it is the last cheap moment to stop.
/// </remarks>
public static class MigrateCommand
{
    public static async Task<int> RunAsync()
    {
        await using var db = await ReqLensDb.OpenAsync();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

        Console.WriteLine($"Applied already: {applied.Count}");

        if (pending.Count == 0)
        {
            Console.WriteLine("Nothing pending; the schema is up to date.");
            return 0;
        }

        Console.WriteLine("Pending:");
        foreach (var migration in pending) Console.WriteLine($"  {migration}");

        await db.Database.MigrateAsync();

        Console.WriteLine();
        Console.WriteLine($"{pending.Count} migration(s) applied.");

        return 0;
    }
}
