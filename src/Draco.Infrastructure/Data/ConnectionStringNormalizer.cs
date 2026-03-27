using Npgsql;

namespace Draco.Infrastructure.Data;

public static class ConnectionStringNormalizer
{
    public static string Normalize(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "Host=localhost;Database=draco;Username=postgres;Password=postgres";
        }

        var trimmed = connectionString.Trim();

        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var uri = new Uri(trimmed);
        var userInfo = uri.UserInfo.Split(':', 2, StringSplitOptions.TrimEntries);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty,
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty
        };

        foreach (var segment in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;

            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            switch (key.ToLowerInvariant())
            {
                case "sslmode":
                    builder.SslMode = Enum.TryParse<SslMode>(value, true, out var sslMode)
                        ? sslMode
                        : builder.SslMode;
                    break;
                case "channel_binding":
                    builder.ChannelBinding = Enum.TryParse<ChannelBinding>(value, true, out var channelBinding)
                        ? channelBinding
                        : builder.ChannelBinding;
                    break;
                default:
                    builder[key] = value;
                    break;
            }
        }

        return builder.ConnectionString;
    }
}
