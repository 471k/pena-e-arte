namespace Pena_e_Arte.Contracts.Responses.Public;

/// <summary>
/// Deliberately the same shape and content whether the booking actually succeeded or the
/// supplied email collided with an existing platform account (Decision #3 revisited,
/// 2026-09-01, /code-review finding) — the HTTP response must never reveal whether an email
/// has an account. Disambiguation happens only out-of-band, via email, mirroring
/// ForgotPasswordHandler's existing enumeration-resistant pattern.
/// </summary>
public record GuestBookingAckResponse(string Message);
