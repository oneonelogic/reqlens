namespace ReqLens.Domain;

/// <summary>A test panel the lab actually offers. Extraction is validated against this, per tenant.</summary>
public class TestCatalogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string SpecimenType { get; set; }
    public bool Active { get; set; } = true;
}
