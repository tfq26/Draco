namespace Draco.Application.Models;

public sealed record SupportErrorDefinition(
    string Code,
    string Title,
    string Category,
    string Summary,
    string UserMessage,
    IReadOnlyList<string> Steps);

public static class SupportErrorCatalog
{
    public const string GenericProcessingFailure = "DRC-WA-1000";
    public const string UnknownUser = "DRC-WA-1001";
    public const string EmptyMessage = "DRC-WA-1002";
    public const string EmptyResponse = "DRC-WA-1003";
    public const string DeliveryFailed = "DRC-WA-1004";
    public const string TwilioWebhookFailure = "TWILIO-11200";

    public static IReadOnlyList<SupportErrorDefinition> All { get; } =
    [
        new(
            GenericProcessingFailure,
            "WhatsApp processing failed",
            "Messaging",
            "Draco accepted the inbound WhatsApp message but failed while generating or preparing the reply.",
            "There was a problem getting a response. Error code: DRC-WA-1000.",
            [
                "Check the API logs around the time of the message for exceptions in the messaging webhook or AI query flow.",
                "Confirm the backend can reach the AI provider and that required API keys are configured.",
                "Retry the message after the backend is healthy."
            ]),
        new(
            UnknownUser,
            "Inbound number is not linked to a Draco account",
            "Messaging",
            "Draco received the message but could not map the WhatsApp sender number to a known user account.",
            "We couldn't match this WhatsApp number to a Draco account. Error code: DRC-WA-1001.",
            [
                "Open Settings and verify the WhatsApp recipient list contains the sender number.",
                "Make sure the number is stored in E.164 format, for example +14251234567.",
                "If the user recently changed numbers, update the account profile and recipients."
            ]),
        new(
            EmptyMessage,
            "Inbound message body was empty",
            "Messaging",
            "Twilio delivered the webhook but the body field was blank or unreadable.",
            "We received an empty message. Error code: DRC-WA-1002.",
            [
                "Ask the user to resend the message with text content.",
                "If this happens repeatedly, inspect the Twilio webhook payload in the API logs.",
                "Confirm the WhatsApp sender is not sending only attachments or unsupported content."
            ]),
        new(
            EmptyResponse,
            "No response text was generated",
            "Messaging",
            "Draco completed processing but ended up with no reply body to send back to the user.",
            "There was a problem getting a response. Error code: DRC-WA-1003.",
            [
                "Check the AI response payload and command processor output for empty content.",
                "Verify the AI provider returned a valid completion.",
                "Retry after confirming the assistant service is healthy."
            ]),
        new(
            DeliveryFailed,
            "WhatsApp reply could not be delivered",
            "Messaging",
            "Draco generated a reply, but Twilio rejected or failed the outbound WhatsApp delivery.",
            "There was an error with getting a response: DRC-WA-1004.",
            [
                "Inspect Twilio message logs for the outbound reply SID and provider error.",
                "Verify the WhatsApp sender is approved and the destination number is opted in.",
                "Confirm Twilio credentials and the WhatsApp from number are configured correctly."
            ]),
        new(
            TwilioWebhookFailure,
            "Twilio could not reach the inbound webhook",
            "Integration",
            "Twilio recorded an HTTP retrieval failure while trying to post an inbound message to Draco.",
            "There was a webhook problem receiving your message. Error code: TWILIO-11200.",
            [
                "Verify the Twilio inbound webhook URL points to the stable Railway API URL.",
                "Make sure the configured path is /api/webhooks/twilio/messages.",
                "Check that the Railway deployment is up and publicly reachable over HTTPS."
            ])
    ];

    public static SupportErrorDefinition? Find(string code) =>
        All.FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));
}
