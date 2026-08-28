using Microsoft.EntityFrameworkCore;
using ReqLens.Domain;

namespace ReqLens.Data;

public class ReqLensDbContext(DbContextOptions<ReqLensDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<LabOrder> Orders => Set<LabOrder>();
    public DbSet<ExtractedField> Fields => Set<ExtractedField>();
    public DbSet<ReviewAction> Reviews => Set<ReviewAction>();
    public DbSet<TestCatalogEntry> TestCatalog => Set<TestCatalogEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tenant>(e =>
        {
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.Name).HasMaxLength(200);
            e.Property(t => t.Slug).HasMaxLength(64);
        });

        b.Entity<LabOrder>(e =>
        {
            // Every read path filters on tenant first; the index matches that access pattern.
            e.HasIndex(o => new { o.TenantId, o.Status });
            e.HasMany(o => o.Fields).WithOne().HasForeignKey(f => f.OrderId);
            e.HasMany(o => o.Reviews).WithOne().HasForeignKey(r => r.OrderId);
        });

        b.Entity<ExtractedField>(e =>
        {
            e.HasIndex(f => new { f.TenantId, f.OrderId });
            e.Property(f => f.Name).HasMaxLength(64);
        });

        b.Entity<ReviewAction>(e =>
        {
            e.HasIndex(r => new { r.TenantId, r.At });
            e.Property(r => r.ReviewerId).HasMaxLength(128);
        });

        b.Entity<TestCatalogEntry>(e =>
        {
            e.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();
            e.Property(t => t.Code).HasMaxLength(32);
        });
    }
}
