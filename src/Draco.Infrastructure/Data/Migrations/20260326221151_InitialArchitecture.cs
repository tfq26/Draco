using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Draco.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "CloudResources",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: false),
                    SubscriptionId = table.Column<string>(type: "text", nullable: false),
                    ResourceGroupName = table.Column<string>(type: "text", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RawMetadata = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudResources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CostBudgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    SubscriptionId = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    TimeGrain = table.Column<string>(type: "text", nullable: false),
                    AlertThresholdPercentage = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostBudgets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CostRecommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: false),
                    ResourceName = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    SubscriptionId = table.Column<string>(type: "text", nullable: false),
                    RecommendationType = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    PotentialSavings = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostRecommendations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ObservabilityLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RawData = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObservabilityLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ObservabilityMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: false),
                    MetricName = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<double>(type: "double precision", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Dimensions = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObservabilityMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemediationAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: false),
                    ActionType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    AIReasoning = table.Column<string>(type: "text", nullable: true),
                    PreviousState = table.Column<string>(type: "text", nullable: true),
                    NewState = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ExecutedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    PreferredChannel = table.Column<string>(type: "text", nullable: true),
                    AuthId = table.Column<string>(type: "text", nullable: true),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CloudConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    SubscriptionId = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    ExternalAccountId = table.Column<string>(type: "text", nullable: true),
                    AccessToken = table.Column<string>(type: "text", nullable: true),
                    RefreshToken = table.Column<string>(type: "text", nullable: true),
                    TokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SyncStatus = table.Column<string>(type: "text", nullable: false),
                    SyncMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudConnections_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CloudCostSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    SubscriptionId = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Granularity = table.Column<string>(type: "text", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RawData = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudCostSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudCostSnapshots_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PulseReportSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Frequency = table.Column<string>(type: "text", nullable: false),
                    IncludeCostAnalysis = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeSecurityHealth = table.Column<bool>(type: "boolean", nullable: false),
                    LastSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PulseReportSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PulseReportSchedules_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    SubscriptionId = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<string>(type: "text", nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: true),
                    ProcessingError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowEvents_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkflowType = table.Column<string>(type: "text", nullable: false),
                    Trigger = table.Column<string>(type: "text", nullable: false),
                    SuggestedAction = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    SubscriptionId = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CanAutoRun = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Recommendation = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowRuns_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowRuns_WorkflowEvents_WorkflowEventId",
                        column: x => x.WorkflowEventId,
                        principalTable: "WorkflowEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CloudConnections_UserId",
                table: "CloudConnections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudConnections_UserId_Provider_SubscriptionId",
                table: "CloudConnections",
                columns: new[] { "UserId", "Provider", "SubscriptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CloudCostSnapshots_UserId",
                table: "CloudCostSnapshots",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudCostSnapshots_UserId_Provider_SubscriptionId_PeriodEnd",
                table: "CloudCostSnapshots",
                columns: new[] { "UserId", "Provider", "SubscriptionId", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_CloudResources_Provider",
                table: "CloudResources",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_CloudResources_Type",
                table: "CloudResources",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_CostBudgets_UserId",
                table: "CostBudgets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CostBudgets_UserId_Provider_SubscriptionId_Name",
                table: "CostBudgets",
                columns: new[] { "UserId", "Provider", "SubscriptionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostRecommendations_Provider",
                table: "CostRecommendations",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_CostRecommendations_ResourceId",
                table: "CostRecommendations",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CostRecommendations_SubscriptionId",
                table: "CostRecommendations",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_ObservabilityLogs_ResourceId",
                table: "ObservabilityLogs",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ObservabilityLogs_Timestamp",
                table: "ObservabilityLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ObservabilityMetrics_ResourceId",
                table: "ObservabilityMetrics",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ObservabilityMetrics_Timestamp",
                table: "ObservabilityMetrics",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_PulseReportSchedules_UserId_IsActive",
                table: "PulseReportSchedules",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_AuthId",
                table: "UserAccounts",
                column: "AuthId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_Email",
                table: "UserAccounts",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_Phone",
                table: "UserAccounts",
                column: "Phone",
                unique: true,
                filter: "\"Phone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEvents_ReceivedAt",
                table: "WorkflowEvents",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEvents_Status",
                table: "WorkflowEvents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEvents_UserId",
                table: "WorkflowEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_CreatedAt",
                table: "WorkflowRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_Status",
                table: "WorkflowRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_UserId",
                table: "WorkflowRuns",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_WorkflowEventId",
                table: "WorkflowRuns",
                column: "WorkflowEventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CloudConnections");

            migrationBuilder.DropTable(
                name: "CloudCostSnapshots");

            migrationBuilder.DropTable(
                name: "CloudResources");

            migrationBuilder.DropTable(
                name: "CostBudgets");

            migrationBuilder.DropTable(
                name: "CostRecommendations");

            migrationBuilder.DropTable(
                name: "ObservabilityLogs");

            migrationBuilder.DropTable(
                name: "ObservabilityMetrics");

            migrationBuilder.DropTable(
                name: "PulseReportSchedules");

            migrationBuilder.DropTable(
                name: "RemediationAudits");

            migrationBuilder.DropTable(
                name: "WorkflowRuns");

            migrationBuilder.DropTable(
                name: "WorkflowEvents");

            migrationBuilder.DropTable(
                name: "UserAccounts");
        }
    }
}
