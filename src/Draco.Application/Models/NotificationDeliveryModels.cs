using System.Text.Json;
using Draco.Domain.Entities;

namespace Draco.Application.Models;

public static class NotificationChannelNames
{
    public const string Browser = "Browser";
    public const string Email = "Email";
    public const string Messages = "Messages";
    public const string WhatsApp = "WhatsApp";
}

public sealed class NotificationDeliveryPreferences
{
    public bool BrowserEnabled { get; init; } = true;
    public bool EmailEnabled { get; init; }
    public string? EmailAddress { get; init; }
    public bool MessagesEnabled { get; init; }
    public string? MessagesNumber { get; init; }
    public bool WhatsAppEnabled { get; init; }
    public string? WhatsAppNumber { get; init; }
}

public static class NotificationDeliveryPreferencesSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static NotificationDeliveryPreferences Resolve(UserAccount account)
    {
        var stored = Deserialize(account.NotificationPreferencesJson);
        var normalizedPreferredChannel = NormalizeChannel(account.PreferredChannel);
        var fallbackEmail = NormalizeValue(account.Email);
        var fallbackPhone = NormalizeValue(account.Phone);

        return new NotificationDeliveryPreferences
        {
            BrowserEnabled = stored?.BrowserEnabled ?? true,
            EmailEnabled = stored?.EmailEnabled ?? false,
            EmailAddress = NormalizeValue(stored?.EmailAddress) ?? fallbackEmail,
            MessagesEnabled = stored?.MessagesEnabled
                ?? (string.Equals(normalizedPreferredChannel, NotificationChannelNames.Messages, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(fallbackPhone)),
            MessagesNumber = NormalizeValue(stored?.MessagesNumber) ?? fallbackPhone,
            WhatsAppEnabled = stored?.WhatsAppEnabled
                ?? (string.Equals(normalizedPreferredChannel, NotificationChannelNames.WhatsApp, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(fallbackPhone)),
            WhatsAppNumber = NormalizeValue(stored?.WhatsAppNumber) ?? fallbackPhone
        };
    }

    public static string Serialize(NotificationDeliveryPreferences preferences) =>
        JsonSerializer.Serialize(preferences, JsonOptions);

    public static NotificationDeliveryPreferences? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<NotificationDeliveryPreferences>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return null;
        }

        return channel.Trim().ToUpperInvariant() switch
        {
            "SMS" => NotificationChannelNames.Messages,
            "MESSAGE" => NotificationChannelNames.Messages,
            "MESSAGES" => NotificationChannelNames.Messages,
            "WHATSAPP" => NotificationChannelNames.WhatsApp,
            "EMAIL" => NotificationChannelNames.Email,
            "BROWSER" => NotificationChannelNames.Browser,
            _ => channel.Trim()
        };
    }
}
