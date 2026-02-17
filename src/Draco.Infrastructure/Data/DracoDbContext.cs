using Draco.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Draco.Infrastructure.Data;

public class DracoDbContext : DbContext
{
    public DracoDbContext(DbContextOptions<DracoDbContext> options) : base(options)
    {
    }

    public DbSet<CloudResource> CloudResources => Set<CloudResource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure CloudResource mapping
        modelBuilder.Entity<CloudResource>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Provider);
            entity.HasIndex(e => e.Type);
            
            // Map Tags to JSON for easier querying if needed, or stick to simple formatting
            // For now, we'll store tags as a simple dictionary that EF can handle with JSON support in Postgres
        });
        
        // Ensure pgvector extension is enabled
        modelBuilder.HasPostgresExtension("vector");
    }
}
