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
    public DbSet<ExtractionCall> ExtractionCalls => Set<ExtractionCall>();

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

            e.HasOne<Tenant>()
             .WithMany()
             .HasForeignKey(o => o.TenantId)
             .OnDelete(DeleteBehavior.Restrict);

            // Children carry TenantId of their own so tenant-scoped reads never need a join.
            // That denormalisation is only safe if the database refuses to let the two disagree,
            // so the key they point at is (TenantId, Id), not Id alone: a field claiming ClinicA
            // cannot attach to an order owned by ClinicB, because no such parent row exists.
            e.HasAlternateKey(o => new { o.TenantId, o.Id });

            e.HasMany(o => o.Fields)
             .WithOne()
             .HasForeignKey(f => new { f.TenantId, f.OrderId })
             .HasPrincipalKey(o => new { o.TenantId, o.Id })
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(o => o.Reviews)
             .WithOne()
             .HasForeignKey(r => new { r.TenantId, r.OrderId })
             .HasPrincipalKey(o => new { o.TenantId, o.Id })
             .OnDelete(DeleteBehavior.Restrict); // audit rows outlive nothing; never cascade them away

            e.HasMany(o => o.Calls)
             .WithOne()
             .HasForeignKey(c => new { c.TenantId, c.OrderId })
             .HasPrincipalKey(o => new { o.TenantId, o.Id })
             .OnDelete(DeleteBehavior.Cascade); // telemetry is about the order, not about the lab
        });

        b.Entity<ExtractionCall>(e =>
        {
            e.HasIndex(c => new { c.TenantId, c.At });
            e.Property(c => c.ModelId).HasMaxLength(128);
            e.Property(c => c.Role).HasMaxLength(32);

            // Fractions of a cent per call, summed over a month. numeric, never a float:
            // a cost total that drifts is a cost total nobody trusts.
            e.Property(c => c.EstimatedCostUsd).HasPrecision(12, 6);
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

            e.HasOne<Tenant>()
             .WithMany()
             .HasForeignKey(t => t.TenantId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
