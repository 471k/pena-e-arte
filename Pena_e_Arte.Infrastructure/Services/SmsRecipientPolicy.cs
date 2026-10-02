using Microsoft.Extensions.Configuration;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Thrown by <see cref="NotificationService.SendSmsAsync"/> when the environment restricts SMS to an
/// allow-list and the recipient is not on it. Deliberately carries no phone number.
/// </summary>
public sealed class SmsRecipientNotAllowedException()
    : Exception("The SMS recipient is not on this environment's allow-list.");

/// <summary>
/// Optional recipient allow-list for SMS, meant for non-production environments. A test environment must
/// never text a number nobody has agreed to receive messages on: seed or load-test data routinely carries
/// well-formed phone numbers, and the SMS provider will happily deliver to a real stranger who owns one.
///
/// Off unless <c>Sms:RestrictToAllowList</c> is true, so production behaves exactly as before. When on, it
/// fails closed: a recipient is allowed only if it appears in <c>Sms:AllowedRecipients</c> (comma or
/// semicolon separated E.164 numbers), and an empty or missing list allows nothing.
/// </summary>
public static class SmsRecipientPolicy
{
    public const string RestrictKey = "Sms:RestrictToAllowList";
    public const string AllowedKey = "Sms:AllowedRecipients";

    public static bool IsAllowed(IConfiguration configuration, string recipient)
    {
        if (!bool.TryParse(configuration[RestrictKey], out bool restricted) || !restricted)
            return true;

        string normalizedRecipient = Normalize(recipient);
        if (normalizedRecipient.Length == 0)
            return false;

        string allowedRaw = configuration[AllowedKey] ?? string.Empty;
        foreach (string entry in allowedRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string normalizedEntry = Normalize(entry);
            if (normalizedEntry.Length > 0 && string.Equals(normalizedEntry, normalizedRecipient, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Keeps digits and a leading '+', so "+355 69 244 1454" and "+355692441454" compare equal.</summary>
    private static string Normalize(string value) =>
        new string(value.Where(ch => char.IsDigit(ch) || ch == '+').ToArray());
}
