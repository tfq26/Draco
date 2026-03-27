using Draco.Domain.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;

namespace Draco.Infrastructure.Data;

public class DracoDbContext : DbContext
{
    private static readonly ValueComparer<IDictionary<string, string>> DictionaryValueComparer =
        new(
            (left, right) => DictionariesEqual(left, right),
            value => GetDictionaryHashCode(value),
            value => value == null
                ? new Dictionary<string, string>()
                : value.ToDictionary(entry => entry.Key, entry => entry.Value));

    public DracoDbContext(DbContextOptions<DracoDbContext> options) : base(options)
    {
    }

    public DbSet<CloudResource> CloudResources => Set<CloudResource>();
    public DbSet<RemediationAudit> RemediationAudits => Set<RemediationAudit>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<CloudConnection> CloudConnections => Set<CloudConnection>();
    public DbSet<PulseReportSchedule> PulseReportSchedules => Set<PulseReportSchedule>();
    public DbSet<CostBudget> CostBudgets => Set<CostBudget>();
    public DbSet<CloudCostSnapshot> CloudCostSnapshots => Set<CloudCostSnapshot>();
    public DbSet<CloudResourceCost> CloudResourceCosts => Set<CloudResourceCost>();
    public DbSet<CostRecommendation> CostRecommendations => Set<CostRecommendation>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<ObservabilityMetric> ObservabilityMetrics => Set<ObservabilityMetric>();
    public DbSet<ObservabilityLog> ObservabilityLogs => Set<ObservabilityLog>();
    public DbSet<SystemNotification> SystemNotifications => Set<SystemNotification>();

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
                )
                .Metadata.SetValueComparer(DictionaryValueComparer);
        });

        // Configure UserAccount
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Phone)
                .IsUnique()
                .HasFilter("\"Phone\" IS NOT NULL");
            entity.HasIndex(e => e.AuthId).IsUnique();
            entity.HasIndex(e => e.Email);
            entity.HasMany(e => e.Connections)
                  .WithOne(c => c.User)
                  .HasForeignKey(c => c.UserId);
            entity.HasMany(e => e.ReportSchedules)
                  .WithOne(s => s.User)
                  .HasForeignKey(s => s.UserId);
        });

        // Configure CloudConnection
        modelBuilder.Entity<CloudConnection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.Provider, e.SubscriptionId }).IsUnique();
        });

        modelBuilder.Entity<PulseReportSchedule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.IsActive });
        });

        modelBuilder.Entity<CostBudget>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.Provider, e.SubscriptionId, e.Name, e.BudgetSource }).IsUnique();
        });

        modelBuilder.Entity<CloudCostSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.Provider, e.SubscriptionId, e.PeriodEnd });
        });

        modelBuilder.Entity<CloudResourceCost>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ResourceId);
            entity.HasIndex(e => new { e.UserId, e.Provider, e.SubscriptionId, e.ResourceGroupName, e.PeriodEnd });
            entity.HasIndex(e => new { e.UserId, e.ResourceId, e.PeriodEnd });
        });

        modelBuilder.Entity<CostRecommendation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Provider);
            entity.HasIndex(e => e.SubscriptionId);
            entity.HasIndex(e => e.ResourceId);
        });

        modelBuilder.Entity<WorkflowEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ReceivedAt);
        });

        modelBuilder.Entity<WorkflowRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasOne(e => e.WorkflowEvent)
                .WithMany()
                .HasForeignKey(e => e.WorkflowEventId)
                .OnDelete(DeleteBehavior.SetNull);
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
                )
                .Metadata.SetValueComparer(DictionaryValueComparer);
        });

        // Configure ObservabilityLog
        modelBuilder.Entity<ObservabilityLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ResourceId);
            entity.HasIndex(e => e.Timestamp);
        });

        // Configure SystemNotification
        modelBuilder.Entity<SystemNotification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.IsRead);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.UserId, e.NotificationKey }).IsUnique();
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Ensure pgvector extension is enabled
        modelBuilder.HasPostgresExtension("vector");
    }

    private static bool DictionariesEqual(IDictionary<string, string>? left, IDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var entry in left)
        {
            if (!right.TryGetValue(entry.Key, out var rightValue) || rightValue != entry.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static int GetDictionaryHashCode(IDictionary<string, string>? dictionary)
    {
        if (dictionary is null || dictionary.Count == 0)
        {
            return 0;
        }

        var hash = new HashCode();
        foreach (var entry in dictionary.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            hash.Add(entry.Key, StringComparer.Ordinal);
            hash.Add(entry.Value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
