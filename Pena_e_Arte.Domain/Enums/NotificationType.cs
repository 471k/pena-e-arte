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
}
