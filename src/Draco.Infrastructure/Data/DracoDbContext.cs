using Draco.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Draco.Infrastructure.Data;

public class DracoDbContext : DbContext
{
    public DracoDbContext(DbContextOptions<DracoDbContext> options) : base(options)
    {
    }

    public DbSet<CloudResource> CloudResources => Set<CloudResource>();
    public DbSet<RemediationAudit> RemediationAudits => Set<RemediationAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure CloudResource mapping
        modelBuilder.Entity<CloudResource>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Provider);
            entity.HasIndex(e => e.Type);
            
            // Map Tags to JSON string for Postgres compatibility
            entity.Property(e => e.Tags)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<IDictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new Dictionary<string, string>()
                );
        });
        
        // Ensure pgvector extension is enabled
        modelBuilder.HasPostgresExtension("vector");
    }
}
