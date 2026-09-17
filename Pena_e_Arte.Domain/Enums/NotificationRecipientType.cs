namespace Pena_e_Arte.Domain.Enums;

public enum NotificationRecipientType
{
    Client,
    Studio,
    Artist,
    // Recipient has no Client record at all — a manual reminder sent to a raw phone number
    // the artist typed in, with no platform record created. See ManualReminder.ClientId (null).
    ExternalContact,
    // A platform-level notice concerning a studio (not addressed to a specific admin user) —
    // RecipientId is the studio's own Id, mirroring the Studio case's semantics. See
    // SendStudioRegisteredNotificationCommand.
    Admin
}
