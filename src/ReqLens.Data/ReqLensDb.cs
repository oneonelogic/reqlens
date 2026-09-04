using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.EntityFrameworkCore;

namespace ReqLens.Data;

/// <summary>
/// Builds a database context from whatever credentials the current environment offers.
/// </summary>
/// <remarks>
/// Two sources, in priority order: REQLENS_DB_CONNECTION for a laptop, and the Secrets Manager
/// secret named by DB_SECRET_ARN for anything running in AWS. No third path, and no fallback to
/// a literal - a connection string has never been in this repository and this class is where
/// that would first stop being true.
///
/// The resolved string is cached for the life of the process. Lambda reuses an execution
/// environment across invocations, so fetching the secret per invocation would add a round trip
/// and a Secrets Manager charge to every single document for a value that does not change.
/// </remarks>
public static class ReqLensDb
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _cached;

    public static async Task<string> ConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null) return _cached;

        await Gate.WaitAsync(cancellationToken);

        try
        {
            if (_cached is not null) return _cached;

            var direct = Environment.GetEnvironmentVariable("REQLENS_DB_CONNECTION");

            if (!string.IsNullOrWhiteSpace(direct))
                return _cached = direct;

            var arn = Environment.GetEnvironmentVariable("DB_SECRET_ARN")
                      ?? throw new InvalidOperationException(
                          "Neither REQLENS_DB_CONNECTION nor DB_SECRET_ARN is set; there is no way to reach the database.");

            return _cached = await FromSecretAsync(arn, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<ReqLensDbContext> OpenAsync(CancellationToken cancellationToken = default)
        => new(await OptionsAsync(cancellationToken));

    public static async Task<DbContextOptions<ReqLensDbContext>> OptionsAsync(CancellationToken cancellationToken = default)
        => new DbContextOptionsBuilder<ReqLensDbContext>()
            .UseNpgsql(await ConnectionStringAsync(cancellationToken))
            .Options;

    private static async Task<string> FromSecretAsync(string secretArn, CancellationToken cancellationToken)
    {
        using var client = new AmazonSecretsManagerClient();

        var secret = await client.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = secretArn }, cancellationToken);

        using var parsed = JsonDocument.Parse(secret.SecretString);
        var root = parsed.RootElement;

        string Read(string name) => root.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.Number ? v.GetInt32().ToString() : v.GetString() ?? ""
            : throw new InvalidOperationException($"Secret {secretArn} has no '{name}' property.");

        // Npgsql escapes nothing for you: a password containing a semicolon or a quote silently
        // truncates the connection string. The builder handles the quoting.
        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = Read("host"),
            Port = int.Parse(Read("port")),
            Database = Read("dbname"),
            Username = Read("username"),
            Password = Read("password"),
            SslMode = Npgsql.SslMode.Require,

            // The Lambda holds a warm execution environment open between invocations, and a
            // pooled connection outliving the database's idle timeout surfaces as an
            // intermittent failure on the first query of a warm start.
            ConnectionIdleLifetime = 60,
            Timeout = 15,
            CommandTimeout = 30
        };

        return builder.ConnectionString;
    }
}
