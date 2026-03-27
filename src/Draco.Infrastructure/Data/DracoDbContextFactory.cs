using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Draco.Infrastructure.Data;

public sealed class DracoDbContextFactory : IDesignTimeDbContextFactory<DracoDbContext>
{
    public DracoDbContext CreateDbContext(string[] args)
    {
        var connectionString = ConnectionStringNormalizer.Normalize(
            Environment.GetEnvironmentVariable("DRACO_DB_MAIN_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DRACO_DB_CONNECTION")
            ?? TryReadConnectionStringFromDotEnv()
            ?? "Host=localhost;Database=draco;Username=postgres;Password=postgres");

        var optionsBuilder = new DbContextOptionsBuilder<DracoDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseVector());
        return new DracoDbContext(optionsBuilder.Options);
    }

    private static string? TryReadConnectionStringFromDotEnv()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Draco.Api", ".env"),
            Path.Combine(AppContext.BaseDirectory, ".env"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Draco.Api", ".env")
        };

        foreach (var candidate in candidates.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            foreach (var line in File.ReadAllLines(candidate))
            {
                if (line.StartsWith("DRACO_DB_MAIN_CONNECTION=", StringComparison.Ordinal))
                {
                    return line["DRACO_DB_MAIN_CONNECTION=".Length..].Trim();
                }
            }
        }

        return null;
    }
}
