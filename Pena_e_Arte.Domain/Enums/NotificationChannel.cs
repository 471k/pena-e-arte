namespace Pena_e_Arte.Domain.Enums;

public enum NotificationChannel
{
    Email,
    Sms,
    // Platform-generated notices with no email/SMS equivalent (e.g. an issuer
    // generating a referral code on a studio's behalf) — bell/log only, never
    // routed through the per-event email/SMS opt-in preferences.
    InApp
}
