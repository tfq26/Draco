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
    public IReadOnlyList<string> MessagesNumbers { get; init; } = [];
    public bool WhatsAppEnabled { get; init; }
    public IReadOnlyList<string> WhatsAppNumbers { get; init; } = [];
}

public static class NotificationDeliveryPreferencesSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static NotificationDeliveryPreferences Resolve(UserAccount account)
    {
        var normalizedPreferredChannels = SplitChannels(account.PreferredChannel);
        var fallbackEmail = NormalizeValue(account.Email);
        var fallbackPhone = NormalizeValue(account.Phone);
        var smsRecipients = DeserializeRecipients(account.SmsRecipientsJson);
        var whatsAppRecipients = DeserializeRecipients(account.WhatsAppRecipientsJson);
        var messagesNumbers = MergeRecipients(smsRecipients, fallbackPhone);
        var whatsAppNumbers = MergeRecipients(whatsAppRecipients, fallbackPhone);

        return new NotificationDeliveryPreferences
        {
            BrowserEnabled = true,
            EmailEnabled = normalizedPreferredChannels.Contains(NotificationChannelNames.Email)
                && !string.IsNullOrWhiteSpace(fallbackEmail),
            EmailAddress = fallbackEmail,
            MessagesEnabled = normalizedPreferredChannels.Contains(NotificationChannelNames.Messages)
                && messagesNumbers.Count > 0,
            MessagesNumbers = messagesNumbers,
            WhatsAppEnabled = normalizedPreferredChannels.Contains(NotificationChannelNames.WhatsApp)
                && whatsAppNumbers.Count > 0,
            WhatsAppNumbers = whatsAppNumbers
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

    private static HashSet<string> SplitChannels(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                NotificationChannelNames.Messages
            };
        }

        return channel
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeChannelName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeChannelName(string channel) =>
        channel.Trim().ToUpperInvariant() switch
        {
            "SMS" => NotificationChannelNames.Messages,
            "MESSAGE" => NotificationChannelNames.Messages,
            "MESSAGES" => NotificationChannelNames.Messages,
            "WHATSAPP" => NotificationChannelNames.WhatsApp,
            "EMAIL" => NotificationChannelNames.Email,
            "BROWSER" => NotificationChannelNames.Browser,
            _ => channel.Trim()
        };

    private static List<string> DeserializeRecipients(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions)?
                .Select(NormalizeValue)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> MergeRecipients(IEnumerable<string> configuredRecipients, string? fallbackRecipient)
    {
        var merged = configuredRecipients
            .Select(NormalizeValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(fallbackRecipient))
        {
            merged.Add(fallbackRecipient);
        }

        return merged
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
