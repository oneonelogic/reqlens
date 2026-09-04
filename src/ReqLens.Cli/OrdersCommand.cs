using Microsoft.EntityFrameworkCore;
using ReqLens.Data;

namespace ReqLens.Cli;

/// <summary>
/// What the pipeline has produced, from the terminal.
/// </summary>
/// <remarks>
/// The review console is the real interface, but a query that does not depend on the browser,
/// the API or the SPA build is worth having when the question is "did the Lambda write anything
/// at all". It has answered that question faster than CloudWatch every time so far.
/// </remarks>
public static class OrdersCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var slug = Options.Value(args, "--tenant");

        await using var db = await ReqLensDb.OpenAsync();

        var query = db.Orders.AsQueryable();

        if (slug is not null)
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug);

            if (tenant is null)
            {
                Console.Error.WriteLine($"No clinic with slug '{slug}'.");
                return 1;
            }

            query = query.Where(o => o.TenantId == tenant.Id);
        }

        var tenants = await db.Tenants.ToDictionaryAsync(t => t.Id, t => t.Slug);

        var orders = await query
            .OrderByDescending(o => o.UpdatedAt)
            .Take(50)
            .Select(o => new
            {
                o.Id,
                o.TenantId,
                o.SourceObjectKey,
                o.Status,
                o.OverallConfidence,
                o.ModelId,
                o.ReviewReasons,
                o.UpdatedAt
            })
            .ToListAsync();

        if (orders.Count == 0)
        {
            Console.WriteLine("No orders yet.");
            return 0;
        }

        Console.WriteLine($"{"DOCUMENT",-16} {"CLINIC",-12} {"STATUS",-12} {"CONF",5}  WHY");

        foreach (var order in orders)
        {
            var reason = order.ReviewReasons.FirstOrDefault() ?? "-";
            if (reason.Length > 64) reason = reason[..61] + "...";

            Console.WriteLine(
                $"{Path.GetFileName(order.SourceObjectKey),-16} "
                + $"{tenants.GetValueOrDefault(order.TenantId, "?"),-12} "
                + $"{order.Status,-12} "
                + $"{(order.OverallConfidence is { } c ? c.ToString("P0") : "-"),5}  "
                + reason);
        }

        Console.WriteLine();
        Console.WriteLine($"{orders.Count} order(s).");

        return 0;
    }
}
