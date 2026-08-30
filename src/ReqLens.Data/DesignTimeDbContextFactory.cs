using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ReqLens.Data;

/// <summary>
/// Lets `dotnet ef` build the context without a running application.
///
/// Migrations are generated offline, so the connection string here only has to be well formed -
/// it is never dialled during `migrations add`. `database update` does connect, and takes the
/// real value from REQLENS_DB_CONNECTION so no credential is ever committed.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ReqLensDbContext>
{
    public ReqLensDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("REQLENS_DB_CONNECTION")
                         ?? "Host=localhost;Database=reqlens;Username=design_time;Password=design_time";

        var options = new DbContextOptionsBuilder<ReqLensDbContext>()
            .UseNpgsql(connection)
            .Options;

        return new ReqLensDbContext(options);
    }
}
