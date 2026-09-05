using System.Text.RegularExpressions;

namespace Pena_e_Arte.Application.Common;

/// <summary>
/// The canonical ITU E.164 phone shape, shared by every validator that accepts a phone number
/// (CreateClientValidator, CreateManualReminderValidator, UpdateMyStudioValidator,
/// CreateGuestAppointmentValidator). Was independently redeclared as an identical private
/// `Regex E164Format` field in all four — found via /code-review, 2026-09-01.
/// </summary>
public static class PhoneValidationRules
{
    public static readonly Regex E164Format = new(@"^\+[1-9]\d{1,14}$", RegexOptions.Compiled);

    public const string E164ErrorMessage = "Phone must be in international format, e.g. +351912345678.";
}
