namespace Pena_e_Arte.Domain.Enums;

public enum NotificationType
{
    AppointmentCreated,
    AppointmentConfirmed,
    AppointmentCancelled,
    DepositCaptured,
    PaymentRefunded,
    IntakeFormSubmitted,
    ConsentFormSigned,
    DesignReviewed,
    Aftercare,
    MessageReceived, // in-app messaging — Email channel only, see architecture.md Decisions Log
    WaitlistSlotAvailable,
    DepositHoldLost, // owner only, Email channel only — the card hold on a deposit ended before it was charged (C-15)
}
