namespace ReqLens.Domain;

/// <summary>A partner clinic. Every other row hangs off a tenant; the API layer scopes by it.</summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
