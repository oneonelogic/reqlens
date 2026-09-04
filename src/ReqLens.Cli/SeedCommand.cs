using Microsoft.EntityFrameworkCore;
using ReqLens.Data;
using ReqLens.Domain;

namespace ReqLens.Cli;

/// <summary>
/// Loads the clinics and the test catalogue into Postgres from the synthetic CSVs.
/// </summary>
/// <remarks>
/// Idempotent by natural key - slug for a clinic, (tenant, code) for a panel - so it can be run
/// against a database that already has orders in it without disturbing them. Re-seeding is the
/// normal way to pick up a catalogue change, not an exceptional repair operation.
///
/// The catalogue is the same list for every clinic here, but it is stored per tenant rather than
/// globally. A real lab negotiates a different menu with every practice, and a shared table would
/// have to be unpicked the first time that happened.
/// </remarks>
public static class SeedCommand
{
    public static async Task<int> RunAsync()
    {
        await using var db = await ReqLensDb.OpenAsync();

        var tenantRows = Csv.Rows(Path.Combine(Workspace.Synthetic, "tenants.csv"), 2).ToList();
        var catalogRows = Csv.Rows(Path.Combine(Workspace.Synthetic, "test-catalog.csv"), 4).ToList();

        var tenantsAdded = 0;
        var panelsAdded = 0;
        var panelsUpdated = 0;

        foreach (var row in tenantRows)
        {
            var (slug, name) = (row[0], row[1]);

            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug);

            if (tenant is null)
            {
                tenant = new Tenant { Slug = slug, Name = name };
                db.Tenants.Add(tenant);
                tenantsAdded++;

                // Saved per clinic so the catalogue rows below have a tenant id to hang off.
                await db.SaveChangesAsync();
            }
            else if (tenant.Name != name)
            {
                tenant.Name = name;
            }

            var existing = await db.TestCatalog
                .Where(c => c.TenantId == tenant.Id)
                .ToDictionaryAsync(c => c.Code);

            foreach (var panel in catalogRows)
            {
                var (code, panelName, specimen, active) = (panel[0], panel[1], panel[2], bool.Parse(panel[3]));

                if (existing.TryGetValue(code, out var entry))
                {
                    if (entry.Name == panelName && entry.SpecimenType == specimen && entry.Active == active)
                        continue;

                    entry.Name = panelName;
                    entry.SpecimenType = specimen;
                    entry.Active = active;
                    panelsUpdated++;
                }
                else
                {
                    db.TestCatalog.Add(new TestCatalogEntry
                    {
                        TenantId = tenant.Id,
                        Code = code,
                        Name = panelName,
                        SpecimenType = specimen,
                        Active = active
                    });

                    panelsAdded++;
                }
            }
        }

        await db.SaveChangesAsync();

        var tenants = await db.Tenants.CountAsync();
        var panels = await db.TestCatalog.CountAsync();

        Console.WriteLine($"Clinics:   {tenants} total ({tenantsAdded} added)");
        Console.WriteLine($"Catalogue: {panels} rows ({panelsAdded} added, {panelsUpdated} updated)");

        return 0;
    }
}
