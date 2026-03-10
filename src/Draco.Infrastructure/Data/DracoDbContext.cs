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
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<CloudConnection> CloudConnections => Set<CloudConnection>();
    public DbSet<PulseReportSchedule> PulseReportSchedules => Set<PulseReportSchedule>();
    public DbSet<CostBudget> CostBudgets => Set<CostBudget>();
    public DbSet<CostRecommendation> CostRecommendations => Set<CostRecommendation>();
    public DbSet<ObservabilityMetric> ObservabilityMetrics => Set<ObservabilityMetric>();
    public DbSet<ObservabilityLog> ObservabilityLogs => Set<ObservabilityLog>();

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

        // Configure UserAccount
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(e => e.Phone);
            entity.HasMany(e => e.Connections)
                  .WithOne()
                  .HasForeignKey(c => c.UserPhone);
            entity.HasMany(e => e.ReportSchedules)
                  .WithOne()
                  .HasForeignKey(s => s.UserPhone);
        });

        // Configure CloudConnection
        modelBuilder.Entity<CloudConnection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserPhone);
        });

        // Configure ObservabilityMetric
        modelBuilder.Entity<ObservabilityMetric>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ResourceId);
            entity.HasIndex(e => e.Timestamp);
            entity.Property(e => e.Dimensions)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<IDictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new Dictionary<string, string>()
                );
        });

        // Configure ObservabilityLog
        modelBuilder.Entity<ObservabilityLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ResourceId);
            entity.HasIndex(e => e.Timestamp);
        });

        // Ensure pgvector extension is enabled
        modelBuilder.HasPostgresExtension("vector");
    }
}
