namespace Pena_e_Arte.Domain.Enums;

public enum NotificationRecipientType
{
    Client,
    Studio,
    Artist,
    // Recipient has no Client record at all — a manual reminder sent to a raw phone number
    // the artist typed in, with no platform record created. See ManualReminder.ClientId (null).
    ExternalContact
}
